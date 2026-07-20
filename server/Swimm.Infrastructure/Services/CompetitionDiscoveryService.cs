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

    private static DiscoveredCompetitionDto ToDto(DiscoveredCompetition d, string? matched) => new(
        d.Id, d.OrgCompId, d.Name, d.DateStart, d.DateEnd, d.Venue, d.LogligId,
        d.Status, d.DiscoveredAt, d.LastSeenAt, d.LastError, matched, d.Languages);
}
