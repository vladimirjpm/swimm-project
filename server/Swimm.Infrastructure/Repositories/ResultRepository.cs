using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;

namespace Swimm.Infrastructure.Repositories;

public class ResultRepository : IResultRepository
{
    // Read-only контекст (swimm_ro, SELECT-only роль) — публичный read-путь не имеет
    // привилегий записи на уровне БД.
    private readonly SwimmReadDbContext _db;
    private readonly ICacheService _cache;

    private static readonly TimeSpan StaticHintsTtl  = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DynamicHintsTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResultsTtl       = TimeSpan.FromMinutes(2);

    public ResultRepository(SwimmReadDbContext db, ICacheService cache)
    {
        _db    = db;
        _cache = cache;
    }

    public async Task<(List<ResultDto> Items, bool HasMore, int Total)> GetPagedAsync(ResultFilter filter, int page, int pageSize)
    {
        pageSize = Math.Min(pageSize, 500);
        var key = ResultsCacheKey(filter, page, pageSize);

        var cached = await _cache.GetAsync<(List<ResultDto>, bool, int)>(key);
        if (cached != default)
            return cached;

        var query = await BuildFilteredQueryAsync(filter);
        if (query is null)
            return ([], false, 0);

        // Total — отдельный кэш-ключ БЕЗ page/pageSize: листание страниц не пересчитывает COUNT.
        var totalKey = $"results-total:{FilterCacheKey(filter)}";
        var total = await _cache.GetAsync<int?>(totalKey) ?? -1;
        if (total < 0)
        {
            total = await query.CountAsync();
            await _cache.SetAsync<int?>(totalKey, total, ResultsTtl);
        }

        var items = await query
            .OrderByDescending(r => r.CompetitionDate)
            .ThenBy(r => r.Position)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ResultMapping.ToDto)
            .ToListAsync();

        // hasMore — из total; расхождение возможно только в пределах TTL кэша (2 мин), как и раньше.
        var hasMore = (page - 1) * pageSize + items.Count < total;

