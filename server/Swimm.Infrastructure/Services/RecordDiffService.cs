using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Дифф спарсенных рекордов (<see cref="IRecordSourceProvider"/>) с текущими Records +
/// применение выбранных групп. Диффы держим в <see cref="IMemoryCache"/> 10 минут (сессия
/// превью в UI — Fetch → показать дифф → Apply), а прогон по странам просит больше —
/// он сам идёт час-два (11.1.2); это не публичный HTTP-кэш
/// (<see cref="ICacheService"/>) — тот Apply сбрасывает сам, сохранением через EF (К4).
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

    /// <summary>Сколько живёт дифф превью в админке, если вызывающий не попросил другого.</summary>
    private static readonly TimeSpan DefaultPreviewTtl = TimeSpan.FromMinutes(10);

    private readonly SwimmDbContext _db;
    private readonly IMemoryCache _memoryCache;

    public RecordDiffService(SwimmDbContext db, IMemoryCache memoryCache)
    {
        _db = db;
        _memoryCache = memoryCache;
    }

    public async Task<RecordDiffResult> BuildDiffAsync(
        string source, IReadOnlyList<ParsedRecordDto> parsed,
        TimeSpan? previewTtl = null, CancellationToken ct = default)
    {
        // Источник может давать НЕСКОЛЬКО строк на одну дисциплину: рекорд, установленный
        // дважды («equalled», worldaquatics даёт обе даты), или прогрессию времён. Без
        // схлопывания Apply падает на 23505 (unique-индекс по 8 осям) — найдено на приёмке 2.6.
        parsed = DeduplicateByAxes(parsed);

        var categories = parsed.Select(p => p.Category).Distinct().ToHashSet();
        var regionTypes = parsed.Select(p => p.RegionType).Distinct().ToHashSet();

        // Сужаем до регионов, которые источник вообще принёс. Иначе при прогоне по одной
        // стране (11.1.2) все остальные страны категории open выглядели бы «пропавшими из
        // источника»: раньше это было незаметно, потому что регион был ровно один.
        var regionCodes = parsed.Select(p => p.RegionCode).Distinct().ToHashSet();

        var existing = await _db.Records.AsNoTracking()
            .Where(r => categories.Contains(r.Category)
                        && regionTypes.Contains(r.RegionType)
                        && regionCodes.Contains(r.RegionCode))
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

        // Сторож правдоподобия (И-20): источник сам отдаёт невозможные времена. Ничего не
        // отбрасываем — Apply запишет строки как есть и заведёт их в реестр кандидатами.
        var suspicious = RecordPlausibility.Check(
            addedEntries.Concat(changedEntries), await WorldReferenceAsync(parsed, ct));

        var diffId = Guid.NewGuid().ToString("N");
        _memoryCache.Set(DiffCacheKey(diffId), new CachedRecordDiff(source, added, changed, suspicious),
            previewTtl ?? DefaultPreviewTtl);

        return new RecordDiffResult(diffId, source, added.Count, changed.Count, unchanged, missingInSource,
            addedEntries, changedEntries, suspicious);
    }

    /// <summary>
    /// Мировые рекорды из базы и из этого диффа — эталон правила «быстрее мирового».
    /// Источник возрастных рекордов мировых строк не приносит, поэтому база нужна всегда.
    /// </summary>
    private async Task<Dictionary<string, (int Ms, string Time)>> WorldReferenceAsync(
        IReadOnlyList<ParsedRecordDto> parsed, CancellationToken ct)
    {
        var fromDb = await _db.Records.AsNoTracking()
            .Where(r => r.RegionType == "world" && r.Category == "open")
            .Select(r => new { r.Gender, r.PoolType, r.Style, r.Distance, r.Time })
            .ToListAsync(ct);

        return RecordPlausibility.WorldReference(
            fromDb.Select(r => (r.Gender, r.PoolType, r.Style, r.Distance, r.Time))
                .Concat(parsed
                    .Where(p => p.RegionType == "world" && p.Category == "open")
                    .Select(p => (p.Gender, p.PoolType, p.Style, p.Distance, p.Time))));
    }

    public async Task<RecordDiffApplyResult> ApplyAsync(RecordDiffApplyRequest request, CancellationToken ct = default)
    {
        if (!_memoryCache.TryGetValue(DiffCacheKey(request.DiffId), out CachedRecordDiff? cached) || cached == null)
            return new RecordDiffApplyResult(false, "Дифф не найден или истёк — повторите Fetch или прогон.", 0);

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

        var candidates = await AddIssueCandidatesAsync(cached.Suspicious, toApply, now, ct);

        await _db.SaveChangesAsync(ct);
        _memoryCache.Remove(DiffCacheKey(request.DiffId));

        return new RecordDiffApplyResult(true, null, toApply.Count, candidates);
    }

    /// <summary>
    /// Подозрительные значения, которые этот Apply реально записал, — в реестр спорных
    /// рекордов кандидатами (<see cref="RecordIssueStatuses.Candidate"/>). Пишутся тем же
    /// SaveChanges, что и рекорды: рекорд без претензии или претензия без рекорда — оба хуже.
    ///
    /// Претензию на то же значение (8 осей + время), которая уже есть в реестре, не трогаем ни
    /// в каком статусе: если человек её отклонил, повторный импорт того же файла не должен её
    /// воскрешать.
    /// </summary>
    private async Task<int> AddIssueCandidatesAsync(
        IReadOnlyList<RecordSuspiciousEntry> suspicious, IReadOnlyList<ParsedRecordDto> applied,
        DateTime now, CancellationToken ct)
    {
        if (suspicious.Count == 0) return 0;

        // Кандидат — только то, что реально легло: галки «новые / изменившиеся» в UI могут
        // отсечь часть диффа.
        var appliedKeys = applied.Select(p => IssueKey(p.RegionType, p.RegionCode, p.Category, p.AgeKey,
            p.Gender, p.PoolType, p.Style, p.Distance, p.Time)).ToHashSet();
        var toFlag = suspicious.Where(s => appliedKeys.Contains(IssueKey(s))).ToList();
        if (toFlag.Count == 0) return 0;

        var times = toFlag.Select(s => s.Time).Distinct().ToList();
        var inRegistry = (await _db.RecordIssues.AsNoTracking()
                .Where(i => times.Contains(i.FlaggedTime))
                .Select(i => new { i.RegionType, i.RegionCode, i.Category, i.AgeKey, i.Gender, i.PoolType, i.Style, i.Distance, i.FlaggedTime })
                .ToListAsync(ct))
            .Select(i => IssueKey(i.RegionType, i.RegionCode, i.Category, i.AgeKey, i.Gender, i.PoolType, i.Style, i.Distance, i.FlaggedTime))
            .ToHashSet();

        var created = 0;
        foreach (var s in toFlag)
        {
            if (!inRegistry.Add(IssueKey(s))) continue;
            _db.RecordIssues.Add(new RecordIssue
            {
                RegionType = s.RegionType,
                RegionCode = s.RegionCode,
                Category = s.Category,
                AgeKey = s.AgeKey,
                Gender = s.Gender,
                PoolType = s.PoolType,
                Style = s.Style,
                Distance = s.Distance,
                FlaggedTime = s.Time,
                Reason = s.Reason,
                Status = RecordIssueStatuses.Candidate,
                Note = s.Note,
                CreatedBy = "auto",
                CreatedAt = now,
                UpdatedAt = now,
            });
            created++;
        }
        return created;
    }

    private static string IssueKey(RecordSuspiciousEntry s) =>
        IssueKey(s.RegionType, s.RegionCode, s.Category, s.AgeKey, s.Gender, s.PoolType, s.Style, s.Distance, s.Time);

    // Ключ реестра нормализует регистр и суффикс дистанции: претензию, заведённую руками как
    // «100», импорт обязан узнать в «100m».
    private static string IssueKey(string regionType, string regionCode, string category, string ageKey,
        string gender, string poolType, string style, string distance, string time) =>
        RecordIssueKey.Of(regionType, regionCode, category, ageKey, gender, poolType, style, distance, time);

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

    private sealed record CachedRecordDiff(
        string Source, IReadOnlyList<ParsedRecordDto> Added, IReadOnlyList<ParsedRecordDto> Changed,
        IReadOnlyList<RecordSuspiciousEntry> Suspicious);
}
