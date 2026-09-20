using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Доклейка промежуточных к строкам, которые уже в базе (план
/// docs/plans/splits-attach-without-repull-plan.md, решение Влада 20.09.2026).
///
/// Пишет РОВНО два поля — <c>RelayMembers.SplitTime</c> и <c>Results.TimeSplit</c>. Ни одной
/// строки не создаёт и не удаляет, мест, пола, категории и состава не трогает: именно этим
/// доклейка отличается от переимпорта, который при разъехавшемся ключе upsert заводит второй
/// комплект соревнования (1581, И-28).
///
/// Правило сопоставления живёт в <see cref="SplitAttachMatcher"/> (чистая функция) — здесь
/// только чтение строк, вызов источника и запись.
/// </summary>
public class SplitAttachService : ISplitAttachService
{
    private readonly SwimmDbContext _db;
    private readonly ISplitSourceProvider _source;
    private readonly ILogger<SplitAttachService> _logger;

    public SplitAttachService(
        SwimmDbContext db, ISplitSourceProvider source, ILogger<SplitAttachService> logger)
    {
        _db = db;
        _source = source;
        _logger = logger;
    }

    public async Task<SplitAttachResult> AttachAsync(
        int competitionId, int? logligId, bool apply, CancellationToken ct = default)
    {
        var comp = await _db.Competitions.AsNoTracking()
            .Where(c => c.Id == competitionId)
            .Select(c => new { c.Id, c.EventId, c.OrgCompId })
            .FirstOrDefaultAsync(ct);
        if (comp is null) return SplitAttachResult.Failed($"Соревнования #{competitionId} нет");

        // Промежуточные качаются на ВЕСЬ турнир, а строки лежат по дням (решение §6-2):
        // раскладываем за один заход по всем дням события, иначе день легко забыть.
        var days = await _db.Competitions.AsNoTracking()
            .Where(c => comp.EventId != null ? c.EventId == comp.EventId : c.Id == comp.Id)
            .Select(c => new { c.Id, c.Name, c.Date })
            .ToListAsync(ct);
        var dayIds = days.Select(d => d.Id).ToList();

        // LogligId: параметром (у склеенных стартов строки discovery нет) или из discovery
        // по OrgCompId любого дня события (решение §6-1).
        var orgIds = await _db.Competitions.AsNoTracking()
            .Where(c => dayIds.Contains(c.Id) && c.OrgCompId != null)
            .Select(c => c.OrgCompId!.Value)
            .ToListAsync(ct);
        var resolved = logligId ?? await _db.DiscoveredCompetitions.AsNoTracking()
            .Where(d => orgIds.Contains(d.OrgCompId) && d.LogligId != null)
            .Select(d => d.LogligId)
            .FirstOrDefaultAsync(ct);
        if (resolved is not int loglig || loglig <= 0)
            return SplitAttachResult.Failed(
                "LogligId не найден: у соревнования нет строки discovery — передай его параметром");

        var (relayRows, relayDay) = await ReadRelayRowsAsync(dayIds, ct);
        var (swimRows, swimDay) = await ReadSwimRowsAsync(dayIds, ct);

        SplitSource source;
        try
        {
            source = await _source.FetchAsync(loglig, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Промежуточные loglig {LogligId} не прочитаны", loglig);
            return SplitAttachResult.Failed($"промежуточные не прочитаны: {ex.Message}");
        }

        var plan = SplitAttachMatcher.Build(relayRows, swimRows, source.Teams, source.Swims);

        // Разбивка по дням — по картам «нога/строка → день»: сам матчер про дни не знает
        // и знать не должен.
        var dayRows = days.Select(d => new SplitAttachDay(
                d.Id, d.Name, d.Date,
                plan.Legs.Count(w => relayDay.GetValueOrDefault(w.MemberId) == d.Id),
                plan.Swims.Count(w => swimDay.GetValueOrDefault(w.ResultId) == d.Id)))
            .ToList();

        if (apply && (plan.Legs.Count > 0 || plan.Swims.Count > 0))
            await WriteAsync(plan, dayRows, ct);

        var message = apply
            ? $"{source.Message}. {plan.Report}"
            : $"{source.Message}. СУХОЙ ПРОГОН (ничего не записано). {plan.Report}";
        return new SplitAttachResult(apply, loglig, dayRows, plan.Report, message);
    }

    /// <summary>
    /// Строки эстафет со структурным составом (ноги + годы рождения) и карта «нога → день»:
    /// по ней отчёт раскладывается по дням многодневки.
    /// </summary>
    private async Task<(List<RelayRow> Rows, Dictionary<int, int> LegDay)> ReadRelayRowsAsync(
        List<int> dayIds, CancellationToken ct)
    {
        var rows = await _db.Results.AsNoTracking()
            .Where(r => dayIds.Contains(r.CompetitionId) && r.RelayId != null)
            .Select(r => new
            {
                r.Id, r.CompetitionId, RelayId = r.RelayId!.Value,
                Style = r.Style.Name, r.Distance, r.Heat, r.Lane, r.TimeOriginal,
            })
            .ToListAsync(ct);
        if (rows.Count == 0) return ([], []);

        var relayIds = rows.Select(r => r.RelayId).Distinct().ToList();
        var legs = (await _db.RelayMembers.AsNoTracking()
                .Where(m => relayIds.Contains(m.RelayId))
                .Select(m => new { m.RelayId, m.Id, m.LegOrder, m.Swimmer.BirthYear, m.SplitTime })
                .ToListAsync(ct))
            .GroupBy(m => m.RelayId)
            .ToDictionary(g => g.Key, g => g
                .Select(m => new RelayLegRow(m.Id, m.LegOrder, m.BirthYear, m.SplitTime))
                .ToList());

        var built = rows
            .Select(r => new RelayRow(
                r.Id, r.RelayId, r.Style, r.Distance, r.Heat, r.Lane, r.TimeOriginal,
                legs.GetValueOrDefault(r.RelayId, [])))
            .ToList();
        var legDay = rows
            .SelectMany(r => legs.GetValueOrDefault(r.RelayId, []).Select(l => (l.MemberId, r.CompetitionId)))
            .ToDictionary(x => x.MemberId, x => x.CompetitionId);
        return (built, legDay);
    }

    /// <summary>Строки личных заплывов и карта «строка → день» (см. ReadRelayRowsAsync).</summary>
    private async Task<(List<SwimRow> Rows, Dictionary<long, int> SwimDay)> ReadSwimRowsAsync(
        List<int> dayIds, CancellationToken ct)
    {
        var rows = await _db.Results.AsNoTracking()
            .Where(r => dayIds.Contains(r.CompetitionId) && r.RelayId == null)
            .Select(r => new
            {
                r.Id, r.CompetitionId, Style = r.Style.Name, r.Distance, r.Gender,
                r.TimeOriginal, r.Swimmer.BirthYear, r.TimeSplit,
            })
            .ToListAsync(ct);

        return (
            rows.Select(r => new SwimRow(
                r.Id, r.Style, r.Distance, r.Gender, r.TimeOriginal, r.BirthYear, r.TimeSplit)).ToList(),
            rows.ToDictionary(r => r.Id, r => r.CompetitionId));
    }

    /// <summary>
    /// Запись одной транзакцией: доклейка — это одно решение, а не россыпь мелких правок.
    /// <c>HasSplits</c> ставим только тем дням, где промежуточные реально появились.
    /// </summary>
    private async Task WriteAsync(SplitAttachPlan plan, List<SplitAttachDay> days, CancellationToken ct)
    {
        // Execution strategy обязательна: ручная транзакция при retry-стратегии соединения
        // иначе бросает «does not support user-initiated transactions» (тот же приём, что в
        // JsonImportService и HubGroupUserService).
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(() => WriteInTransactionAsync(plan, days, ct));
    }

    private async Task WriteInTransactionAsync(
        SplitAttachPlan plan, List<SplitAttachDay> days, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        if (plan.Legs.Count > 0)
        {
            var ids = plan.Legs.Select(w => w.MemberId).ToList();
            var members = await _db.RelayMembers.Where(m => ids.Contains(m.Id)).ToListAsync(ct);
            var byId = plan.Legs.ToDictionary(w => w.MemberId, w => w.SplitTime);
            foreach (var m in members) m.SplitTime = byId[m.Id];
        }

        if (plan.Swims.Count > 0)
        {
            var ids = plan.Swims.Select(w => w.ResultId).ToList();
            var results = await _db.Results.Where(r => ids.Contains(r.Id)).ToListAsync(ct);
            var byId = plan.Swims.ToDictionary(w => w.ResultId, w => w.TimeSplit);
            foreach (var r in results) r.TimeSplit = byId[r.Id];
        }

        var touched = days.Where(d => d.LegWrites > 0 || d.SwimWrites > 0).Select(d => d.CompetitionId).ToList();
        if (touched.Count > 0)
        {
            var comps = await _db.Competitions.Where(c => touched.Contains(c.Id) && !c.HasSplits).ToListAsync(ct);
            foreach (var c in comps) c.HasSplits = true;
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