        var result = (items, hasMore, total);
        await _cache.SetAsync(key, result, ResultsTtl);
        return result;
    }

    /// <summary>
    /// Применяет весь фильтр к запросу результатов (включая разрешение <c>Latest</c> —
    /// потому и async). Возвращает <c>null</c>, если <c>Latest</c> запрошен, а данных нет
    /// (вызывающий отдаёт пустой ответ). Единая точка фильтрации для paged и агрегатов —
    /// чтобы семантика фильтров не разъезжалась между эндпоинтами.
    /// </summary>
    private async Task<IQueryable<Domain.Entities.ResultRecord>?> BuildFilteredQueryAsync(ResultFilter filter)
    {
        var query = _db.Results.AsNoTracking().AsQueryable();

        // "Последнее" соревнование: берём соревнование самого свежего результата;
        // если это день многодневного события — фильтруем по всему событию.
        if (filter.Latest)
        {
            var latest = await _db.Results.AsNoTracking()
                .OrderByDescending(r => r.CompetitionDate)
                .ThenByDescending(r => r.CompetitionId)
                .Select(r => new { r.CompetitionId, r.Competition.EventId })
                .FirstOrDefaultAsync();

            if (latest is null)
                return null;

            if (latest.EventId.HasValue)
                query = query.Where(r => r.Competition.EventId == latest.EventId.Value);
            else
                query = query.Where(r => r.CompetitionId == latest.CompetitionId);
        }

        // Стиль фильтруем по StyleId, а НЕ по Style.Name через JOIN: JOIN мешает планировщику
        // взять композитные индексы (StyleId,Distance,Gender,CompDate) и (CompetitionId,StyleId,
        // Distance,Gender) — на 3М это разница LIMIT 34→4 мс, COUNT 86→4 мс (под конкуренцией
        // именно JOIN-версия давала обрыв p95 ~14с, см. server/loadtest/full-scan-smoke.js).
        // Styles — 8 строк, карта Name→Id кэшируется. Неизвестный стиль → заведомо пустой набор.
        if (!string.IsNullOrWhiteSpace(filter.StyleName))
        {
            // Список (а не один id) сохраняет старую семантику Style.Name==x даже при дублях имени
            // (в реальной БД Name уникален → 1 элемент → Postgres сводит IN(x) к равенству, индекс
            // работает; пустой список → заведомо пустой набор).
            var styleIds = await ResolveStyleIdsAsync(filter.StyleName);
            query = styleIds.Length > 0
                ? query.Where(r => styleIds.Contains(r.StyleId))
                : query.Where(r => false);
        }

        if (!string.IsNullOrWhiteSpace(filter.Distance))
            query = query.Where(r => r.Distance == filter.Distance);

        if (!string.IsNullOrWhiteSpace(filter.Gender))
            query = query.Where(r => r.Gender == filter.Gender);

        if (!string.IsNullOrWhiteSpace(filter.PoolType))
            query = query.Where(r => r.Competition.PoolType == filter.PoolType);

        if (!string.IsNullOrWhiteSpace(filter.Country))
            query = query.Where(r => r.Competition.Country != null && r.Competition.Country.CountryCode == filter.Country);

        if (filter.DateFrom.HasValue)
            query = query.Where(r => r.CompetitionDate >= filter.DateFrom.Value);

        if (filter.DateTo.HasValue)
            query = query.Where(r => r.CompetitionDate <= filter.DateTo.Value);

        if (filter.EventId.HasValue)
            query = query.Where(r => r.Competition.EventId == filter.EventId.Value);

        if (filter.CompetitionId.HasValue)
            query = query.Where(r => r.CompetitionId == filter.CompetitionId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Competition))
            query = query.Where(r => r.Competition.Name.StartsWith(filter.Competition));

        if (!string.IsNullOrWhiteSpace(filter.Name))
            query = query.Where(r =>
                r.Swimmer.LastName.StartsWith(filter.Name) ||
                r.Swimmer.FirstName.StartsWith(filter.Name) ||
                r.Swimmer.LastNameEn.StartsWith(filter.Name) ||
                r.Swimmer.FirstNameEn.StartsWith(filter.Name));

        if (!string.IsNullOrWhiteSpace(filter.Club))
            query = query.Where(r => r.Club.Name.StartsWith(filter.Club) || r.Club.NameEn.StartsWith(filter.Club));

        if (filter.SwimmerIds is { Count: > 0 })
            query = query.Where(r => filter.SwimmerIds.Contains(r.SwimmerId));

        /* Параметры paged-режима (контракт docs/tasks/phase3-paged-results-contract.md) */

        // Клиентский фильтр Age — годы рождения, не возраст.
        if (filter.BirthYearFrom.HasValue)
            query = query.Where(r => r.Swimmer.BirthYear >= filter.BirthYearFrom.Value);

        if (filter.BirthYearTo.HasValue)
            query = query.Where(r => r.Swimmer.BirthYear <= filter.BirthYearTo.Value);

        if (!string.IsNullOrWhiteSpace(filter.AgeGroup))
            query = query.Where(r => r.AgeGroup == filter.AgeGroup);

        // Семантика мест — зеркало клиентского baseFilteredResults (паритет full/paged):
        // top (KeepUnranked=true) оставляет строки без места (DSQ/DNS), podium — исключает.
        if (filter.PositionMax.HasValue)
        {
            var max = filter.PositionMax.Value;
            query = filter.PositionKeepUnranked
                ? query.Where(r => r.Position == null || r.Position <= max)
                : query.Where(r => r.Position != null && r.Position <= max);
        }

        if (filter.EventDate.HasValue)
            query = query.Where(r => r.CompetitionDate == filter.EventDate.Value);

        return query;
    }

    /// <summary>Name→Id(ы) стиля из кэша (Styles — крошечный справочник, TTL 10 мин); пусто — нет
    /// такого. Матч точный, как раньше сравнение <c>Style.Name == filter.StyleName</c>. Возвращает
    /// список на случай неуникальных имён (в проде Name уникален — обычно 1 элемент).</summary>
    private async Task<int[]> ResolveStyleIdsAsync(string styleName)
    {
        const string key = "styles:name-to-ids";
        var map = await _cache.GetAsync<Dictionary<string, int[]>>(key);
        if (map is null)
        {
            map = (await _db.Styles.AsNoTracking().Select(s => new { s.Name, s.Id }).ToListAsync())
                .GroupBy(s => s.Name)
                .ToDictionary(g => g.Key, g => g.Select(s => s.Id).ToArray());
            await _cache.SetAsync(key, map, TimeSpan.FromMinutes(10));
        }
        return map.TryGetValue(styleName, out var ids) ? ids : [];
    }

    public async Task<IReadOnlyList<ClubSummaryDto>> GetClubSummaryAsync(ResultFilter filter)
    {
        var key = $"club-summary:{FilterCacheKey(filter)}";
        var cached = await _cache.GetAsync<IReadOnlyList<ClubSummaryDto>>(key);
        if (cached is not null)
            return cached;

        var query = await BuildFilteredQueryAsync(filter);
        if (query is null)
            return [];

        // Минимальная проекция для агрегации в памяти (клубов и заплывов в одном источнике мало).
        // Псевдоклубы (страна/сборная вместо клуба, Maccabiah) в клубный зачёт не входят.
        var rows = await query
            .Where(r => !r.Club.IsPseudo)
            .Select(r => new
            {
                ClubName = r.Club.Name,
                ClubNameEn = r.Club.NameEn,
                RelayTeamName = r.Relay != null ? r.Relay.TeamName : null,
                LastName = r.Swimmer.LastName,
                r.Position,
                r.TimeFail,
                IsRelay = r.RelayId != null,
                IsMasters = r.Competition.IsMasters,
                r.CompetitionDate
            })
            .ToListAsync();

        // Правила очков грузим целиком (их единицы), применяем в памяти — как в сезонном зачёте.
        var rules = await _db.ClubPointsRules.AsNoTracking()
            .Include(r => r.Entries)
            .ToListAsync();

        var map = new Dictionary<string, (int Points, HashSet<string> Swimmers, int Successful, int Gold, int Silver, int Bronze)>();

        foreach (var r in rows)
        {
            // Ключ клуба — как в клиентском getClubsSummary: club → relay_team_name → club_en.
            var club = FirstNonEmpty(r.ClubName, r.RelayTeamName, r.ClubNameEn);
            if (club is null) continue;

            // Очки: те же правила, что на клиенте; эстафета удваивает (fix relay club points).
            // timeFail НЕ гейтит (паритет с клиентским getPoints, который смотрит только на место).
            var basePoints = ClubPointsScoring.PointsFor(
                rules, r.Position, timeFail: false, r.IsMasters, DateOnly.FromDateTime(r.CompetitionDate));
            var points = r.IsRelay ? basePoints * 2 : basePoints;

            map.TryGetValue(club, out var e);
            e.Swimmers ??= new HashSet<string>();
            e.Points += points;
            if (points > 0) e.Successful += 1;
            if (!string.IsNullOrWhiteSpace(r.LastName)) e.Swimmers.Add(r.LastName.Trim());
            if (r.Position == 1) e.Gold += 1;
            else if (r.Position == 2) e.Silver += 1;
            else if (r.Position == 3) e.Bronze += 1;
            map[club] = e;
        }

        var summary = map
            .Select(kv => new ClubSummaryDto
            {
                Club = kv.Key,
                Points = kv.Value.Points,
                SwimmerCount = kv.Value.Swimmers.Count,
                SuccessfulCount = kv.Value.Successful,
                Gold = kv.Value.Gold,
                Silver = kv.Value.Silver,
                Bronze = kv.Value.Bronze
            })
            .OrderByDescending(c => c.Points)
            .ThenBy(c => c.Club)
            .ToList();

        await _cache.SetAsync(key, (IReadOnlyList<ClubSummaryDto>)summary, ResultsTtl);
        return summary;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        return null;
    }

    public async Task<ResultDto?> GetByIdAsync(long id)
    {
        return await _db.Results.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(ResultMapping.ToDto)
            .FirstOrDefaultAsync();
    }

    public async Task<string[]> GetFilterHintsAsync(string field, string? q, int limit)
    {
        limit = Math.Min(limit, 50);
        var prefix = (q ?? "").Trim();
        var key = $"hints:{field}:{prefix}";

        var cached = await _cache.GetAsync<string[]>(key);
        if (cached is not null)
            return cached;

        var ttl = field is "style" or "distance" ? StaticHintsTtl : DynamicHintsTtl;

        var hints = field switch
        {
            "style" => await _db.Styles
                .OrderBy(s => s.Name)
                .Select(s => s.Name)
                .ToArrayAsync(),

            "distance" => await _db.Results
                .Select(r => r.Distance)
                .Distinct()
                .OrderBy(d => d.Length)
                .ThenBy(d => d)
                .ToArrayAsync(),

            "club" => await _db.Clubs
                .Where(c => prefix.Length == 0 || c.Name.StartsWith(prefix) || c.NameEn.StartsWith(prefix))
                .Select(c => c.Name)
                .Where(n => n.Length > 0)
                .Distinct()
                .OrderBy(n => n)
                .Take(limit)
                .ToArrayAsync(),

            "competition" => await _db.Competitions
                .Where(c => prefix.Length == 0 || c.Name.StartsWith(prefix))
                .Select(c => c.Name)
                .Where(n => n.Length > 0)
                .Distinct()
                .OrderBy(n => n)
                .Take(limit)
                .ToArrayAsync(),

            "name" when prefix.Length > 0 => await _db.Swimmers
                .Where(s => s.LastName.StartsWith(prefix))
                .Select(s => s.LastName)
                .Union(_db.Swimmers.Where(s => s.FirstName.StartsWith(prefix)).Select(s => s.FirstName))
                .Where(n => n.Length > 0)
                .OrderBy(n => n)
                .Take(limit)
                .ToArrayAsync(),

            _ => []
        };

        if (hints.Length > 0)
            await _cache.SetAsync(key, hints, ttl);

        return hints;
    }

    private static string FilterCacheKey(ResultFilter f) =>
        $"{f.StyleName}:{f.Distance}:{f.Gender}:{f.PoolType}:{f.Country}" +
        $":{f.DateFrom:yyyyMMdd}:{f.DateTo:yyyyMMdd}:{f.Competition}:{f.EventId}:{f.CompetitionId}:{f.Latest}:{f.Name}:{f.Club}" +
        $":{f.BirthYearFrom}:{f.BirthYearTo}:{f.AgeGroup}:{f.PositionMax}:{f.PositionKeepUnranked}:{f.EventDate:yyyyMMdd}" +
        $":{(f.SwimmerIds is { Count: > 0 } ids ? string.Join(",", ids.OrderBy(x => x)) : "")}";

    private static string ResultsCacheKey(ResultFilter f, int page, int pageSize) =>
        $"results:{FilterCacheKey(f)}:{page}:{pageSize}";

    public async Task<IReadOnlyList<CompetitionSourceDto>> GetSourcesAsync(string? country = null)
    {
        // Нормализация как в GetStandardsAsync (RecordRepository): trim + upper, пусто — без фильтра.
        var countryKey = string.IsNullOrWhiteSpace(country) ? null : country.Trim().ToUpperInvariant();
        var key = $"competition-sources:{countryKey ?? "all"}";
        var cached = await _cache.GetAsync<IReadOnlyList<CompetitionSourceDto>>(key);
        if (cached is not null)
            return cached;

        // Многодневные события — сворачиваем в одну запись, агрегируя по дням.
        // Флаги по дням: masters/award — у ЛЮБОГО дня; show_combine — у ВСЕХ дней
        // (как !Any(!combine), чтобы EF надёжно транслировал в SQL). Пустые события пропускаем.
        var events = await _db.CompetitionEvents
            .AsNoTracking()
            .Where(e => _db.Competitions.Any(c => c.EventId == e.Id))
            .Select(e => new
            {
                e.Id,
                e.Name,
                e.StartDate,
                e.EndDate,
                DayCount = _db.Competitions.Count(c => c.EventId == e.Id),
                PoolType = _db.Competitions.Where(c => c.EventId == e.Id).Select(c => c.PoolType).FirstOrDefault(),
                IsMasters = _db.Competitions.Any(c => c.EventId == e.Id && c.IsMasters),
                IsAward = _db.Competitions.Any(c => c.EventId == e.Id && c.IsAward),
                ShowCombine = !_db.Competitions.Any(c => c.EventId == e.Id && !c.ShowCombineAllResults),
                ResultCount = _db.Results.Count(r => r.Competition.EventId == e.Id),
                DayDates = _db.Competitions.Where(c => c.EventId == e.Id).Select(c => c.Date).ToList(),
                // Страны всех дней (alpha-3 коды) — фильтр по country применяем ниже, в памяти (Any).
                DayCountries = _db.Competitions.Where(c => c.EventId == e.Id)
                    .Select(c => c.Country != null ? c.Country.CountryCode : "").ToList()
            })
            .ToListAsync();

        // Однодневные соревнования (без события).
        var singles = await _db.Competitions
            .AsNoTracking()
            .Where(c => c.EventId == null)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Date,
                c.PoolType,
                c.IsMasters,
                c.IsAward,
                c.ShowCombineAllResults,
                Country = c.Country != null ? c.Country.CountryCode : "",
                ResultCount = _db.Results.Count(r => r.CompetitionId == c.Id)
            })
            .ToListAsync();

        // Категория для селектора — из РЕАЛЬНОГО членства (CategoryCompetitions, те же
        // чекбоксы, что в админке), а не эвристики по возрасту: соревнование может быть
        // одновременно в Youth Results и Junior Results, поэтому приоритет
        // masters > youth-team (young8_11) > junior-results/main (junior).
        var categoryKeysByCompetition = await _db.CategoryCompetitions.AsNoTracking()
            .Select(cc => new { cc.CompetitionId, cc.Category.Key })
            .ToListAsync();

        var categoryKeysMap = categoryKeysByCompetition
            .GroupBy(cc => cc.CompetitionId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Key).ToHashSet());

        // День → событие, чтобы агрегировать членство по всем дням события.
        var dayToEvent = await _db.Competitions.AsNoTracking()
            .Where(c => c.EventId != null)
            .Select(c => new { c.Id, EventId = c.EventId!.Value })
            .ToListAsync();

        var categoryKeysByEvent = dayToEvent
            .Where(d => categoryKeysMap.ContainsKey(d.Id))
            .GroupBy(d => d.EventId)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(d => categoryKeysMap[d.Id]).ToHashSet());

        // null — соревнование ни в одной из канонических категорий (кастомные вроде
        // result-maccabiah или вовсе без категории): клиент покажет его в «All» и в табах
        // кастомных категорий по списку Categories, но НЕ в Junior. Раньше здесь был
        // фоллбек «всё прочее = junior», из-за которого в Junior падала вся синтетика.
        static string? CategoryFor(bool isMasters, HashSet<string>? keys) =>
            isMasters || keys?.Contains("results-masters") == true ? "masters"
            : keys?.Contains("results-youth-team") == true ? "young8_11"
            : keys?.Contains("results-junior-results") == true || keys?.Contains("results-main") == true ? "junior"
            : null;

        // Полное членство (сырые Category.Key) — для табов кастомных категорий на клиенте.
        static IReadOnlyList<string> CategoriesFor(HashSet<string>? keys) =>
            keys is null ? [] : keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        var today = DateOnly.FromDateTime(DateTime.Now);
        static string StatusFor(DateOnly? start, DateOnly? end, DateOnly today)
        {
            if (start is null) return "done";
            if (start.Value > today) return "upcoming";
            return (end ?? start.Value) >= today ? "live" : "done";
        }

        static string Fmt(DateOnly? d) => d?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";

        // Дни события по возрастанию даты (строки dd/MM/yyyy сортируем через парсинг).
        static List<string> SortDayDates(IEnumerable<string> dates) => dates
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct()
            .OrderBy(d => DateOnly.TryParseExact(d, "dd/MM/yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed) ? parsed : DateOnly.MinValue)
            .ToList();

        var items = new List<(DateOnly Sort, CompetitionSourceDto Dto)>(events.Count + singles.Count);

        // Фильтр по стране — в памяти, после сборки списков (маленький датасет, не усложняем EF).
        // Событие проходит фильтр, если country совпадает хотя бы у ОДНОГО дня (Any).
        var filteredEvents = countryKey is null
            ? events
            : events.Where(e => e.DayCountries.Any(c => c == countryKey)).ToList();
        var filteredSingles = countryKey is null
            ? singles
            : singles.Where(c => c.Country == countryKey).ToList();

        foreach (var e in filteredEvents)
        {
            items.Add((e.StartDate ?? DateOnly.MinValue, new CompetitionSourceDto
            {
                Kind = "event",
                Id = e.Id,
                Name = e.Name,
                Date = Fmt(e.StartDate),
                DateEnd = e.EndDate != e.StartDate ? Fmt(e.EndDate) : null,
                PoolType = e.PoolType ?? "",
                IsMasters = e.IsMasters,
                IsAward = e.IsAward,
                ShowCombineAllResults = e.ShowCombine,
                Category = CategoryFor(e.IsMasters, categoryKeysByEvent.GetValueOrDefault(e.Id)),
                Categories = CategoriesFor(categoryKeysByEvent.GetValueOrDefault(e.Id)),
                Status = StatusFor(e.StartDate, e.EndDate, today),
                DayCount = e.DayCount,
                ResultCount = e.ResultCount,
                DayDates = SortDayDates(e.DayDates)
            }));
        }

        foreach (var c in filteredSingles)
        {
            DateOnly.TryParseExact(c.Date, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d);
            items.Add((d, new CompetitionSourceDto
            {
                Kind = "competition",
                Id = c.Id,
                Name = c.Name,
                Date = c.Date,
                PoolType = c.PoolType,
                IsMasters = c.IsMasters,
                IsAward = c.IsAward,
                ShowCombineAllResults = c.ShowCombineAllResults,
                Category = CategoryFor(c.IsMasters, categoryKeysMap.GetValueOrDefault(c.Id)),
                Categories = CategoriesFor(categoryKeysMap.GetValueOrDefault(c.Id)),
                Status = StatusFor(d == default ? null : d, null, today),
                DayCount = 1,
                ResultCount = c.ResultCount,
                DayDates = string.IsNullOrWhiteSpace(c.Date) ? [] : [c.Date]
            }));
        }

        var ordered = items
            .OrderByDescending(x => x.Sort)
            .ThenBy(x => x.Dto.Name)
            .Select(x => x.Dto)
            .ToList();

        await _cache.SetAsync(key, (IReadOnlyList<CompetitionSourceDto>)ordered, TimeSpan.FromMinutes(5));
        return ordered;
    }

    public async Task<AthleteCareerDto?> GetAthleteCareerAsync(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return null;

        var key = $"athlete-career:{name.ToLowerInvariant()}";
        var cached = await _cache.GetAsync<AthleteCareerDto>(key);
        if (cached is not null)
            return cached;

        // Клиент передаёт полное имя ("First Last"); матчим оба порядка + EN-вариант.
        var rows = await _db.Results.AsNoTracking()
            .Where(r => r.RelayId == null && (
                r.Swimmer.FirstName + " " + r.Swimmer.LastName == name ||
                r.Swimmer.LastName + " " + r.Swimmer.FirstName == name ||
                r.Swimmer.FirstNameEn + " " + r.Swimmer.LastNameEn == name ||
                r.Swimmer.LastNameEn + " " + r.Swimmer.FirstNameEn == name))
            .Select(r => new
            {
                r.CompetitionId,
                EventId = (int?)r.Competition.EventId,
                r.CompetitionDate,
                r.Position,
                r.InternationalPoints,
                r.TimeMillisecond,
                r.TimeOriginal,
                r.TimeFail,
                StyleName = r.Style.Name,
                r.Distance,
                Pool = r.Competition.PoolType,
                CompetitionName = r.Competition.Name,
                DateRaw = r.Competition.Date,
                r.Gender,
                r.EventStyleAge,
                r.AgeGroup,
                IsMasters = r.Competition.IsMasters,
                IsAward = r.Competition.IsAward
            })
            .ToListAsync();

        // Эстафеты: в БД одна строка Result на команду, привязана к ОДНОМУ "первому" пловцу
        // (SwimmerId), остальные участники — только строкой Relay.SwimmersName ("Имя Фамилия, …").
        // Поэтому медаль за эстафету не находится обычным матчем по Swimmer — ищем спортсмена
        // в SwimmersName у ЛЮБОЙ эстафеты (не только "своей" по SwimmerId).
        // Грубая SQL-фильтрация по вхождению имени, точная проверка — посегментно в C#.
        var nameTokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var relayCandidates = nameTokens.Length == 0
            ? []
            : (await _db.Results.AsNoTracking()
                .Where(r => r.RelayId != null)
                .Select(r => new
                {
                    r.CompetitionId,
                    EventId = (int?)r.Competition.EventId,
                    r.Position,
                    r.Relay!.SwimmersName,
                    StyleName = r.Style.Name,
                    r.Distance,
                    CompetitionName = r.Competition.Name,
                    DateRaw = r.Competition.Date
                })
                .ToListAsync())
                // Грубый фильтр по вхождению имени — на клиенте (EF InMemory-провайдер в тестах
                // не транслирует Any() с захваченным массивом внутри Where; на Postgres было бы
                // эффективнее фильтровать в SQL, но датасет эстафет некрупный — не критично).
                .Where(r => nameTokens.Any(t => r.SwimmersName != null && r.SwimmersName.Contains(t)))
                .ToList();

        static bool SegmentMatchesName(string segment, string name)
        {
            segment = segment.Trim();
            if (segment == name) return true;
            var parts = segment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 2 && $"{parts[1]} {parts[0]}" == name;
        }

        var relayMedals = relayCandidates
            .Where(r => (r.SwimmersName ?? "").Split(',').Any(seg => SegmentMatchesName(seg, name)))
            .ToList();

        if (rows.Count == 0 && relayMedals.Count == 0) return null;

        // Лучшее время на (стиль × дистанция) — только валидные времена.
        var bestByStyle = rows
            .Where(r => r.TimeMillisecond != null && !r.TimeFail)
            .GroupBy(r => new { r.StyleName, r.Distance })
            .Select(g => g.OrderBy(r => r.TimeMillisecond).First())
            .OrderByDescending(r => r.InternationalPoints)
            .Select(r => new CareerBestDto
            {
                Stroke = r.StyleName,
                Distance = r.Distance,
                Time = r.TimeOriginal,
                Points = r.InternationalPoints,
                Pool = r.Pool,
                Competition = r.CompetitionName,
                Date = r.DateRaw,
                Position = r.Position,
                Gender = r.Gender,
                EventStyleAge = r.EventStyleAge,
                AgeGroup = r.AgeGroup,
                IsMasters = r.IsMasters,
                IsAward = r.IsAward
            })
            .ToList();

        // Разбивка медалей по конкретным заплывам — для тултипа "за что" на карточке.
        var medals = rows
            .Where(r => r.Position is 1 or 2 or 3)
            .Select(r => new MedalDetailDto
            {
                Position = r.Position!.Value,
                Note = $"{r.StyleName} {r.Distance}м",
                Competition = r.CompetitionName,
                Date = r.DateRaw
            })
            .Concat(relayMedals
                .Where(r => r.Position is 1 or 2 or 3)
                .Select(r => new MedalDetailDto
                {
                    Position = r.Position!.Value,
                    Note = $"{r.StyleName} {r.Distance}м · relay",
                    Competition = r.CompetitionName,
                    Date = r.DateRaw
                }))
            .OrderBy(m => m.Position)
            .ToList();

        var dto = new AthleteCareerDto
        {
            // Ключ дедупликации: у многодневных событий все дни делят один EventId — считаем
            // событие как одно "соревнование" карьеры, а не по числу дней.
            Competitions = rows.Select(r => r.EventId != null ? $"e{r.EventId}" : $"c{r.CompetitionId}")
                .Concat(relayMedals.Select(r => r.EventId != null ? $"e{r.EventId}" : $"c{r.CompetitionId}"))
                .Distinct()
                .Count(),
            Races = rows.Count,
            Since = rows.Count > 0 ? rows.Min(r => r.CompetitionDate).Year : DateTime.Now.Year,
            TotalPoints = rows.Sum(r => r.InternationalPoints),
            // Медали за эстафету (relayMedals) считаются к общему итогу — командная награда
            // так же личная, как индивидуальная (see [[athlete-alltime-card]]).
            Gold = rows.Count(r => r.Position == 1) + relayMedals.Count(r => r.Position == 1),
            Silver = rows.Count(r => r.Position == 2) + relayMedals.Count(r => r.Position == 2),
            Bronze = rows.Count(r => r.Position == 3) + relayMedals.Count(r => r.Position == 3),
            BestByStyle = bestByStyle,
            Medals = medals
        };

        await _cache.SetAsync(key, dto, TimeSpan.FromMinutes(5));
        return dto;
    }
}
