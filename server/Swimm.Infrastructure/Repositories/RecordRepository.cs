using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

public class RecordRepository : IRecordRepository
{
    // Read-only контекст (swimm_ro) — рекорды/нормативы публичны.
    private readonly SwimmReadDbContext _db;
    private readonly ICacheService _cache;

    // Данные меняются редко (правки в админке, будущее автообновление) — длинный TTL;
    // админ-CRUD инвалидирует всё через ICacheService.InvalidateAllAsync().
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    public RecordRepository(SwimmReadDbContext db, ICacheService cache)
    {
        _db    = db;
        _cache = cache;
    }

    public async Task<IReadOnlyList<RecordDto>> GetRecordsAsync(
        string region, string? category = null, bool withHolderDetails = false)
    {
        // Регион нормализуем к ключу кэша: records:{region}:{category|all}[:details]
        var regionKey = region.Trim().ToUpperInvariant();
        var cacheKey = $"records:{regionKey}:{category ?? "all"}"
                     + (withHolderDetails ? ":details" : "");

        var cached = await _cache.GetAsync<IReadOnlyList<RecordDto>>(cacheKey);
        if (cached is not null)
            return cached;

        var query = _db.Records.AsNoTracking();

        // "world" — тип региона; всё остальное — код континента или страны.
        query = regionKey == "WORLD"
            ? query.Where(r => r.RegionType == "world")
            : query.Where(r => r.RegionCode == regionKey);

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(r => r.Category == category);

        var records = await query
            .OrderBy(r => r.Category).ThenBy(r => r.Gender).ThenBy(r => r.PoolType)
            .ThenBy(r => r.Style).ThenBy(r => r.Distance).ThenBy(r => r.AgeKey)
            .Select(r => new RecordDto
            {
                Id            = r.Id,
                RegionType    = r.RegionType,
                RegionCode    = r.RegionCode,
                Category      = r.Category,
                AgeKey        = r.AgeKey,
                Gender        = r.Gender,
                PoolType      = r.PoolType,
                Style         = r.Style,
                Distance      = r.Distance,
                Time          = r.Time,
                HolderName    = r.HolderName,
                Club          = r.Club,
                HolderCountry = r.HolderCountry,
                RecordDate    = r.RecordDate,
                UpdatedAt     = r.UpdatedAt
            })
            .ToListAsync();

        // Метка «запись оспаривается» (docs/plans/records-quality-plan.md). Отдельным запросом,
        // а не JOIN: претензий десятки на 1.9к рекордов, а ключ сопоставления — 8 осей ПЛЮС
        // время, что в SQL-джойне читалось бы куда хуже, чем словарь в памяти.
        var issues = await OpenIssuesAsync();
        var axes = records.Select((r, i) => new RecordAxes(
            i, r.RegionType, r.RegionCode, r.Category, r.AgeKey,
            r.Gender, r.PoolType, r.Style, r.Distance, r.Time, r.HolderName, r.RecordDate)).ToList();
        var reasons = RecordIssueSpreader.Resolve(axes, issues);
        foreach (var (index, reason) in reasons)
            records[index].IssueReason = reason;

        if (withHolderDetails) await FillHolderDetailsAsync(records);

        await _cache.SetAsync(cacheKey, (IReadOnlyList<RecordDto>)records, CacheTtl);

        return records;
    }

    /// <summary>
    /// Досыпает год рождения держателя и его возраст в год рекорда (отладочная опция
    /// ShowAgeRecordsDetails). В справочнике федерации года рождения нет — восстанавливаем
    /// по нашим пловцам, совпадением имени.
    ///
    /// ⚠ Почему НЕ по сверке «рекорды ↔ протоколы» (Sys_RecordVerifications.SwimmerId), хотя
    /// она надёжнее: публичный read-путь ходит под ролью <c>swimm_ro</c>, у которой нет прав
    /// на <c>Sys_*</c> по дизайну (server/db/setup-roles.sql). Лезть туда из витрины — значит
    /// открывать системные таблицы публичной роли ради отладочной подписи; не стоит того.
    ///
    /// Правила совпадения:
    /// • имя сверяется в ОБЕИХ перестановках слов — справочник пишет «טלר מרק», мы «מרק טלר»;
    /// • тёзки с разными годами рождения отбрасываются: угадывать нельзя;
    /// • не опознан — поля остаются null, витрина покажет прочерк.
    ///
    /// Отсюда и пометка источника <c>name</c> в DTO: подпись на витрине помечена «?», потому
    /// что это совпадение имени, а не доказанный заплыв.
    /// </summary>
    private async Task FillHolderDetailsAsync(List<RecordDto> records)
    {
        var byName = await SwimmerBirthYearsByNameAsync();

        foreach (var r in records)
        {
            if (string.IsNullOrWhiteSpace(r.HolderName)) continue;
            if (!byName.TryGetValue(NormalizeHolderName(r.HolderName!), out var birthYear)) continue;

            r.HolderBirthYear = birthYear;
            r.HolderSource = "name";

            var recordYear = RecordYearOf(r.RecordDate);
            if (recordYear is int y && y - birthYear is > 0 and < 120)
                r.HolderAge = y - birthYear;
        }
    }

