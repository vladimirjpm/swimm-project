using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// «Входящие» автозабора (фаза 6): синхронизирует Sys_DiscoveredCompetitions со списком
/// isr.org.il, определяет «уже импортировано» матчем по дате+нормализованному имени с
/// таблицей Competitions (OrgCompId у PDF-импортов не заполняется — имя+дата единственный шов).
/// </summary>
public class CompetitionDiscoveryService(
    SwimmDbContext db,
    ICompetitionDiscoveryProvider provider,
    ILogger<CompetitionDiscoveryService> logger) : ICompetitionDiscoveryService
{
    private readonly DiscoveryCompetitionMatcher matcher = new(db);

    public async Task<DiscoverySyncResult> SyncAsync(int? year = null, CancellationToken ct = default)
    {
        // Завершённые + предстоящие сезона (2 запроса, провайдер сам держит паузу).
        // year=null — текущий сезон сайта; иначе прошлый (cYear).
        var finished = await provider.FetchListAsync(finished: true, year, ct);
        var upcoming = await provider.FetchListAsync(finished: false, year, ct);
        var items = finished.Concat(upcoming)
            .GroupBy(i => i.OrgCompId)
            .Select(g => g.First())
            .ToList();

        var result = new DiscoverySyncResult { TotalOnSite = items.Count };
        var now = DateTime.UtcNow;

        var known = await db.DiscoveredCompetitions
            .ToDictionaryAsync(d => d.OrgCompId, ct);

        foreach (var item in items)
        {
            if (known.TryGetValue(item.OrgCompId, out var existing))
            {
                // Имя/даты на сайте могут править — обновляем, статус не трогаем.
                if (existing.Name != item.Name || existing.DateStart != item.DateStart || existing.DateEnd != item.DateEnd)
                {
                    existing.Name = item.Name;
                    existing.DateStart = item.DateStart;
                    existing.DateEnd = item.DateEnd;
                    result.Updated++;
                }
                existing.LastSeenAt = now;
            }
            else
            {
                db.DiscoveredCompetitions.Add(new DiscoveredCompetition
                {
                    OrgCompId = item.OrgCompId,
                    Name = item.Name,
                    DateStart = item.DateStart,
                    DateEnd = item.DateEnd,
                    // Дисциплина — догадка по названию и ТОЛЬКО при первом обнаружении:
                    // у известных строк её мог поправить админ, и повторный забор не должен
                    // затирать ручное решение своим угадыванием.
                    Discipline = Disciplines.GuessFromName(item.Name),
                    DiscoveredAt = now,
                    LastSeenAt = now
                });
                result.Added++;
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Discovery sync (сезон {Season}): на сайте {Total}, добавлено {Added}, обновлено {Updated}",
            year?.ToString() ?? "текущий", result.TotalOnSite, result.Added, result.Updated);
        return result;
    }

    public async Task<IReadOnlyList<DiscoveredCompetitionDto>> GetAllAsync(CancellationToken ct = default)
    {
        var rows = await db.DiscoveredCompetitions
            .AsNoTracking()
            .OrderByDescending(d => d.DateStart)
            .ToListAsync(ct);

        var matches = await matcher.MatchAsync(rows, ct);

        // Fallback-линк по OrgCompId: если справочник уже штампован этим compID (ручная привязка
        // или кросс-языковое имя «מכביה»↔«Maccabiah», которое матчер по имени+дате не спарит) —
        // это авторитетная связь, приоритетнее эвристики матчера.
        var orgCompIds = rows.Select(d => d.OrgCompId).ToList();
        var byOrgCompId = (await db.Competitions
                .AsNoTracking()
                .Where(c => c.OrgCompId != null && orgCompIds.Contains(c.OrgCompId.Value))
                .Select(c => new { OrgCompId = c.OrgCompId!.Value, c.Id, c.Name })
                .ToListAsync(ct))
            .ToDictionary(c => c.OrgCompId, c => new CompetitionMatch(c.Id, c.Name));

        return rows.Select(d => ToDto(d,
            byOrgCompId.TryGetValue(d.OrgCompId, out var linked) ? linked : matches.GetValueOrDefault(d.Id)))
            .ToList();
    }

    public async Task<DiscoveredCompetitionDto?> RefreshDetailsAsync(int id, CancellationToken ct = default)
    {
        var row = await db.DiscoveredCompetitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (row is null) return null;

        try
        {
            var details = await provider.FetchDetailsAsync(row.OrgCompId, ct);
            row.Venue = details.Venue;
            row.LogligId = details.LogligId;
            row.LastError = details.LogligId is null
                ? "Результаты на странице не опубликованы (нет loglig-iframe)."
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            row.LastError = ex.Message;
            logger.LogWarning(ex, "Discovery: не удалось получить детали compID={OrgCompId}", row.OrgCompId);
        }

        await db.SaveChangesAsync(ct);
        return ToDto(row, null);
    }

    /// <summary>
    /// С2 (docs/plans/start-list-plan.md): дочитать детали будущих стартов без loglig-id —
    /// без этого весь конвейер стартового протокола начать нечем. Ошибка по одной строке не
    /// роняет прогон, RefreshDetailsAsync уже пишет её в LastError.
    /// </summary>
    public async Task<(int Checked, int Resolved)> RefreshUpcomingDetailsAsync(
        int daysAhead, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var horizon = today.AddDays(daysAhead);

        var candidates = await db.DiscoveredCompetitions
            .Where(d => d.LogligId == null
                && d.Status != DiscoveredCompetitionStatus.Ignored
                && d.DateStart >= today
                && d.DateStart <= horizon)
            .Select(d => d.Id)
            .ToListAsync(ct);

        var resolved = 0;
        foreach (var id in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var dto = await RefreshDetailsAsync(id, ct);
                if (dto?.LogligId is not null) resolved++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // RefreshDetailsAsync сам ловит сетевые исключения и пишет LastError — сюда
                // долетают только неожиданные обёртки; логируем и идём дальше.
                // Отмену НЕ глотаем (TaskCanceledException — её наследник): при остановке
                // приложения обход обязан прекратиться сразу, а не спустя ещё одну строку.
                logger.LogWarning(ex, "Discovery: RefreshUpcomingDetailsAsync упала на строке {Id}", id);
            }
        }

        logger.LogInformation(
            "Discovery: догрузка деталей будущих стартов (окно {Days} дн.) — проверено {Checked}, добыто {Resolved}",
            daysAhead, candidates.Count, resolved);
        return (candidates.Count, resolved);
    }

    public async Task<int?> GetOrgCompIdAsync(int id, CancellationToken ct = default)
        => await db.DiscoveredCompetitions
            .AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => (int?)d.OrgCompId)
            .FirstOrDefaultAsync(ct);

    public async Task<bool> SetStatusAsync(int id, string status, CancellationToken ct = default)
    {
        if (status is not (DiscoveredCompetitionStatus.New
            or DiscoveredCompetitionStatus.Imported
            or DiscoveredCompetitionStatus.Ignored))
            return false;

        var row = await db.DiscoveredCompetitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (row is null) return false;
        row.Status = status;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Ручная правка дисциплины входящей строки: эвристика по названию иногда промахивается
    /// («סינכרו» без слова «אומנותית» и наоборот), и один клик должен это чинить, не трогая
    /// саму строку. Автозабор эту правку не перетирает — он ставит дисциплину только новым.
    /// </summary>
    public async Task<bool> SetDisciplineAsync(int id, string discipline, CancellationToken ct = default)
    {
        if (!Disciplines.IsValid(discipline)) return false;

        var row = await db.DiscoveredCompetitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (row is null) return false;
        row.Discipline = discipline;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> AddLanguagesAsync(int id, IEnumerable<string> languages, CancellationToken ct = default)
    {
        var row = await db.DiscoveredCompetitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (row is null) return false;

        // Объединение с уже сохранёнными, канонический порядок "he,en".
        var set = (row.Languages ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Concat(languages)
            .Select(l => l.Trim().ToLowerInvariant())
            .Where(l => l is "he" or "en")
            .ToHashSet();
        var merged = string.Join(',', new[] { "he", "en" }.Where(set.Contains));

        if (merged.Length > 0 && merged != row.Languages)
        {
            row.Languages = merged;
            await db.SaveChangesAsync(ct);
        }
        return true;
    }

    public async Task<bool> SetLastErrorAsync(int id, string? error, CancellationToken ct = default)
    {
        var row = await db.DiscoveredCompetitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (row is null) return false;
        row.LastError = error is { Length: > 1000 } ? error[..1000] : error;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetEmptySourceAsync(int id, bool empty, string by, CancellationToken ct = default)
    {
        var row = await db.DiscoveredCompetitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (row is null) return false;

        row.EmptySourceAt = empty ? DateTime.UtcNow : null;
        row.EmptySourceBy = empty ? (by.Length > 200 ? by[..200] : by) : null;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static DiscoveredCompetitionDto ToDto(DiscoveredCompetition d, CompetitionMatch? matched) => new(
        d.Id, d.OrgCompId, d.Name, d.DateStart, d.DateEnd, d.Venue, d.LogligId,
        d.Status, d.DiscoveredAt, d.LastSeenAt, d.LastError, matched?.Name, matched?.CompetitionId, d.Languages);

    /// <summary>Разовый CLI-бэкфилл (см. Program.cs --backfill-discovery-orgcompid): прогоняет
    /// ВСЕ Discovery-строки через матчер и для каждой сматченной проставляет OrgCompId, уважая
    /// уникальность. Строки без матча в отчёт не попадают. dry-run по умолчанию.</summary>
    public async Task<IReadOnlyList<DiscoveryBackfillRow>> BackfillImportedOrgCompIdsAsync(bool apply, CancellationToken ct = default)
    {
        var rows = await db.DiscoveredCompetitions.AsNoTracking().ToListAsync(ct);
        var matches = await matcher.MatchAsync(rows, ct);

        var report = new List<DiscoveryBackfillRow>();
        foreach (var row in rows)
        {
            var match = matches.GetValueOrDefault(row.Id);
            if (match is not { } m) continue;

            var comp = await db.Competitions.FirstAsync(c => c.Id == m.CompetitionId, ct);
            var action = await DetermineLinkActionAsync(comp, row.OrgCompId, apply, ct);
            report.Add(new DiscoveryBackfillRow(row.OrgCompId, row.Name, comp.Id, comp.Name, action));
        }

        if (apply)
            await db.SaveChangesAsync(ct);

        return report;
    }

    /// <summary>Общая логика «привязать compID к соревнованию, если ещё не занят другим»,
    /// используемая батч-бэкфиллом (<see cref="BackfillImportedOrgCompIdsAsync"/>). При apply=false
    /// только определяет действие (WouldLink), не мутирует comp.</summary>
    private async Task<string> DetermineLinkActionAsync(Competition comp, int orgCompId, bool apply, CancellationToken ct)
    {
        if (comp.OrgCompId == orgCompId)
            return "AlreadyLinked";

        var takenByOther = await db.Competitions.AnyAsync(c => c.OrgCompId == orgCompId && c.Id != comp.Id, ct);
        if (takenByOther)
            return "TakenByOther";

        if (!apply)
            return "WouldLink";

        comp.OrgCompId = orgCompId;
        return "Linked";
    }
}
