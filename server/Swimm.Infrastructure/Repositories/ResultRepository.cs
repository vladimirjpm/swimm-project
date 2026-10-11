using System.Globalization;
using Swimm.Domain;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;

namespace Swimm.Infrastructure.Repositories;

public partial class ResultRepository : IResultRepository
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
        // GetOrCreate: страницу зовут и ВНЕ сборки CachedJson (/api/results), и только своя
        // сборка даёт ей метки её таблиц (К3, docs/plans/cache-tags-plan.md).
        var cached = await _cache.GetOrCreateAsync(ResultsCacheKey(filter, page, pageSize),
            () => LoadPageAsync(filter, page, pageSize), ResultsTtl);
        return (cached.Items, cached.HasMore, cached.Total);
    }

    private async Task<CachedResultsPage> LoadPageAsync(ResultFilter filter, int page, int pageSize)
    {
        var query = await BuildFilteredQueryAsync(filter);
        if (query is null)
            return new CachedResultsPage([], false, 0);

        // Total — отдельный кэш-ключ БЕЗ page/pageSize: листание страниц не пересчитывает COUNT.
        // Вложенная запись: её метки наследует и страница.
        var total = (await _cache.GetOrCreateAsync($"results-total:{FilterCacheKey(filter)}",
            async () => new CachedCount(await query.CountAsync()), ResultsTtl)).Value;

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

        return new CachedResultsPage(items, hasMore, total);
    }

    /// <summary>
    /// Страница результатов в кэше. Record, а не кортеж: у <c>ValueTuple</c> только поля, и
    /// System.Text.Json пишет его как <c>{}</c> — в Redis страница молча стала бы пустой
    /// (docs/plans/cache-tags-plan.md §4-2).
    /// </summary>
    private sealed record CachedResultsPage(List<ResultDto> Items, bool HasMore, int Total);

    /// <summary>COUNT фильтра в кэше. Record, а не <c>int?</c>: GetOrCreate работает со ссылочными значениями.</summary>
    private sealed record CachedCount(int Value);

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
        var map = await _cache.GetOrCreateAsync("styles:name-to-ids", async () =>
            (await _db.Styles.AsNoTracking().Select(s => new { s.Name, s.Id }).ToListAsync())
                .GroupBy(s => s.Name)
                .ToDictionary(g => g.Key, g => g.Select(s => s.Id).ToArray()),
            TimeSpan.FromMinutes(10));
        return map.TryGetValue(styleName, out var ids) ? ids : [];
    }

    /// <summary>EventId → его CompetitionId(ы) из кэша (TTL 10 мин). Пусто — событие без
    /// соревнований или несуществующий id. Резолвим в Id, чтобы фильтр по Results шёл по
    /// композитному индексу (CompetitionId,...), а не через JOIN на Competition.EventId —
    /// последнее на большом объёме заставляет планировщик сканировать всю таблицу.</summary>
    private async Task<int[]> ResolveEventCompetitionIdsAsync(int eventId)
    {
        return await _cache.GetOrCreateAsync($"event-competitions:{eventId}", () =>
            _db.Competitions.AsNoTracking()
                .Where(c => c.EventId == eventId)
                .Select(c => c.Id)
                .ToArrayAsync(),
            TimeSpan.FromMinutes(10));
    }

    // GetOrCreate: запись собирается в своём контексте и получает метки своих таблиц сама
    // (К3, docs/plans/cache-tags-plan.md). Обзор соревнования зовёт её же — метки наследует.
    public Task<IReadOnlyList<ClubSummaryDto>> GetClubSummaryAsync(ResultFilter filter) =>
        _cache.GetOrCreateAsync($"club-summary:{FilterCacheKey(filter)}",
            () => LoadClubSummaryAsync(filter), ResultsTtl);

    private async Task<IReadOnlyList<ClubSummaryDto>> LoadClubSummaryAsync(ResultFilter filter)
    {
        var query = await BuildFilteredQueryAsync(filter);
        if (query is null)
            return [];

        // Минимальная проекция для агрегации в памяти (клубов и заплывов в одном источнике мало).
        // Псевдоклубы (страна/сборная вместо клуба, Maccabiah) в клубный зачёт не входят.
        var rows = await query
            .Where(r => !r.Club.IsPseudo)
            .Select(r => new
            {
                r.Id,
                r.CompetitionId,
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

        // Правило «по заплыву целиком» (схема Красного моря): очки зависят от всех участников
        // заплыва, а не от отфильтрованной выборки, — их считает ContextClubPoints по
        // соревнованию целиком. Объединённых мест у такого правила нет: зачёт по протоколу.
        var contextPoints = await ContextClubPoints.LoadAsync(_db.Results, rules,
            ContextClubPoints.CompetitionsNeedingContext(rules, rows
                .Select(r => (r.CompetitionId, r.RuleId, r.IsMasters, DateOnly.FromDateTime(r.CompetitionDate)))
                .Distinct()));

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
            var contextual = PointRulesClubsScoring.NeedsContext(rule);
            var place = contextual ? r.Position : EffectivePlace(filter.Combined && r.ShowCombine, r.Position, r.CombinedPlace);
            var points = contextual
                ? contextPoints.GetValueOrDefault(r.Id)
                : PointRulesClubsScoring.RelayPointsFor(rule, place, r.TimeFail, r.IsRelay);

            scoring.Add(new ClubScoringRow(
                ClubId: r.ClubId, ClubKey: club, SwimmerId: r.SwimmerId,
                Place: place, IsRelay: r.IsRelay, Points: points));
        }

        var summary = ClubStandingCalculator.Build(scoring)
            .Select(c => new ClubSummaryDto
            {
                Club = c.ClubKey,
                ClubId = c.ClubId,
                Points = c.Points,
                SwimmerCount = c.SwimmerCount,
                SuccessfulCount = c.ScoringSwims,
                Gold = c.Gold,
                Silver = c.Silver,
                Bronze = c.Bronze
            })
            .ToList();

        return summary;
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

        var ttl = field is "style" or "distance" ? StaticHintsTtl : DynamicHintsTtl;

        // GetOrCreate: метки своих таблиц запись получает сама (К3). Пустую выдачу не кэшируем,
        // как и раньше, — фабрика отдаёт null, и в кэш он не ложится.
        return await _cache.GetOrCreateAsync<string[]?>(key, LoadAsync, ttl) ?? [];

        async Task<string[]?> LoadAsync()
        {
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

            return hints.Length > 0 ? hints : null;
        }
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

    public Task<IReadOnlyList<CompetitionSourceDto>> GetSourcesAsync(string? country = null)
    {
        // Нормализация как в GetStandardsAsync (RecordRepository): trim + upper, пусто — без фильтра.
        var countryKey = string.IsNullOrWhiteSpace(country) ? null : country.Trim().ToUpperInvariant();
        // GetOrCreate: метки своих таблиц запись получает сама (К3).
        return _cache.GetOrCreateAsync($"competition-sources:{countryKey ?? "all"}",
            () => LoadSourcesAsync(countryKey), TimeSpan.FromMinutes(5));
    }

    private async Task<IReadOnlyList<CompetitionSourceDto>> LoadSourcesAsync(string? countryKey)
    {
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
        // Лестница приоритетов вынесена в общее место (CompetitionCategories.Canonical):
        // тем же ключом плитку соревнования собирает My media.
        static string? CategoryFor(bool isMasters, HashSet<string>? keys) =>
            CompetitionCategories.Canonical(isMasters, keys);

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

        return ordered;
    }

    public async Task<AthleteCareerDto?> GetAthleteCareerAsync(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return null;

        var key = $"athlete-career:{name.ToLowerInvariant()}";
        // GetOrCreate: запись собирается в своём контексте и получает метки своих таблиц
        // сама (К3). «Не найдено» (null) в кэш не ложится — как и раньше.
        return await _cache.GetOrCreateAsync<AthleteCareerDto?>(key, LoadAsync, TimeSpan.FromMinutes(5));

        async Task<AthleteCareerDto?> LoadAsync()
        {
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

            return await BuildCareerAsync(rows, relayMedals);
        }
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
        // GetOrCreate: запись собирается в своём контексте и получает метки своих таблиц
        // сама (К3). «Не найдено» (null) в кэш не ложится — как и раньше.
        return await _cache.GetOrCreateAsync<AthleteCareerDto?>(key, LoadAsync, TimeSpan.FromMinutes(5));

        async Task<AthleteCareerDto?> LoadAsync()
        {
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

            return await BuildCareerAsync(rows, relayMedals);
        }
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
                r.IsParaPoints ? 0 : r.InternationalPoints,  // пара-очки — не FINA (Р67)
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
        List<CareerSwimRow> rows, List<CareerRelayRow> relayMedals)
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

        return dto;
    }

    public async Task<SwimmerProfileDto?> GetSwimmerProfileAsync(int id)
    {
        if (id <= 0) return null;

        var key = $"swimmer-profile:{id}";
        // GetOrCreate: запись собирается в своём контексте и получает метки своих таблиц
        // сама (К3, docs/plans/cache-tags-plan.md) — от кого бы её ни позвали.
        return await _cache.GetOrCreateAsync(key, LoadAsync, TimeSpan.FromMinutes(5));

        async Task<SwimmerProfileDto?> LoadAsync()
        {
            // Пловец группы (Р71) публичного профиля не имеет: для страницы пловца, сравнения и
            // всех публичных вкладок его нет — 404, как у несуществующего id.
            var s = await _db.Swimmers.AsNoTracking()
                .Where(x => x.Id == id && x.PrivateHubGroupId == null)
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

            return dto;
        }
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

        // Правило «по заплыву целиком» (схема Красного моря): на странице протокола видна лишь
        // часть заплыва, поэтому очки таких соревнований считаются по ним целиком.
        // Id соревнования в DTO нет — берём по строкам, и только когда такое правило вообще есть.
        IReadOnlyDictionary<long, int> contextPoints = new Dictionary<long, int>();
        if (rules.Any(PointRulesClubsScoring.NeedsContext))
        {
            var ids = items.Select(i => i.Id).ToList();
            var competitions = await _db.Results.AsNoTracking()
                .Where(r => ids.Contains(r.Id))
                .Select(r => new { r.CompetitionId, r.Competition.PointRuleClubsId, r.Competition.IsMasters, r.CompetitionDate })
                .Distinct()
                .ToListAsync();
            contextPoints = await ContextClubPoints.LoadAsync(_db.Results, rules,
                ContextClubPoints.CompetitionsNeedingContext(rules, competitions
                    .Select(c => (c.CompetitionId, c.PointRuleClubsId, c.IsMasters, DateOnly.FromDateTime(c.CompetitionDate)))));
        }

        foreach (var item in items)
        {
            var date = ParseCompetitionDate(item.Date);
            var rule = CompetitionRuleResolver.Resolve(rules, item.PointRuleClubsId, item.IsMasters, date);

            if (PointRulesClubsScoring.NeedsContext(rule))
            {
                item.ClubPoints = contextPoints.GetValueOrDefault(item.Id);
                item.CombinedClubPoints = item.CombinedPlace is null ? null : item.ClubPoints;
                continue;
            }

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
    private static DateOnly ParseCompetitionDate(string? date) =>
        DateOnly.TryParseExact(date, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : DateOnly.FromDateTime(DateTime.UtcNow);
}