    /// <summary>
    /// «имя фамилия» → год рождения, только там, где имя однозначно. Ключи кладём в обеих
    /// перестановках: справочник и наши протоколы пишут порядок слов по-разному.
    /// </summary>
    private async Task<Dictionary<string, int>> SwimmerBirthYearsByNameAsync()
    {
        var swimmers = await _db.Swimmers.AsNoTracking()
            .Where(s => s.BirthYear > 0)
            .Select(s => new { s.FirstName, s.LastName, s.BirthYear })
            .ToListAsync();

        var years = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

        void Remember(string key, int year)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (!years.TryGetValue(key, out var set)) years[key] = set = new HashSet<int>();
            set.Add(year);
        }

        foreach (var s in swimmers)
        {
            var first = (s.FirstName ?? "").Trim();
            var last = (s.LastName ?? "").Trim();
            if (first.Length == 0 && last.Length == 0) continue;

            Remember(NormalizeHolderName($"{first} {last}"), s.BirthYear);
            Remember(NormalizeHolderName($"{last} {first}"), s.BirthYear);
        }

        // Один год рождения на имя — иначе это тёзки, и угадывать мы не имеем права.
        return years
            .Where(kv => kv.Value.Count == 1)
            .ToDictionary(kv => kv.Key, kv => kv.Value.First(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Схлопывает пробелы: в справочнике их бывает по нескольку подряд.</summary>
    private static string NormalizeHolderName(string name) =>
        string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries
                                       | StringSplitOptions.TrimEntries));

    /// <summary>Год из даты рекорда: «22/12/2003» → 2003. null — даты нет или она мусорная.</summary>
    private static int? RecordYearOf(string? recordDate)
    {
        var m = System.Text.RegularExpressions.Regex.Match(recordDate ?? "", @"(19|20)\d{2}");
        return m.Success && int.TryParse(m.Value, out var y) ? y : null;
    }

    /// <summary>
    /// Открытые претензии по ключу «оси + время». Закрытые (<c>rejected</c> — разобрались,
    /// запись верна; <c>fixed-by-source</c> — федерация уже исправила) метку не дают: иначе
    /// значок висел бы вечно и обесценился.
    /// </summary>
    private async Task<Dictionary<string, string>> OpenIssuesAsync()
    {
        var open = await _db.RecordIssues.AsNoTracking()
            .Where(i => i.Status == RecordIssueStatuses.Open
                     || i.Status == RecordIssueStatuses.Reported
                     || i.Status == RecordIssueStatuses.Accepted)
            .Select(i => new
            {
                i.RegionType, i.RegionCode, i.Category, i.AgeKey, i.Gender,
                i.PoolType, i.Style, i.Distance, i.FlaggedTime, i.Reason
            })
            .ToListAsync();

        var map = new Dictionary<string, string>();
        foreach (var i in open)
        {
            var key = RecordIssueKey.Of(i.RegionType, i.RegionCode, i.Category, i.AgeKey,
                i.Gender, i.PoolType, i.Style, i.Distance, i.FlaggedTime);
            map[key] = i.Reason;
        }
        return map;
    }

    public async Task<IReadOnlyList<NormativeStandardDto>> GetStandardsAsync(string? kind = null, string? country = null)
    {
        // Страну нормализуем как регион выше: trim + upper.
        var countryKey = string.IsNullOrWhiteSpace(country) ? null : country.Trim().ToUpperInvariant();
        var cacheKey = $"normative-standards:{kind ?? "all"}:{countryKey ?? "all"}";

        var cached = await _cache.GetAsync<IReadOnlyList<NormativeStandardDto>>(cacheKey);
        if (cached is not null)
            return cached;

        var query = _db.NormativeStandards.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(kind))
            query = query.Where(s => s.Kind == kind);

        // Страна задана — отдаём её строки плюс универсальные (Country == "").
        if (countryKey is not null)
            query = query.Where(s => s.Country == countryKey || s.Country == "");

        var standards = await query
            .OrderBy(s => s.Kind).ThenBy(s => s.Gender).ThenBy(s => s.PoolType)
            .ThenBy(s => s.Style).ThenBy(s => s.Distance).ThenBy(s => s.AgeKey).ThenBy(s => s.Level)
            .Select(s => new NormativeStandardDto
            {
                Id       = s.Id,
                Kind     = s.Kind,
                Country  = s.Country,
                Gender   = s.Gender,
                PoolType = s.PoolType,
                Style    = s.Style,
                Distance = s.Distance,
                AgeKey   = s.AgeKey,
                Level    = s.Level,
                Time     = s.Time
            })
            .ToListAsync();

        await _cache.SetAsync(cacheKey, (IReadOnlyList<NormativeStandardDto>)standards, CacheTtl);

        return standards;
    }
}
