using System.Globalization;
using Swimm.Domain;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;

namespace Swimm.Infrastructure.Repositories;

public class ResultRepository : IResultRepository
{
    // Read-only контекст (swimm_ro, SELECT-only роль) — публичный read-путь не имеет
    // привилегий записи на уровне БД.
    private readonly SwimmReadDbContext _db;
    private readonly ICacheService _cache;
    // null допустим: так репозиторий конструируют изолированные тесты, которым рекорды
    // не нужны. В приложении настройка приходит из DI, и ось берётся из /Admin/Settings.
    private readonly ISettingsService? _settings;

    private static readonly TimeSpan StaticHintsTtl  = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DynamicHintsTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResultsTtl       = TimeSpan.FromMinutes(2);

    public ResultRepository(SwimmReadDbContext db, ICacheService cache, ISettingsService? settings = null)
    {
        _db       = db;
        _cache    = cache;
        _settings = settings;
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
            // Id — стабильный tie-breaker: (дата, позиция) массово неуникальны (одно место в
            // каждой возрастной группе каждого стиля), и без него OFFSET-пагинация Postgres
            // дублирует строки на стыках страниц и молча теряет другие (замер 2026-07-20 на
            // событии 5: 198 дублей из 1670 строк).
            .ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ResultMapping.ToDto)
            .ToListAsync();

        await ApplyClubPointsAsync(items);

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
        {
            var eventCompIds = await ResolveEventCompetitionIdsAsync(filter.EventId.Value);
            query = eventCompIds.Length > 0
                ? query.Where(r => eventCompIds.Contains(r.CompetitionId))
                : query.Where(r => false);
        }

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

