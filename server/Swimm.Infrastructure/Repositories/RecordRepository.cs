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
    // правка рекорда сбрасывает метку Records сама (К4).
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    public RecordRepository(SwimmReadDbContext db, ICacheService cache)
    {
        _db    = db;
        _cache = cache;
    }

    public Task<IReadOnlyList<RecordDto>> GetRecordsAsync(
        string region, string? category = null, bool withHolderDetails = false)
    {
        // Регион нормализуем к ключу кэша: records:{region}:{category|all}[:details]
        var regionKey = region.Trim().ToUpperInvariant();
        var cacheKey = $"records:{regionKey}:{category ?? "all"}"
                     + (withHolderDetails ? ":details" : "");

        // GetOrCreate: метки Records/RecordIssues/Swimmers запись получает сама (К3).
        return _cache.GetOrCreateAsync(cacheKey,
            () => LoadRecordsAsync(regionKey, category, withHolderDetails), CacheTtl);
    }

    private async Task<IReadOnlyList<RecordDto>> LoadRecordsAsync(
        string regionKey, string? category, bool withHolderDetails)
    {
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

        return records;
    }

    /// <summary>
    /// Рейтинг стран по одной дисциплине (11.2.1). Кэш — по СОСТАВУ ФИЛЬТРА, а не по региону:
    /// у рейтинга региона нет, а дисциплин с бассейном и полом около сотни, и каждая ложится
    /// своим ключом. Метки <c>Records</c>/<c>RecordIssues</c> запись получает сама (К3).
    /// </summary>
    public Task<RecordRankingDto> GetRankingAsync(RecordRankingQuery query)
    {
        var regionsKey = query.Regions is { Count: > 0 }
            ? string.Join(",", query.Regions)
            : "all";

        var cacheKey = $"records:ranking:{query.Style}:{query.Distance}:{query.Gender}"
                     + $":{query.PoolType}:{regionsKey}:{query.Limit}:{query.Offset}";

        return _cache.GetOrCreateAsync(cacheKey, () => LoadRankingAsync(query), CacheTtl);
    }

    private async Task<RecordRankingDto> LoadRankingAsync(RecordRankingQuery query)
    {
        // Дисциплина — это ЧЕТЫРЕ оси сразу; ни одну нельзя не задать, иначе в одном рейтинге
        // окажутся 50 и 100 метров или мужчины с женщинами.
        var discipline = _db.Records.AsNoTracking()
            .Where(r => r.Category == "open"
                     && r.Gender == query.Gender
                     && r.PoolType == query.PoolType
                     && r.Style == query.Style
                     && r.Distance == query.Distance);

        var countries = discipline.Where(r => r.RegionType == "country");

        if (query.Regions is { Count: > 0 })
        {
            var regions = query.Regions;
            countries = countries.Where(r => regions.Contains(r.RegionCode));
        }

        // Строки без разобранного времени в рейтинг по времени не ставятся — но и не
        // исчезают молча: их число едет в ответе (UnparsedSkipped).
        var unparsed = await countries.CountAsync(r => r.TimeMs == null);

        // Берём дисциплину целиком: в ней не больше одной строки на страну (~215 максимум),
        // а места обязаны считаться по всему рейтингу — см. RecordRankingBuilder.
        var rows = await countries
            .Where(r => r.TimeMs != null)
            .OrderBy(r => r.TimeMs).ThenBy(r => r.RegionCode)   // ничья — по коду, чтобы порядок был устойчивым
            .Select(r => new
            {
                r.RegionType, r.RegionCode, r.Category, r.AgeKey, r.Gender,
                r.PoolType, r.Style, r.Distance,
                r.Time, TimeMs = r.TimeMs!.Value, r.HolderName, r.RecordDate,
            })
            .ToListAsync();

        // Мировой тянем СО ВСЕМИ осями, а не сразу в DTO: он тоже проходит разнос претензий
        // (инвариант И11 — показал время, покажи качество; живой случай — И-20).
        var worldRow = await discipline
            .Where(r => r.RegionType == "world" && r.TimeMs != null)
            .Select(r => new
            {
                r.RegionType, r.RegionCode, r.Category, r.AgeKey, r.Gender,
                r.PoolType, r.Style, r.Distance,
                r.Time, TimeMs = r.TimeMs!.Value, r.HolderName, r.RecordDate,
            })
            .FirstOrDefaultAsync();

        // Претензии — тем же путём, что у /api/records: словарь в памяти, а не JOIN по восьми
        // осям плюс время (см. LoadRecordsAsync). Мировой идёт последним индексом, одним
        // разносом со странами: лестницу претензий нельзя считать по частям.
        var issues = await OpenIssuesAsync();

        var axes = rows.Select((r, i) => new RecordAxes(
                i, r.RegionType, r.RegionCode, r.Category, r.AgeKey,
                r.Gender, r.PoolType, r.Style, r.Distance, r.Time, r.HolderName, r.RecordDate))
            .ToList();
        if (worldRow != null)
            axes.Add(new RecordAxes(
                rows.Count, worldRow.RegionType, worldRow.RegionCode, worldRow.Category,
                worldRow.AgeKey, worldRow.Gender, worldRow.PoolType, worldRow.Style,
                worldRow.Distance, worldRow.Time, worldRow.HolderName, worldRow.RecordDate));

        var reasons = RecordIssueSpreader.Resolve(axes, issues);

        // Экран международный: держателей приводим к латинице, чтобы одна ивритская строка
        // посреди рейтинга 212 стран не читалась как сбой кодировки (см. HolderLatinNamesAsync).
        var latin = await HolderLatinNamesAsync();

        var world = worldRow == null ? null : new RecordRankingWorldDto
        {
            Time = worldRow.Time,
            TimeMs = worldRow.TimeMs,
            HolderName = HolderLatinName.Resolve(worldRow.HolderName, latin),
            RecordDate = worldRow.RecordDate,
            IssueReason = reasons.TryGetValue(rows.Count, out var worldReason) ? worldReason : null,
        };

        var input = rows.Select((r, i) => new RecordRankingBuilder.Row(
            r.RegionCode, r.Time, r.TimeMs, HolderLatinName.Resolve(r.HolderName, latin), r.RecordDate,
            reasons.TryGetValue(i, out var reason) ? reason : null)).ToList();

        var ranked = RecordRankingBuilder.Build(input, world);

        return new RecordRankingDto
        {
            Style = query.Style,
            Distance = query.Distance,
            Gender = query.Gender,
            PoolType = query.PoolType,
            Total = ranked.Count,
            UnparsedSkipped = unparsed,
            World = world,
            Rows = ranked.Skip(query.Offset).Take(query.Limit).ToList(),
        };
    }

    /// <summary>Страны справочника для выбора на витрине (11.3.2).</summary>
    public Task<IReadOnlyList<RecordCountryOptionDto>> GetRecordCountriesAsync()
        => _cache.GetOrCreateAsync("records:countries", async () =>
        {
            var rows = await _db.Records.AsNoTracking()
                .Where(r => r.RegionType == "country" && r.Category == "open")
                .GroupBy(r => r.RegionCode)
                .Select(g => new RecordCountryOptionDto { Code = g.Key, Records = g.Count() })
                .OrderBy(x => x.Code)
                .ToListAsync();

            return (IReadOnlyList<RecordCountryOptionDto>)rows;
        }, CacheTtl);

    /// <summary>
    /// Сравнение двух стран (11.3.1). Кэш — по паре кодов и разрезу; пара НЕ сортируется:
    /// «A против B» и «B против A» дают зеркальный ответ (слева своя сторона), и один ключ
    /// на оба означал бы, что второй запрос получит чужую раскладку.
    /// </summary>
    public Task<RecordCompareDto> GetCompareAsync(RecordCompareQuery query)
    {
        var cacheKey = $"records:compare:{query.A}:{query.B}"
                     + $":{query.PoolType ?? "all"}:{query.Gender ?? "all"}";

        return _cache.GetOrCreateAsync(cacheKey, () => LoadCompareAsync(query), CacheTtl);
    }

    private async Task<RecordCompareDto> LoadCompareAsync(RecordCompareQuery query)
    {
        var codes = new[] { query.A, query.B };

        var q = _db.Records.AsNoTracking()
            .Where(r => r.RegionType == "country"
                     && r.Category == "open"
                     && r.TimeMs != null
                     && codes.Contains(r.RegionCode));

        if (query.PoolType != null) q = q.Where(r => r.PoolType == query.PoolType);
        if (query.Gender != null) q = q.Where(r => r.Gender == query.Gender);

        var rows = await q
            .Select(r => new
            {
                r.RegionType, r.RegionCode, r.Category, r.AgeKey, r.Gender,
                r.PoolType, r.Style, r.Distance,
                r.Time, TimeMs = r.TimeMs!.Value, r.HolderName, r.RecordDate,
            })
            .ToListAsync();

        // Претензии — тем же путём, что у /api/records и рейтинга: словарь в памяти.
        var issues = await OpenIssuesAsync();
        var axes = rows.Select((r, i) => new RecordAxes(
            i, r.RegionType, r.RegionCode, r.Category, r.AgeKey,
            r.Gender, r.PoolType, r.Style, r.Distance, r.Time, r.HolderName, r.RecordDate)).ToList();
        var reasons = RecordIssueSpreader.Resolve(axes, issues);

        // Тот же международный экран — та же латиница (см. HolderLatinNamesAsync).
        var latin = await HolderLatinNamesAsync();

        var input = rows.Select((r, i) => new RecordCompareBuilder.Row(
            r.RegionCode, r.Style, r.Distance, r.Gender, r.PoolType,
            r.Time, r.TimeMs, HolderLatinName.Resolve(r.HolderName, latin), r.RecordDate,
            reasons.TryGetValue(i, out var reason) ? reason : null)).ToList();

        var result = RecordCompareBuilder.Build(input, query.A, query.B);
        result.PoolType = query.PoolType;
        result.Gender = query.Gender;
        return result;
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
    /// Словарь «ивритское имя → латинское» из карточек наших пловцов. Сама подстановка и
    /// правило, зачем она нужна именно на международных экранах, — в
    /// <see cref="HolderLatinName"/>; здесь только сбор данных, который требует БД.
    ///
    /// Кэшируется отдельным ключом: словарь один на все дисциплины рейтинга (их под сотню),
    /// и собирать его заново на каждую значило бы читать таблицу пловцов сотню раз.
    /// </summary>
    private Task<Dictionary<string, string>> HolderLatinNamesAsync()
        => _cache.GetOrCreateAsync("records:holder-latin-names", async () =>
        {
            var swimmers = await _db.Swimmers.AsNoTracking()
                .Where(s => s.FirstNameEn != null && s.LastNameEn != null)
                .Select(s => new { s.FirstName, s.LastName, s.FirstNameEn, s.LastNameEn })
                .ToListAsync();

            var found = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            void Remember(string key, string latin)
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(latin)) return;
                if (!found.TryGetValue(key, out var set)) found[key] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(latin);
            }

            foreach (var s in swimmers)
            {
                var first = (s.FirstName ?? "").Trim();
                var last = (s.LastName ?? "").Trim();
                var latin = HolderLatinName.Normalize($"{(s.FirstNameEn ?? "").Trim()} {(s.LastNameEn ?? "").Trim()}");

                // Английское имя-копия ивритского нам не поможет: у части карточек в
                // *En лежит тот же иврит (то же, что у клубов — см. clubLabel на клиенте).
                if (latin.Length == 0 || HolderLatinName.HasHebrew(latin)) continue;
                if (first.Length == 0 && last.Length == 0) continue;

                Remember(HolderLatinName.Normalize($"{first} {last}"), latin);
                Remember(HolderLatinName.Normalize($"{last} {first}"), latin);
            }

            // Одно латинское имя на ключ — иначе это тёзки, и угадывать мы не имеем права
            // (то же правило, что у года рождения ниже).
            return found.Where(kv => kv.Value.Count == 1)
                .ToDictionary(kv => kv.Key, kv => kv.Value.First(), StringComparer.OrdinalIgnoreCase);
        }, CacheTtl);

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

    public Task<IReadOnlyList<NormativeStandardDto>> GetStandardsAsync(string? kind = null, string? country = null)
    {
        // Страну нормализуем как регион выше: trim + upper.
        var countryKey = string.IsNullOrWhiteSpace(country) ? null : country.Trim().ToUpperInvariant();
        var cacheKey = $"normative-standards:{kind ?? "all"}:{countryKey ?? "all"}";

        return _cache.GetOrCreateAsync(cacheKey, () => LoadStandardsAsync(kind, countryKey), CacheTtl);
    }

    private async Task<IReadOnlyList<NormativeStandardDto>> LoadStandardsAsync(string? kind, string? countryKey)
    {
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

        return standards;
    }
}
