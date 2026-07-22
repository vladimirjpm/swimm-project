using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
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

    public async Task<DiscoverySyncResult> SyncAsync(CancellationToken ct = default)
    {
        // Завершённые + предстоящие текущего сезона (2 запроса, провайдер сам держит паузу).
        var finished = await provider.FetchListAsync(finished: true, ct);
        var upcoming = await provider.FetchListAsync(finished: false, ct);
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
                    DiscoveredAt = now,
                    LastSeenAt = now
                });
                result.Added++;
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Discovery sync: на сайте {Total}, добавлено {Added}, обновлено {Updated}",
            result.TotalOnSite, result.Added, result.Updated);
        return result;
    }

    public async Task<IReadOnlyList<DiscoveredCompetitionDto>> GetAllAsync(CancellationToken ct = default)
    {
        var rows = await db.DiscoveredCompetitions
            .AsNoTracking()
            .OrderByDescending(d => d.DateStart)
            .ToListAsync(ct);

        var matches = await matcher.MatchAsync(rows, ct);
        return rows.Select(d => ToDto(d, matches.GetValueOrDefault(d.Id))).ToList();
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

    private static DiscoveredCompetitionDto ToDto(DiscoveredCompetition d, CompetitionMatch? matched) => new(
        d.Id, d.OrgCompId, d.Name, d.DateStart, d.DateEnd, d.Venue, d.LogligId,
        d.Status, d.DiscoveredAt, d.LastSeenAt, d.LastError, matched?.Name, matched?.CompetitionId, d.Languages);

    /// <summary>
    /// Бэкфилл связи «Discovery → Competition»: находит уже импортированное соревнование этой
    /// строки (матч по дате+имени) и проставляет ему <see cref="Competition.OrgCompId"/> = compID
    /// сайта, если он ещё не занят другим соревнованием. Нужно для строк, импортированных до
    /// того, как импорт научился штамповать OrgCompId. Идемпотентно.
    /// </summary>
    public async Task<RelinkResult> LinkImportedAsync(int id, CancellationToken ct = default)
    {
        var row = await db.DiscoveredCompetitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (row == null) return new RelinkResult { Ok = false, Message = "Запись не найдена" };

        var match = (await matcher.MatchAsync([row], ct)).GetValueOrDefault(row.Id);
        if (match is not { } m)
            return new RelinkResult { Ok = false, Message = "Соревнование не найдено в справочнике (нет матча по имени+дате)" };

        var comp = await db.Competitions.FirstAsync(c => c.Id == m.CompetitionId, ct);
        if (comp.OrgCompId == row.OrgCompId)
            return new RelinkResult { Ok = true, AlreadyLinked = true, CompetitionId = comp.Id, CompetitionName = comp.Name, Message = "Уже привязано" };

        var takenByOther = await db.Competitions.AnyAsync(c => c.OrgCompId == row.OrgCompId && c.Id != comp.Id, ct);
        if (takenByOther)
            return new RelinkResult { Ok = false, CompetitionId = comp.Id, CompetitionName = comp.Name, Message = $"compID {row.OrgCompId} уже занят другим соревнованием" };

        comp.OrgCompId = row.OrgCompId;
        await db.SaveChangesAsync(ct);
        return new RelinkResult { Ok = true, CompetitionId = comp.Id, CompetitionName = comp.Name, Message = "Привязано" };
    }

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
    /// переиспользуемая <see cref="LinkImportedAsync"/> и батч-бэкфиллом. При apply=false
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