    /// <summary>EventId → его CompetitionId(ы) из кэша (TTL 10 мин). Пусто — событие без
    /// соревнований или несуществующий id. Резолвим в Id, чтобы фильтр по Results шёл по
    /// композитному индексу (CompetitionId,...), а не через JOIN на Competition.EventId —
    /// последнее на большом объёме заставляет планировщик сканировать всю таблицу.</summary>
    private async Task<int[]> ResolveEventCompetitionIdsAsync(int eventId)
    {
        var key = $"event-competitions:{eventId}";
        var ids = await _cache.GetAsync<int[]>(key);
        if (ids is null)
        {
            ids = await _db.Competitions.AsNoTracking()
                .Where(c => c.EventId == eventId)
                .Select(c => c.Id)
                .ToArrayAsync();
            await _cache.SetAsync(key, ids, TimeSpan.FromMinutes(10));
        }
        return ids;
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
                r.ClubId,
                ClubName = r.Club.Name,
                ClubNameEn = r.Club.NameEn,
                RelayTeamName = r.Relay != null ? r.Relay.TeamName : null,
                r.SwimmerId,
                // Место prelim-заплыва — ранжир сессии, не награда: медали и клубные очки
                // дают за финал (у бугрим протокол печатает места и в предварительных).
                // Общий финал «כללי» (Round=final-open) тоже не зачётный: единица зачёта —
                // возрастная ступень, там пловец очки и получает (Р43).
                Position = r.HeatType == "prelim" || r.HeatType == "extra" || r.Round == ResultRounds.FinalOpen
                    ? null : r.Position,
                r.CombinedPlace,
                ShowCombine = r.Competition.ShowCombineAllResults,
                r.TimeFail,
                IsRelay = r.RelayId != null,
                IsMasters = r.Competition.IsMasters,
                RuleId = r.Competition.PointRuleClubsId,
                r.CompetitionDate
            })
            .ToListAsync();

        // Правила очков грузим целиком (их единицы), применяем в памяти — как в сезонном зачёте.
        var rules = await _db.PointRulesClubs.AsNoTracking()
            .Include(r => r.Entries)
            .ToListAsync();

        // Агрегация и ранжирование — в общем ClubStandingCalculator (Application/Mapping):
        // тот же алгоритм считает материализованный зачёт страницы клуба. Здесь остаётся
        // только то, что зависит от Infrastructure: правило очков и выбор места.
        var scoring = new List<ClubScoringRow>(rows.Count);
        foreach (var r in rows)
        {
            // Ключ клуба — как в клиентском getClubsSummary: club → relay_team_name → club_en.
            var club = FirstNonEmpty(r.ClubName, r.RelayTeamName, r.ClubNameEn);
            if (club is null) continue;

            // Правило: привязка соревнования важнее подбора по дате (см. CompetitionRuleResolver).
            // Эстафетный множитель берётся из правила (был хардкод *2).
            // TimeFail (DSQ/незачтённое время) очков НЕ приносит — как в сезонном зачёте групп
            // и как считаются медали. Раньше здесь стояло timeFail: false ради паритета с
            // клиентским getPoints, и один и тот же заплыв давал клубу очки на странице
            // соревнования, но не давал в сезонном зачёте (план §7.6).
            var rule = CompetitionRuleResolver.Resolve(
                rules, r.RuleId, r.IsMasters, DateOnly.FromDateTime(r.CompetitionDate));

            // Соревнование объявлено объединённым → зачёт идёт по объединённому месту
            // дисциплины (по всему событию), а не по месту внутри своего заплыва/дня.
            // Раньше здесь всегда стояло протокольное Position, из-за чего Overview
            // на combine-all соревнованиях показывал очки не той системы (план Э2).
            var place = EffectivePlace(filter.Combined && r.ShowCombine, r.Position, r.CombinedPlace);
            var points = PointRulesClubsScoring.RelayPointsFor(rule, place, r.TimeFail, r.IsRelay);

            scoring.Add(new ClubScoringRow(
                ClubId: r.ClubId, ClubKey: club, SwimmerId: r.SwimmerId,
                Place: place, IsRelay: r.IsRelay, Points: points));
        }

        var summary = ClubStandingCalculator.Build(scoring)
            .Select(c => new ClubSummaryDto
            {
                Club = c.ClubKey,
                Points = c.Points,
                SwimmerCount = c.SwimmerCount,
                SuccessfulCount = c.ScoringSwims,
                Gold = c.Gold,
                Silver = c.Silver,
                Bronze = c.Bronze
            })
            .ToList();

        await _cache.SetAsync(key, (IReadOnlyList<ClubSummaryDto>)summary, ResultsTtl);
        return summary;
    }

    public async Task<CompetitionOverviewDto> GetCompetitionOverviewAsync(ResultFilter filter)
    {
        var key = $"competition-overview:{FilterCacheKey(filter)}";
        var cached = await _cache.GetAsync<CompetitionOverviewDto>(key);
        if (cached is not null)
            return cached;

        var query = await BuildFilteredQueryAsync(filter);
        if (query is null)
            return new CompetitionOverviewDto();

        // Дни источника: для события — по одному на Competition-день, для однодневного — один.
        var days = await query
            .GroupBy(r => new
            {
                r.CompetitionId,
                r.Competition.Date,
                r.Competition.DayNumber,
                r.Competition.SubName
            })
            .Select(g => new OverviewDayDto
            {
                CompetitionId = g.Key.CompetitionId,
                Date = g.Key.Date,
                DayNumber = g.Key.DayNumber,
                SubName = g.Key.SubName,
                ResultCount = g.Count()
            })
            .ToListAsync();
        days = days
            .OrderBy(d => d.DayNumber ?? int.MaxValue)
            .ThenBy(d => ParseDayDate(d.Date))
            .ToList();

        // compID федерации для таба Start list (шаг С7 плана start-list-plan.md): берём у
        // первого дня в выборке — Competition.OrgCompId, а если пуст (штамп многодневки
        // стоит на событии, см. CompetitionIdentity) — Competition.Event.OrgCompId.
        var firstCompetitionId = days.Select(d => d.CompetitionId).FirstOrDefault();
        var orgCompId = firstCompetitionId != 0
            ? await _db.Competitions.AsNoTracking()
                .Where(c => c.Id == firstCompetitionId)
                .Select(c => c.OrgCompId ?? (c.Event != null ? c.Event.OrgCompId : null))
                .FirstOrDefaultAsync()
            : null;

        var resultCount = days.Sum(d => d.ResultCount);
        // Личные пловцы; эстафетные строки не раздувают счётчик участников.
        var swimmerCount = await query.Where(r => r.RelayId == null)
            .Select(r => r.SwimmerId).Distinct().CountAsync();
        var clubCount = await query.Where(r => !r.Club.IsPseudo)
            .Select(r => r.ClubId).Distinct().CountAsync();

        // Наградной ли протокол: у ненаградных места в протоколе есть, а медалей нет —
        // клиент по этому флагу прячет медальные блоки Overview (см. CompetitionOverviewDto).
        // Any, а не All: если у многодневного события наградной хотя бы один день, медали есть.
        var hasAwards = await query.AnyAsync(r => r.Competition.IsAward);

        // Лучший заплыв — максимум FINA-очков; тай-брейк по времени, затем Id (стабильность).
        // ♂/♀ (design_handoff вариант 4) — та же проекция с фильтром по полу.
        static IQueryable<OverviewBestSwimDto> BestSwimProjection(IQueryable<Domain.Entities.ResultRecord> q) =>
            q.Where(r => !r.TimeFail && r.InternationalPoints > 0)
             .OrderByDescending(r => r.InternationalPoints)
             .ThenBy(r => r.TimeMillisecond)
             .ThenBy(r => r.Id)
             .Select(r => new OverviewBestSwimDto
             {
                 ResultId = r.Id,
                 SwimmerId = r.SwimmerId,
                 FirstName = r.Swimmer.FirstName,
                 LastName = r.Swimmer.LastName,
                 FirstNameEn = r.Swimmer.FirstNameEn,
                 LastNameEn = r.Swimmer.LastNameEn,
                 Club = r.Club.Name,
                 StyleName = r.Style.Name,
                 Distance = r.Distance,
                 Gender = r.Gender,
                 Time = r.TimeOriginal,
                 SuspectReason = r.SuspectReason,
                 Points = r.InternationalPoints,
                 IsRelay = r.RelayId != null,
                 RelayTeamName = r.Relay != null ? r.Relay.TeamName : null,
                 DayNumber = r.Competition.DayNumber,
                 CompetitionId = r.CompetitionId
             });

        var bestSwim = await BestSwimProjection(query).FirstOrDefaultAsync();
        var bestSwimMale = await BestSwimProjection(query.Where(r => r.Gender == "male")).FirstOrDefaultAsync();
        var bestSwimFemale = await BestSwimProjection(query.Where(r => r.Gender == "female")).FirstOrDefaultAsync();

        // Медальный зачёт: личные заплывы + эстафеты. TimeFail медаль не даёт.
        // При combine-all медаль определяется объединённым местом дисциплины — иначе на
        // одной странице «золото» по протоколу заплыва, а очки клубу по общему зачёту.
        // Проекция — в анонимный тип, маппинг в MedalRow уже в памяти: фильтр по месту стоит
        // ПОСЛЕ проекции, а обращение к свойству record'а EF перевести в SQL не может
        // (InMemory-провайдер это не ловит — он считает всё клиентски).
        var personalMedals = (await query
            .Where(r => r.RelayId == null && !r.TimeFail)
            .Select(r => new
            {
                r.SwimmerId,
                r.Swimmer.FirstName,
                r.Swimmer.LastName,
                r.Swimmer.FirstNameEn,
                r.Swimmer.LastNameEn,
                Club = r.Club.Name,
                r.Gender,
                // Prelim: протокольное место не медаль (ранжир сессии); объединённое место,
                // если оно лежит на prelim-строке (лучший заплыв был утром), остаётся.
                // Общий финал «כללי» медали тоже не даёт — она у возрастной ступени (Р43).
                Position = filter.Combined && r.Competition.ShowCombineAllResults
                    ? (r.CombinedPlace ?? (r.HeatType == "prelim" || r.HeatType == "extra"
                        || r.Round == ResultRounds.FinalOpen ? null : r.Position))
                    : (r.HeatType == "prelim" || r.HeatType == "extra"
                        || r.Round == ResultRounds.FinalOpen ? null : r.Position),
                StyleName = r.Style.Name,
                r.Distance,
                r.EventStyleAge,
                r.Round
            })
            .Where(r => r.Position != null && r.Position <= 3)
            .ToListAsync())
            .Select(r => new MedalRow(
                r.SwimmerId, r.FirstName, r.LastName, r.FirstNameEn, r.LastNameEn,
                r.Club, r.Gender, r.Position, false,
                $"{r.SwimmerId}|{r.StyleName}|{r.Distance}|{r.EventStyleAge}", r.Round));

        // Эстафетная медаль принадлежит ВСЕЙ команде — разворачиваем строку на ноги через
        // RelayMembers (docs/relays.md: считать по владельцу строки — классический баг, медаль
        // получил бы только якорь). Пол берём у самого пловца: эстафеты бывают смешанные,
        // и Gender строки результата для разбивки ♂/♀ не годится.
        var relayMedals = (await query
            .Where(r => r.RelayId != null && !r.TimeFail)
            .Select(r => new
            {
                r.RelayId,
                Club = r.Club.Name,
                Position = filter.Combined && r.Competition.ShowCombineAllResults
                    ? (r.CombinedPlace ?? (r.HeatType == "prelim" || r.HeatType == "extra" ? null : r.Position))
                    : (r.HeatType == "prelim" || r.HeatType == "extra" ? null : r.Position)
            })
            .Where(r => r.Position != null && r.Position <= 3)
            .Join(_db.RelayMembers.AsNoTracking(),
                r => r.RelayId, m => m.RelayId,
                (r, m) => new
                {
                    m.SwimmerId,
                    m.Swimmer.FirstName,
                    m.Swimmer.LastName,
                    m.Swimmer.FirstNameEn,
                    m.Swimmer.LastNameEn,
                    r.Club,
                    m.Swimmer.Gender,
                    r.Position
                })
            .ToListAsync())
            .Select(r => new MedalRow(
                r.SwimmerId, r.FirstName, r.LastName, r.FirstNameEn, r.LastNameEn,
                r.Club, r.Gender, r.Position, true));

        // Медаль возрастной ступени одна, даже если ступень разыграна дважды за день
        // (утренний зачёт + вечерний финал): раунды схлопываются по лучшему месту.
        // Клубных очков это не касается — там задвоение официальное.
        var medalRows = RoundMedalCollapser
            .Collapse(personalMedals, r => r.MedalKey, r => r.Round, r => r.Position)
            .Concat(relayMedals)
            .ToList();

        // Топ-медалисты общие и по полу (design_handoff вариант 4). gender == null → без фильтра.
        // Порядок — золото → серебро → бронза, а НЕ по сумме наград: три бронзы не выше одного
        // золота. Отдаём всех с идентичным набором (ничья), как в High Point Award.
        IReadOnlyList<OverviewMedalistDto> BuildMedalists(string? gender)
        {
            var ranked = medalRows
                .Where(r => gender == null || r.Gender == gender)
                .GroupBy(r => r.SwimmerId)
                .Select(g => new OverviewMedalistDto
                {
                    SwimmerId = g.Key,
                    FirstName = g.First().FirstName,
                    LastName = g.First().LastName,
                    FirstNameEn = g.First().FirstNameEn,
                    LastNameEn = g.First().LastNameEn,
                    Club = g.First().Club,
                    Gold = g.Count(r => r.Position == 1),
                    Silver = g.Count(r => r.Position == 2),
                    Bronze = g.Count(r => r.Position == 3),
                    RelayMedals = g.Count(r => r.IsRelay)
                })
                .OrderByDescending(m => m.Gold)
                .ThenByDescending(m => m.Silver)
                .ThenByDescending(m => m.Bronze)
                .ThenBy(m => m.SwimmerId)
                .ToList();

            if (ranked.Count == 0) return [];

            var top = ranked[0];
            var winners = ranked
                .Where(m => m.Gold == top.Gold && m.Silver == top.Silver && m.Bronze == top.Bronze)
                .ToList();

            return winners.Count == 1
                ? winners
                : winners.Select(m => new OverviewMedalistDto
                {
                    SwimmerId = m.SwimmerId,
                    FirstName = m.FirstName,
                    LastName = m.LastName,
                    FirstNameEn = m.FirstNameEn,
                    LastNameEn = m.LastNameEn,
                    Club = m.Club,
                    Gold = m.Gold,
                    Silver = m.Silver,
                    Bronze = m.Bronze,
                    RelayMedals = m.RelayMedals,
                    IsTie = true
                }).ToList();
        }

        var topMedalists = BuildMedalists(null);
        var topMedalistsMale = BuildMedalists("male");
        var topMedalistsFemale = BuildMedalists("female");

        // High Point Award: лучший по СУММЕ очков в каждом (возраст × пол), раздельно ♂/♀
        // (design_handoff §High Point Award). Возраст = год соревнования − год рождения
        // (израильская возрастная конвенция). Ничья по сумме → несколько наград (is_tie).
        // InternationalPoints > 0 в выборку НЕ входит: правило может считать очки за место
        // (§8.A), где FINA-очков может не быть вовсе. Legacy-ветка ниже накладывает это
        // условие в памяти, чтобы её результат остался байт-в-байт прежним.
        // Эстафеты в личный зачёт НЕ идут — решение Влада 2026-07-28 (§8.2 плана закрыт).
        // Исключаются здесь, а не флагом IncludeRelays: фильтр обязан действовать и для
        // legacy-ветки (соревнование без правила), где эстафетные FINA-очки иначе попали бы
        // в сумму пловца. Флаг правила остаётся вторым рубежом в PointRulesSwimmersScoring.
        // NB: в медальном зачёте «Most decorated» эстафеты, наоборот, считаются.
        var hpRows = await query
            .Where(r => r.RelayId == null && !r.TimeFail
                        && (r.Gender == "male" || r.Gender == "female")
                        && r.Swimmer.BirthYear > 0
                        // combine-all: дисциплина зачитывается один раз — по лучшему заплыву,
                        // иначе повторный старт удваивал бы вклад в сумму FINA.
                        && (!filter.Combined || !r.Competition.ShowCombineAllResults || r.IsBestResult != false))
            .Select(r => new
            {
                r.SwimmerId,
                r.Swimmer.FirstName,
                r.Swimmer.LastName,
                r.Swimmer.FirstNameEn,
                r.Swimmer.LastNameEn,
                Club = r.Club.Name,
                r.Gender,
                r.Swimmer.BirthYear,
                Year = r.CompetitionDate.Year,
                r.AgeGroup,
                IsMasters = r.Competition.IsMasters,
                r.InternationalPoints,
                // Э2.5: поля для расчёта по правилу. Место берём объединённое, если
                // соревнование его считает и тоггл включён — иначе место в заплыве.
                Place = filter.Combined && r.Competition.ShowCombineAllResults && r.CombinedPlace != null
                    ? r.CombinedPlace
                    : r.Position,
                RuleId = r.Competition.PointRuleSwimmersId,
                r.CompetitionDate,
                // Для бонуса за возрастной рекорд (§8.A: 13 за установленный, 8 за повторённый).
                StyleName = r.Style.Name,
                r.Distance,
                PoolType = r.Competition.PoolType,
                r.TimeMillisecond,
                r.HeatType
            })
            .ToListAsync();

        // Правило High Point: привязка соревнования важнее подбора по дате (CompetitionRuleResolver).
        // Правил единицы — грузим целиком и применяем в памяти, как в клубном зачёте.
        var swimmerRules = await _db.PointRulesSwimmers.AsNoTracking()
            .Include(r => r.Entries)
            .ToListAsync();

        var hpRule = hpRows.Count == 0 ? null : CompetitionRuleResolver.Resolve(
            swimmerRules,
            hpRows.Select(r => r.RuleId).FirstOrDefault(id => id != null),
            hpRows.Any(r => r.IsMasters),
            DateOnly.FromDateTime(hpRows.Min(r => r.CompetitionDate)));

        // Masters: корзина — возрастная ГРУППА ("25-29", как в фильтрах), топ-5 по очкам на пол.
        // Не-masters: корзина — отдельный возраст (год − год рождения), все возрасты по порядку.
        // Группировка по возрастным ГРУППАМ — только для masters (Competition.IsMasters).
        // Молодёжные категории (8-11, 11-14) тоже имеют AgeGroup в данных, но там HPA
        // должен идти по отдельным возрастам, поэтому детектим строго по флагу masters.
        var isMastersOverview = hpRows.Any(r => r.IsMasters);

        // Сумма очков пловца в (корзина × пол), затем max по корзине; ничья по сумме → все.
        // Legacy-ветка (правило не привязано): та же логика, что была до Э2.5, включая
        // условие InternationalPoints > 0 — оно перенесено сюда из SQL-запроса.
        var perSwimmer = hpRows
            .Where(r => r.InternationalPoints > 0)
            .Where(r => isMastersOverview ? !string.IsNullOrEmpty(r.AgeGroup) : (r.Year - r.BirthYear) > 0)
            .GroupBy(r => new
            {
                Bucket = isMastersOverview ? r.AgeGroup : (r.Year - r.BirthYear).ToString(),
                r.Gender,
                r.SwimmerId,
            })
            .Select(g => new
            {
                g.Key.Bucket,
                g.Key.Gender,
                g.Key.SwimmerId,
                g.First().FirstName,
                g.First().LastName,
                g.First().FirstNameEn,
                g.First().LastNameEn,
                g.First().Club,
                Age = g.First().Year - g.First().BirthYear,
                g.First().AgeGroup,
                Points = g.Sum(r => r.InternationalPoints)
            })
            .ToList();

        var winners = perSwimmer
            .GroupBy(x => new { x.Bucket, x.Gender })
            .SelectMany(g =>
            {
                var max = g.Max(x => x.Points);
                var w = g.Where(x => x.Points == max).ToList();
                var tie = w.Count > 1;
                return w.Select(x => new OverviewHighPointDto
                {
                    Age = isMastersOverview ? 0 : x.Age,
                    AgeGroup = isMastersOverview ? x.AgeGroup : "",
                    Gender = x.Gender,
                    SwimmerId = x.SwimmerId,
                    FirstName = x.FirstName,
                    LastName = x.LastName,
                    FirstNameEn = x.FirstNameEn,
                    LastNameEn = x.LastNameEn,
                    Club = x.Club,
                    Points = x.Points,
                    IsTie = tie
                });
            });

        // Masters: все возрастные группы по порядку (нижняя граница ↑). Не-masters: по возрасту.
        // Ведущие цифры — чтобы "90+" (без дефиса) шёл после "85-89", а не в начало.
        static int AgeGroupLow(string ag)
        {
            var digits = new string(ag.TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digits, out var n) ? n : 0;
        }
        var highPointAwards = isMastersOverview
            ? winners.OrderBy(a => AgeGroupLow(a.AgeGroup)).ThenBy(a => a.LastName).ToList()
            : winners.OrderBy(a => a.Age).ThenBy(a => a.LastName).ToList();

        // Э2.5: если соревнованию привязано правило — счёт по нему, а не по зашитой
        // FINA-сумме. Именно ради этого правило и заведено: у возрастных соревнований очки
        // за место плюс замещающий бонус за возрастной рекорд, у «бугрим» — сумма FINA с
        // одним кубком на пол (§8.A / §8.B.1 плана). Legacy-ветка выше остаётся для
        // соревнований без привязки.
        if (hpRule is not null)
        {
            var byId = hpRows
                .GroupBy(r => r.SwimmerId)
                .ToDictionary(g => g.Key, g => g.First());

            // Бонус за возрастной рекорд начисляется, только если правило его задаёт.
            // Индекс строим по той же оси, что и CompetitionRecordsDetector:
            // gender|pool|style|distance|category|ageKey, где category — age или masters.
            var recordIndex = new Dictionary<string, int>();
            if (hpRule.RecordPoints is not null || hpRule.RecordTiePoints is not null)
            {
                foreach (var rec in await GetIsraelRecordsAsync())
                {
                    if (rec.Category is not ("age" or "masters")) continue;
                    var ms = CompetitionRecordsDetector.ParseTimeToMs(rec.Time);
                    if (ms is null) continue;
                    var dist = rec.Distance.EndsWith('m') ? rec.Distance[..^1] : rec.Distance;
                    var recKey = $"{rec.Gender}|{rec.PoolType}|{rec.Style}|{dist}|{rec.Category}|{rec.AgeKey}";
                    if (!recordIndex.TryGetValue(recKey, out var cur) || ms.Value < cur)
                        recordIndex[recKey] = ms.Value;
                }
            }

            RecordStatus RecordStatusOf(int age, string gender, string pool, string style,
                string distance, string ageGroup, bool isMasters, int? timeMs)
            {
                if (recordIndex.Count == 0 || timeMs is null or <= 0) return RecordStatus.None;
                var category = isMasters ? "masters" : "age";
                var ageKey = isMasters ? ageGroup : age.ToString();
                if (!recordIndex.TryGetValue($"{gender}|{pool}|{style}|{distance}|{category}|{ageKey}", out var recMs))
                    return RecordStatus.None;
                if (timeMs.Value < recMs) return RecordStatus.Broken;
                return timeMs.Value == recMs ? RecordStatus.Tied : RecordStatus.None;
            }

            var ruleWinners = PointRulesSwimmersScoring.Winners(hpRule, hpRows
                .Select(r => new SwimmerHighPointRow(
                    SwimmerId: r.SwimmerId,
                    Gender: r.Gender,
                    Age: r.Year - r.BirthYear,
                    AgeGroup: r.AgeGroup,
                    Place: r.Place,
                    InternationalPoints: r.InternationalPoints,
                    IsRelay: false,
                    TimeFail: false,
                    Record: RecordStatusOf(r.Year - r.BirthYear, r.Gender, r.PoolType, r.StyleName,
                        r.Distance, r.AgeGroup, r.IsMasters, r.TimeMillisecond),
                    HeatType: r.HeatType))
                .ToList());

            // «Только финалы» невыполнимо, лишь пока признака заплыва нет НИ У ОДНОЙ строки
            // (данные импортированы до HeatType). Есть хоть одна — расчёт честный, сноска
            // «по всем заплывам» на клиенте не нужна.
            var finalsOnlyUnavailable = hpRule.FinalsOnly && hpRows.All(r => r.HeatType == null);

            highPointAwards = ruleWinners
                .Select(w =>
                {
                    var src = byId[w.SwimmerId];
                    return new OverviewHighPointDto
                    {
                        // GroupBy=age-group подаёт номинацию как группу (карточка показывает
                        // её вместо возраста) — так же, как в masters-ветке legacy.
                        Age = hpRule.GroupBy == "age" ? w.Age : 0,
                        AgeGroup = hpRule.GroupBy == "age" ? "" : w.Bucket,
                        Gender = w.Gender,
                        SwimmerId = w.SwimmerId,
                        FirstName = src.FirstName,
                        LastName = src.LastName,
                        FirstNameEn = src.FirstNameEn,
                        LastNameEn = src.LastNameEn,
                        Club = src.Club,
                        Points = w.Points,
                        IsTie = w.IsTie,
                        RuleVersion = hpRule.Version,
                        GroupLabel = hpRule.GroupBy == "age" ? null : w.Bucket,
                        FinalsOnlyUnavailable = finalsOnlyUnavailable
                    };
                })
                .OrderBy(a => a.AgeGroup.Length > 0 ? AgeGroupLow(a.AgeGroup) : a.Age)
                .ThenBy(a => a.LastName)
                .ToList();
        }

        // Клубный зачёт — переиспользуем GetClubSummaryAsync (свой кэш внутри);
        // по полу — тот же расчёт с Gender-фильтром. Значения в Results.Gender —
        // "male"/"female" (плюс "none"), НЕ "M"/"F" (это формат Swimmer.Gender).
        var topClubs = (await GetClubSummaryAsync(filter)).Take(10).ToList();

        // Правила, по которым посчитан этот зачёт: разрешаем ровно так же, как GetClubSummaryAsync
        // (привязка соревнования важнее подбора по дате), и схлопываем в уникальные.
        var ruleKeys = await query
            .Select(r => new { r.Competition.PointRuleClubsId, r.Competition.IsMasters, r.CompetitionDate })
            .Distinct()
            .ToListAsync();

        var allClubRules = await _db.PointRulesClubs.AsNoTracking().Include(r => r.Entries).ToListAsync();

        // Итог ручной проверки показываем, только если он одинаков у всех соревнований выборки:
        // «сверено» на одном дне и «расхождение» на другом в один бейдж не складываются.
        var verifiedKinds = await query
            .Select(r => r.Competition.ClubPointsVerifiedKind)
            .Distinct()
            .ToListAsync();

        var clubPointsVerified = verifiedKinds.Count == 1 ? verifiedKinds[0] : null;

        // Объяснение расхождения — только к бейджу расхождения и только если оно одно на всю
        // выборку: два разных объяснения в одном попапе противоречили бы друг другу.
        // У многодневки примечание записано каждому дню одинаковым, поэтому сравниваем
        // содержимое, а не число строк.
        CompetitionNoteDto? clubPointsMismatchNote = null;
        if (clubPointsVerified == PointsVerifiedKinds.Mismatch)
        {
            var competitionIds = await query.Select(r => r.CompetitionId).Distinct().ToListAsync();
            var notes = await _db.CompetitionNotes.AsNoTracking()
                .Include(n => n.Texts)
                .Where(n => competitionIds.Contains(n.CompetitionId)
                         && n.Kind == CompetitionNoteKinds.ClubPointsMismatch)
                .ToListAsync();

            var dtos = notes.Select(PointRulesAdminRepository.ToDto).ToList();
            if (dtos.Count > 0 && dtos.All(d => SameNote(d, dtos[0])))
                clubPointsMismatchNote = dtos[0];
        }

        var appliedRules = ruleKeys
            .Select(k => CompetitionRuleResolver.Resolve(
                allClubRules, k.PointRuleClubsId, k.IsMasters, DateOnly.FromDateTime(k.CompetitionDate)))
            .OfType<PointRuleClubs>()
            .DistinctBy(r => r.Id)
            .OrderBy(r => r.Version, StringComparer.Ordinal)
            .Select(r => new OverviewPointsRuleDto
            {
                Version = r.Version,
                Description = r.Description,
                Scope = r.Scope,
                EffectiveFrom = r.EffectiveFrom.ToString("yyyy-MM-dd"),
                DefaultPoints = r.DefaultPoints,
                MaxScoringPlace = r.MaxScoringPlace,
                RelayMultiplier = r.RelayMultiplier,
                PointsByPlace = r.Entries
                    .OrderBy(e => e.Place)
                    .Select(e => new OverviewPointsRuleEntryDto { Place = e.Place, Points = e.Points })
                    .ToList()
            })
            .ToList();
        var topClubsMen = (await GetClubSummaryAsync(CloneWithGender(filter, "male"))).Take(3).ToList();
        var topClubsWomen = (await GetClubSummaryAsync(CloneWithGender(filter, "female"))).Take(3).ToList();

        // Новые рекорды: сравнение личных заплывов с Records (country/ISR) — серверный
        // аналог клиентского isRecordTime. Records ~1.7К строк, кэш 10 мин; кандидаты —
        // минимальная проекция уже отфильтрованного источника.
        var records = await GetIsraelRecordsAsync();
        // SuspectReason != null — строка помечена как недостоверная (ошибка источника,
        // напр. 00:32.59 на 100 м баттерфляем в протоколе Маккабиады). Такие в рекорды
        // не берём: иначе они «бьют» национальный рекорд. Из результатов соревнования
        // при этом НЕ убираются — заплыв был, время в протоколе есть.
        var candidateRows = await query
            .Where(r => r.RelayId == null && !r.TimeFail && r.TimeMillisecond != null
                        && r.SuspectReason == null)
            .Select(r => new RecordCandidateRow(
                r.Id, r.SwimmerId, r.Swimmer.FirstName, r.Swimmer.LastName, r.Club.Name,
                r.Style.Name, r.Distance, r.Gender, r.Competition.PoolType,
                r.Swimmer.BirthYear, r.CompetitionDate, r.TimeMillisecond!.Value,
                r.TimeOriginal, r.Competition.DayNumber, r.Competition.IsMasters, r.AgeGroup))
            .ToListAsync();
        // Ось возраста для сверки со справочником — глобальная настройка RecordAgeAxis
        // (дефолт calendar = как ведёт справочник федерация), см. docs/data-integrity.md §13.
        var newRecords = CompetitionRecordsDetector.Detect(
            records, candidateRows, RecordAgeAxisSetting.From(_settings));

        var overview = new CompetitionOverviewDto
        {
            OrgCompId = orgCompId,
            Summary = new OverviewSummaryDto
            {
                ResultCount = resultCount,
                DayCount = days.Count,
                SwimmerCount = swimmerCount,
                ClubCount = clubCount
            },
            HasAwards = hasAwards,
            Days = days,
            BestSwim = bestSwim,
            BestSwimMale = bestSwimMale,
            BestSwimFemale = bestSwimFemale,
            TopClubs = topClubs,
            ClubPointsRules = appliedRules,
            ClubPointsVerified = clubPointsVerified,
            ClubPointsMismatchNote = clubPointsMismatchNote,
            TopClubsMen = topClubsMen,
            TopClubsWomen = topClubsWomen,
            TopMedalists = topMedalists,
            TopMedalistsMale = topMedalistsMale,
            TopMedalistsFemale = topMedalistsFemale,
            HighPointAwards = highPointAwards,
            Records = newRecords
        };

        await _cache.SetAsync(key, overview, ResultsTtl);
        return overview;
    }

    /// <summary>Рекорды Израиля (country/ISR, все категории) для детекции — кэш 10 мин.</summary>
    private async Task<IReadOnlyList<Domain.Entities.Record>> GetIsraelRecordsAsync()
    {
        const string key = "records:country:ISR:all";
        var cached = await _cache.GetAsync<IReadOnlyList<Domain.Entities.Record>>(key);
        if (cached is not null)
            return cached;

        var records = await _db.Records.AsNoTracking()
            .Where(r => r.RegionType == "country" && r.RegionCode == "ISR")
            .ToListAsync();
        await _cache.SetAsync<IReadOnlyList<Domain.Entities.Record>>(key, records, StaticHintsTtl);
        return records;
    }

    /// <summary>
    /// Место, по которому идёт зачёт. Объединённое место дисциплины по всему событию берётся,
    /// когда включён тоггл «Combine All Results» И соревнование это поддерживает; иначе —
    /// протокольное. Overview обязан следовать за тогглом, иначе его цифры расходятся с
    /// таблицей результатов на том же экране.
    /// Фоллбек на протокольное, если объединённое ещё не рассчитано: приблизительный зачёт
    /// лучше внезапно обнулившегося (пересчёт — dotnet run -- --recalc-combined).
    /// </summary>
    private static int? EffectivePlace(bool showCombineAllResults, int? position, int? combinedPlace)
        => showCombineAllResults ? combinedPlace ?? position : position;

    /// <summary>Копия фильтра-источника с Gender — для клубного зачёта по полу.
    /// Копируются только поля источника (как в /api/club-summary).</summary>
    private static ResultFilter CloneWithGender(ResultFilter f, string gender) => new()
    {
        CompetitionId = f.CompetitionId,
        Latest = f.Latest,
        EventId = f.EventId,
        Country = f.Country,
        PoolType = f.PoolType,
        DateFrom = f.DateFrom,
        DateTo = f.DateTo,
        Gender = gender,
        // Без этого зачёт по полу игнорировал бы тоггл и расходился с общим.
        Combined = f.Combined
    };

    /// <summary>Дата дня dd/MM/yyyy → DateTime для сортировки; непарсимая → MaxValue (в конец).</summary>
    private static DateTime ParseDayDate(string date)
        => DateTime.TryParseExact(date, "dd/MM/yyyy", CultureInfo.InvariantCulture,
               DateTimeStyles.None, out var d) ? d : DateTime.MaxValue;

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
                .Where(c => c.MergedIntoId == null)   // склеенные в подсказки фильтра не идут
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
        $":{(f.SwimmerIds is { Count: > 0 } ids ? string.Join(",", ids.OrderBy(x => x)) : "")}" +
        // Combined меняет сами цифры агрегатов — без него включённый и выключенный тоггл
        // делили бы один кэш и отдавали одинаковый ответ.
        $":{f.Combined}";

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
                // Чемпионат — если помечен хотя бы один день события.
                IsChampionship = _db.Competitions.Any(c => c.EventId == e.Id && c.IsChampionship),
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
                c.IsChampionship,
                c.ShowCombineAllResults,
                Country = c.Country != null ? c.Country.CountryCode : "",
                ResultCount = _db.Results.Count(r => r.CompetitionId == c.Id)
            })
            .ToListAsync();

        // Категория для селектора — из РЕАЛЬНОГО членства (CategoryCompetitions, те же
        // чекбоксы, что в админке), а не эвристики по возрасту: соревнование может быть
        // в нескольких категориях сразу, поэтому приоритет от младшей ступени к старшей:
        // masters > kids-team (Kids) > youth-team (Young) > junior-results (Juniors) >
        // main (Adults/בוגרים).
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
        // Ключи табов соответствуют ступеням (2026-07-31): kids8_11 = «Kids» (8–11),
        // young11_14 = «Young» (11–14), juniors = «Juniors» (נוער), adults = «Adults» (בוגרים).
        // Старые ключи (young8_11, junior) уводятся алиасами в results-categories.ts.
        static string? CategoryFor(bool isMasters, HashSet<string>? keys) =>
            isMasters || keys?.Contains("results-masters") == true ? "masters"
            : keys?.Contains("results-kids-team") == true ? "kids8_11"
            : keys?.Contains("results-youth-team") == true ? "young11_14"
            : keys?.Contains("results-junior-results") == true ? "juniors"
            : keys?.Contains("results-main") == true ? "adults"
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
                IsChampionship = e.IsChampionship,
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
                IsChampionship = c.IsChampionship,
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
        var rows = await ProjectCareerRows(_db.Results.AsNoTracking()
            .Where(r => r.RelayId == null && (
                r.Swimmer.FirstName + " " + r.Swimmer.LastName == name ||
                r.Swimmer.LastName + " " + r.Swimmer.FirstName == name ||
                r.Swimmer.FirstNameEn + " " + r.Swimmer.LastNameEn == name ||
                r.Swimmer.LastNameEn + " " + r.Swimmer.FirstNameEn == name)));

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
                    Position = r.HeatType == "prelim" || r.HeatType == "extra" ? null : r.Position,
                    r.Relay!.SwimmersName,
                    StyleName = r.Style.Name,
                    r.Distance,
                    CompetitionName = r.Competition.Name,
                    DateRaw = r.Competition.Date,
                    IsAward = r.Competition.IsAward
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
            .Select(r => new CareerRelayRow(
                r.CompetitionId, r.EventId, r.Position, r.StyleName, r.Distance,
                r.CompetitionName, r.DateRaw, r.IsAward))
            .ToList();

        if (rows.Count == 0 && relayMedals.Count == 0) return null;

        return await BuildCareerAsync(key, rows, relayMedals);
    }

    /// <summary>
    /// Карьера по ИДЕНТИЧНОСТИ пловца (этап A1 страницы спортсмена). Именной вариант выше
    /// оставлен алиасом для попапа-карточки, но правда здесь: имена не уникальны и совпадают
    /// у разных людей, а страница адресуется по id.
    ///
    /// Эстафеты берутся из <c>RelayMembers</c> (структурная связь), а не разбором строки
    /// <c>Relay.SwimmersName</c>: в именном варианте это единственный доступный способ, здесь —
    /// лишний источник ошибок (обрезанные фамилии, тёзки, см. docs/relays.md).
    /// </summary>
    public async Task<AthleteCareerDto?> GetAthleteCareerByIdAsync(int swimmerId)
    {
        if (swimmerId <= 0) return null;

        var key = $"athlete-career-id:{swimmerId}";
        var cached = await _cache.GetAsync<AthleteCareerDto>(key);
        if (cached is not null) return cached;

        var rows = await ProjectCareerRows(_db.Results.AsNoTracking()
            .Where(r => r.SwimmerId == swimmerId && r.RelayId == null));

        var relayIds = await _db.RelayMembers.AsNoTracking()
            .Where(m => m.SwimmerId == swimmerId)
            .Select(m => m.RelayId)
            .Distinct()
            .ToListAsync();

        // Строка эстафеты, привязанная к самому пловцу как к «первой ноге», может не иметь
        // записи в RelayMembers у старых импортов — берём и её, дальше дедуп по CompetitionId.
        var relayMedals = (await _db.Results.AsNoTracking()
            .Where(r => r.RelayId != null
                        && (relayIds.Contains(r.RelayId!.Value) || r.SwimmerId == swimmerId))
            .Select(r => new
            {
                r.Id,
                r.CompetitionId,
                EventId = (int?)r.Competition.EventId,
                Position = r.HeatType == "prelim" || r.HeatType == "extra" ? null : r.Position,
                StyleName = r.Style.Name,
                r.Distance,
                CompetitionName = r.Competition.Name,
                DateRaw = r.Competition.Date,
                IsAward = r.Competition.IsAward
            })
            .ToListAsync())
            .GroupBy(r => r.Id)
            .Select(g => g.First())
            .Select(r => new CareerRelayRow(
                r.CompetitionId, r.EventId, r.Position, r.StyleName, r.Distance,
                r.CompetitionName, r.DateRaw, r.IsAward))
            .ToList();

        if (rows.Count == 0 && relayMedals.Count == 0) return null;

        return await BuildCareerAsync(key, rows, relayMedals);
    }

    /// <summary>Личный заплыв карьеры — общая форма для именного и id-пути.</summary>
    private sealed record CareerSwimRow(
        int CompetitionId, int? EventId, DateTime CompetitionDate, int? Position,
        int InternationalPoints, int? TimeMillisecond, string TimeOriginal, bool TimeFail,
        string? SuspectReason, string StyleName, string Distance, string Pool,
        string CompetitionName, string DateRaw, string Gender, string EventStyleAge,
        string AgeGroup, bool IsMasters, bool IsAward, string? HeatType = null,
        string? Round = null);

    /// <summary>Эстафета карьеры: из неё берутся только медали и факт участия в соревновании.</summary>
    private sealed record CareerRelayRow(
        int CompetitionId, int? EventId, int? Position, string StyleName, string Distance,
        string CompetitionName, string DateRaw, bool IsAward);

    private static async Task<List<CareerSwimRow>> ProjectCareerRows(IQueryable<ResultRecord> q) =>
        await q.Select(r => new CareerSwimRow(
                r.CompetitionId,
                r.Competition.EventId,
                r.CompetitionDate,
                r.Position,
                r.InternationalPoints,
                r.TimeMillisecond,
                r.TimeOriginal,
                r.TimeFail,
                r.SuspectReason,
                r.Style.Name,
                r.Distance,
                r.Competition.PoolType,
                r.Competition.Name,
                r.Competition.Date,
                r.Gender,
                r.EventStyleAge,
                r.AgeGroup,
                r.Competition.IsMasters,
                r.Competition.IsAward,
                r.HeatType,
                r.Round))
            .ToListAsync();

    /// <summary>
    /// Сборка карьерного DTO — одна на оба пути поиска. Разъехаться этим двум расчётам нельзя:
    /// карточка-попап и страница показывали бы разные цифры про одного человека.
    /// </summary>
    private async Task<AthleteCareerDto> BuildCareerAsync(
        string cacheKey, List<CareerSwimRow> rows, List<CareerRelayRow> relayMedals)
    {
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
                SuspectReason = r.SuspectReason,
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

        // ⚠ Медаль засчитывается ТОЛЬКО там, где её вручали (Competition.IsAward). На лигах
        // мест призёров нет, и таблица результатов там медаль не рисует — карточка не должна
        // считать иначе (найдено 2026-08-13 при сверке страницы спортсмена: первое место на
        // «ליגה רבתי 3» шло в золото карьеры, хотя медали на этом старте не вручались).
        // Prelim-заплывы в медали не идут: их место — ранжир сессии (у эстафет prelim-места
        // обнулены ещё в проекции).
        // Медаль возрастной ступени одна, даже когда ступень разыграна дважды за день
        // (утренний зачёт возрастов + вечерний финал первенства) — см. RoundMedalCollapser.
        var awardedRows = RoundMedalCollapser.Collapse(
            rows.Where(r => r.IsAward && HeatTypes.GivesOfficialPlace(r.HeatType)),
            r => $"{r.CompetitionId}|{r.StyleName}|{r.Distance}|{r.EventStyleAge}",
            r => r.Round,
            r => r.Position);
        var awardedRelays = relayMedals.Where(r => r.IsAward).ToList();

        // Разбивка медалей по конкретным заплывам — для тултипа "за что" на карточке.
        var medals = awardedRows
            .Where(r => r.Position is 1 or 2 or 3)
            .Select(r => new MedalDetailDto
            {
                Position = r.Position!.Value,
                Note = $"{r.StyleName} {r.Distance}м",
                Competition = r.CompetitionName,
                Date = r.DateRaw
            })
            .Concat(awardedRelays
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
            Gold = awardedRows.Count(r => r.Position == 1) + awardedRelays.Count(r => r.Position == 1),
            Silver = awardedRows.Count(r => r.Position == 2) + awardedRelays.Count(r => r.Position == 2),
            Bronze = awardedRows.Count(r => r.Position == 3) + awardedRelays.Count(r => r.Position == 3),
            BestByStyle = bestByStyle,
            Medals = medals
        };

        await _cache.SetAsync(cacheKey, dto, TimeSpan.FromMinutes(5));
        return dto;
    }

    public async Task<SwimmerProfileDto?> GetSwimmerProfileAsync(int id)
    {
        if (id <= 0) return null;

        var key = $"swimmer-profile:{id}";
        var cached = await _cache.GetAsync<SwimmerProfileDto>(key);
        if (cached is not null) return cached;

        var s = await _db.Swimmers.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.FirstName, x.LastName,
                x.FirstNameEn, x.LastNameEn,
                x.BirthYear, x.Gender, x.Origin, x.AvatarUrl,
                x.ClubId,
                ClubName = x.Club != null ? x.Club.Name : null,
                CountryCode = x.Country != null ? x.Country.CountryCode : null,
                CountryName = x.Country != null ? x.Country.CountryName : null,
            })
            .FirstOrDefaultAsync();
        if (s is null) return null;

        // FullName для карьерного запроса и заголовка: приоритет RU, иначе EN.
        var ru = $"{s.FirstName} {s.LastName}".Trim();
        var en = $"{s.FirstNameEn} {s.LastNameEn}".Trim();
        var fullName = ru.Length > 0 ? ru : en;

        var dto = new SwimmerProfileDto
        {
            Id = s.Id,
            FullName = fullName,
            FirstName = s.FirstName,
            LastName = s.LastName,
            FirstNameEn = s.FirstNameEn,
            LastNameEn = s.LastNameEn,
            BirthYear = s.BirthYear,
            Gender = s.Gender,
            ClubId = s.ClubId,
            ClubName = s.ClubName,
            CountryCode = s.CountryCode,
            CountryName = s.CountryName,
            AvatarUrl = s.AvatarUrl,
            Origin = s.Origin,
        };

        await _cache.SetAsync(key, dto, TimeSpan.FromMinutes(5));
        return dto;
    }

    /// <summary>
    /// Э6: проставляет клубные очки строкам страницы. Считает СЕРВЕР, а не клиент — клиент не
    /// видит привязку соревнования к правилу и подбирал его по дате, из-за чего на manual-правиле
    /// расходился с зачётом (docs/competition-overview-cards.md, раздел Top clubs).
    ///
    /// Правил единицы — грузим целиком и применяем в памяти, как в GetClubSummaryAsync;
    /// шкала правила это JOIN на Entries, тянуть его в SQL-проекцию каждой строки незачем.
    /// Даты берём из DTO (строка dd/MM/yyyy) — того же источника, что видит клиент.
    /// </summary>
    private async Task ApplyClubPointsAsync(List<ResultDto> items)
    {
        if (items.Count == 0) return;

        var rules = await _db.PointRulesClubs.AsNoTracking()
            .Include(r => r.Entries)
            .ToListAsync();

        foreach (var item in items)
        {
            var date = ParseCompetitionDate(item.Date);
            var rule = CompetitionRuleResolver.Resolve(rules, item.PointRuleClubsId, item.IsMasters, date);

            // Место prelim-заплыва очков не приносит (ранжир сессии, не награда); само
            // Position в DTO остаётся — клиент показывает место в заплыве как в протоколе.
            item.ClubPoints = PointRulesClubsScoring.RelayPointsFor(
                rule, HeatTypes.GivesOfficialPlace(item.HeatType) ? item.Position : null,
                item.TimeFail, item.IsRelay);

            // Объединённые очки есть только там, где есть объединённое место: тоггл на клиенте
            // переключает поле, а не запускает пересчёт.
            item.CombinedClubPoints = item.CombinedPlace is null
                ? null
                : PointRulesClubsScoring.RelayPointsFor(
                    rule, item.CombinedPlace, item.TimeFail, item.IsRelay);
        }
    }

    /// <summary>Дата соревнования из DTO (dd/MM/yyyy). Неразобранная — сегодня: правило по дате
    /// не подберётся «из прошлого», а привязка по Id всё равно важнее.</summary>
    /// <summary>Одинаковы ли примечания дней события: сравниваем то, что увидит читатель.</summary>
    private static bool SameNote(CompetitionNoteDto a, CompetitionNoteDto b) =>
        a.Texts.Count == b.Texts.Count
        && a.Texts.All(t => b.Texts.TryGetValue(t.Key, out var other) && other == t.Value)
        && a.ScaleDiffCaption == b.ScaleDiffCaption
        && a.ScaleDiff.Count == b.ScaleDiff.Count
        && a.ScaleDiff.Zip(b.ScaleDiff).All(p => p.First == p.Second);

    private static DateOnly ParseCompetitionDate(string? date) =>
        DateOnly.TryParseExact(date, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>
    /// Одна медаль в зачёте «Most decorated»: личная или эстафетная (у эстафетной строка
    /// развёрнута на каждую ногу через RelayMembers). <paramref name="Gender"/> — пол самого
    /// пловца, а не строки результата: эстафеты бывают смешанные.
    /// </summary>
    /// <param name="MedalKey">
    /// Единица награждения — пловец + дисциплина + возрастная ступень. Нужна, чтобы схлопнуть
    /// медали, задвоенные раундами чемпионата «мокдамот и финал» (<see cref="RoundMedalCollapser"/>).
    /// </param>
    private sealed record MedalRow(
        int SwimmerId,
        string FirstName,
        string LastName,
        string FirstNameEn,
        string LastNameEn,
        string Club,
        string? Gender,
        int? Position,
        bool IsRelay,
        string MedalKey = "",
        string? Round = null);
}
