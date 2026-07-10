using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Дифф спарсенных рекордов (<see cref="IRecordSourceProvider"/>) с текущими Records +
/// применение выбранных групп. Диффы держим в <see cref="IMemoryCache"/> 10 минут (сессия
/// превью в UI — Fetch → показать дифф → Apply); отдельно от публичного HTTP-кэша
/// (<see cref="ICacheService"/>), который тут только инвалидируется после Apply.
/// </summary>
public class RecordDiffService : IRecordDiffService
{
    private const char KeySeparator = '';

    private static readonly (string Source, string Category)[] SourceScopes =
    {
        ("worldrecords", "open"),
        ("isrorg-age", "age"),
        ("isrorg-masters", "masters"),
    };

    private readonly SwimmDbContext _db;
    private readonly ICacheService _cache;
    private readonly IMemoryCache _memoryCache;

    public RecordDiffService(SwimmDbContext db, ICacheService cache, IMemoryCache memoryCache)
    {
        _db = db;
        _cache = cache;
        _memoryCache = memoryCache;
    }

    public async Task<RecordDiffResult> BuildDiffAsync(string source, IReadOnlyList<ParsedRecordDto> parsed, CancellationToken ct = default)
    {
        // Источник может давать НЕСКОЛЬКО строк на одну дисциплину: рекорд, установленный
        // дважды («equalled», worldaquatics даёт обе даты), или прогрессию времён. Без
        // схлопывания Apply падает на 23505 (unique-индекс по 8 осям) — найдено на приёмке 2.6.
        parsed = DeduplicateByAxes(parsed);

        var categories = parsed.Select(p => p.Category).Distinct().ToHashSet();
        var regionTypes = parsed.Select(p => p.RegionType).Distinct().ToHashSet();

        var existing = await _db.Records.AsNoTracking()
            .Where(r => categories.Contains(r.Category) && regionTypes.Contains(r.RegionType))
            .ToListAsync(ct);

        var existingByKey = existing.ToDictionary(Key, r => r);
        var parsedKeys = new HashSet<string>();

        var added = new List<ParsedRecordDto>();
        var changed = new List<ParsedRecordDto>();
        var addedEntries = new List<RecordDiffEntry>();
        var changedEntries = new List<RecordDiffEntry>();
        int unchanged = 0;

        foreach (var p in parsed)
        {
            var key = Key(p);
            parsedKeys.Add(key);

            if (existingByKey.TryGetValue(key, out var ex))
            {
                if (ex.Time != p.Time || (ex.HolderName ?? "") != (p.HolderName ?? "") || (ex.RecordDate ?? "") != (p.RecordDate ?? ""))
                {
                    changed.Add(p);
                    changedEntries.Add(new RecordDiffEntry(
                        p.RegionType, p.RegionCode, p.Category, p.AgeKey, p.Gender, p.PoolType, p.Style, p.Distance,
                        ex.Time, ex.HolderName, ex.RecordDate,
                        p.Time, p.HolderName, p.RecordDate));
                }
                else
                {
                    unchanged++;
                }
            }
            else
            {
                added.Add(p);
                addedEntries.Add(new RecordDiffEntry(
                    p.RegionType, p.RegionCode, p.Category, p.AgeKey, p.Gender, p.PoolType, p.Style, p.Distance,
                    null, null, null,
                    p.Time, p.HolderName, p.RecordDate));
            }
        }

        var missingInSource = existingByKey.Keys.Count(k => !parsedKeys.Contains(k));

        var diffId = Guid.NewGuid().ToString("N");
        _memoryCache.Set(DiffCacheKey(diffId), new CachedRecordDiff(source, added, changed), TimeSpan.FromMinutes(10));

        return new RecordDiffResult(diffId, source, added.Count, changed.Count, unchanged, missingInSource, addedEntries, changedEntries);
    }

    public async Task<RecordDiffApplyResult> ApplyAsync(RecordDiffApplyRequest request, CancellationToken ct = default)
    {
        if (!_memoryCache.TryGetValue(DiffCacheKey(request.DiffId), out CachedRecordDiff? cached) || cached == null)
            return new RecordDiffApplyResult(false, "Дифф не найден или истёк (10 минут) — повторите Fetch.", 0);

        var toApply = new List<ParsedRecordDto>();
        if (request.ApplyAdded) toApply.AddRange(cached.Added);
        if (request.ApplyChanged) toApply.AddRange(cached.Changed);

        if (toApply.Count == 0)
        {
            _memoryCache.Remove(DiffCacheKey(request.DiffId));
            return new RecordDiffApplyResult(true, null, 0);
        }

        var categories = toApply.Select(p => p.Category).Distinct().ToHashSet();
        var regionTypes = toApply.Select(p => p.RegionType).Distinct().ToHashSet();
        var existing = await _db.Records
            .Where(r => categories.Contains(r.Category) && regionTypes.Contains(r.RegionType))
            .ToListAsync(ct);
        var existingByKey = existing.ToDictionary(Key, r => r);

        var now = DateTime.UtcNow;
        foreach (var p in toApply)
        {
            if (existingByKey.TryGetValue(Key(p), out var rec))
            {
                rec.Time = p.Time;
                rec.HolderName = p.HolderName;
                rec.Club = p.Club;
                rec.HolderCountry = p.HolderCountry;
                rec.RecordDate = p.RecordDate;
                rec.UpdatedAt = now;
            }
            else
            {
                _db.Records.Add(new Record
                {
                    RegionType = p.RegionType,
                    RegionCode = p.RegionCode,
                    Category = p.Category,
                    AgeKey = p.AgeKey,
                    Gender = p.Gender,
                    PoolType = p.PoolType,
                    Style = p.Style,
                    Distance = p.Distance,
                    Time = p.Time,
                    HolderName = p.HolderName,
                    Club = p.Club,
                    HolderCountry = p.HolderCountry,
                    RecordDate = p.RecordDate,
                    UpdatedAt = now,
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        _memoryCache.Remove(DiffCacheKey(request.DiffId));
        await _cache.InvalidateAllAsync();

        return new RecordDiffApplyResult(true, null, toApply.Count);
    }

    public async Task<IReadOnlyList<RecordSourceStatusDto>> GetSourceStatusAsync(CancellationToken ct = default)
    {
        var result = new List<RecordSourceStatusDto>();
        foreach (var (source, category) in SourceScopes)
        {
            var max = await _db.Records.AsNoTracking()
                .Where(r => r.Category == category)
                .Select(r => (DateTime?)r.UpdatedAt)
                .MaxAsync(ct);
            result.Add(new RecordSourceStatusDto(source, max));
        }
        return result;
    }

    /// <summary>
    /// Схлопывает дубли по 8 осям: побеждает лучшее (наименьшее) время; при равных лучших
    /// временах держатели объединяются через ", " (совместное владение рекордом), остальные
    /// поля — от первой из равных строк. Нераспарсиваемое время проигрывает распарсиваемому.
    /// </summary>
    public static IReadOnlyList<ParsedRecordDto> DeduplicateByAxes(IReadOnlyList<ParsedRecordDto> parsed)
    {
        return parsed
            .GroupBy(Key)
            .Select(g =>
            {
                var items = g.ToList();
                if (items.Count == 1) return items[0];

                var ordered = items.OrderBy(p => TimeSortKey(p.Time)).ToList();
                var best = ordered[0];
                var bestKey = TimeSortKey(best.Time);

                var holders = ordered
                    .Where(p => TimeSortKey(p.Time) == bestKey)
                    .Select(p => p.HolderName?.Trim())
                    .Where(h => !string.IsNullOrEmpty(h))
                    .Distinct()
                    .ToList();

                return holders.Count > 1 ? best with { HolderName = string.Join(", ", holders) } : best;
            })
            .ToList();
    }

    /// <summary>«ss.xx» / «mm:ss.xx» / «hh:mm:ss.xx» → сантисекунды; мусор → MaxValue (проигрывает всем).</summary>
    private static long TimeSortKey(string? time)
    {
        if (string.IsNullOrWhiteSpace(time)) return long.MaxValue;
        var parts = time.Trim().Split(':');
        if (parts.Length > 3) return long.MaxValue;

        long total = 0;
        foreach (var part in parts)
        {
            if (!decimal.TryParse(part, System.Globalization.NumberStyles.AllowDecimalPoint,
                    System.Globalization.CultureInfo.InvariantCulture, out var value) || value < 0)
                return long.MaxValue;
            total = total * 60 + (long)Math.Round(value * 100);
        }
        return total;
    }

    private static string DiffCacheKey(string diffId) => $"recorddiff:{diffId}";

    private static string Key(Record r) =>
        string.Join(KeySeparator, r.RegionType, r.RegionCode, r.Category, r.AgeKey, r.Gender, r.PoolType, r.Style, r.Distance);

    private static string Key(ParsedRecordDto p) =>
        string.Join(KeySeparator, p.RegionType, p.RegionCode, p.Category, p.AgeKey, p.Gender, p.PoolType, p.Style, p.Distance);

    private sealed record CachedRecordDiff(string Source, IReadOnlyList<ParsedRecordDto> Added, IReadOnlyList<ParsedRecordDto> Changed);
}
