using Microsoft.AspNetCore.Mvc;
using Swimm.API.Http;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain;

namespace Swimm.API.Controllers;

/// <summary>
/// Публичные данные страницы спортсмена (docs/plans/athlete-page-plan.md, этапы A2–A4).
/// По эндпоинту на блок-группу, чтобы табы грузились лениво; все они считают своё из ОДНОГО
/// набора заплывов (<see cref="ISwimmerPageRepository.GetSwimsAsync"/>) через
/// <see cref="SwimmerPageBuilder"/> — иначе цифры табов разъедутся.
///
/// Профиль по id (<c>GET /api/swimmers/{id}</c>) остаётся в ResultsController: он старше
/// страницы и его использует попап-карточка.
///
/// Кэш — как у страницы клуба: ETag + Cache-Control, инвалидация общая
/// (ICacheService.InvalidateAllAsync после импорта и админ-мутаций).
/// </summary>
[ApiController]
public class SwimmersPublicController : ControllerBase
{
    private readonly ISwimmerPageRepository _swims;
    private readonly IResultRepository _results;
    private readonly ICacheService _cache;

    /// <summary>
    /// Витринный сезон — общий шов продукта (docs/season-boundary-rule.md). Раньше страница
    /// считала его сама из дат зимних чемпионатов; правило от этого жило в двух местах и
    /// разъехалось с карточками клуба и /season-best.
    /// </summary>
    private readonly IShowcaseSeasonProvider _showcase;

    /// <summary>
    /// Ось возраста справочника рекордов (`RecordAgeAxis` из /Admin/Settings): по ней бейдж
    /// «REC» находит свою ступень. Читается ТЕМ ЖЕ хелпером, что детектор рекордов
    /// соревнования, — вторая копия разбора разъехалась бы с первой.
    /// </summary>
    private readonly ISettingsService _settings;

    private const string CacheControlValue = "public, max-age=60";
    private static readonly TimeSpan PayloadTtl = TimeSpan.FromMinutes(5);

    /// <summary>Значение <c>?season=</c>, означающее «за карьеру».</summary>
    private const string AllSeasons = "all";

    public SwimmersPublicController(
        ISwimmerPageRepository swims, IResultRepository results, ICacheService cache,
        IShowcaseSeasonProvider showcase, ISettingsService settings)
    {
        _swims = swims;
        _results = results;
        _cache = cache;
        _showcase = showcase;
        _settings = settings;
    }

    /// <summary>
    /// Профиль спортсмена: шапка страницы плюс список сезонов для карусели.
    /// Базовые поля те же, что раньше отдавал ResultsController (их читает попап-карточка),
    /// сверху — возраст в сезоне, зачётная группа, программы, рекорды и сезоны.
    /// 404 — пловца нет.
    /// </summary>
    [HttpGet("/api/swimmers/{id:int}")]
    public async Task<IActionResult> GetProfile(int id)
    {
        // Резолв ДО кэшируемой загрузки: 404 не должен плодить кэш-записи.
        if (await _results.GetSwimmerProfileAsync(id) is null) return NotFound();

        return await this.CachedJson(_cache, $"http:swimmer:{id}:profile",
            () => BuildProfileAsync(id), PayloadTtl, CacheControlValue);
    }

    private async Task<SwimmerProfileDto> BuildProfileAsync(int id)
    {
        var dto = (await _results.GetSwimmerProfileAsync(id))!;
        var rows = await _swims.GetSwimsAsync(id);

        var showcase = await _showcase.CurrentStartYearAsync();

        dto.Seasons = SwimmerPageBuilder.Seasons(rows, showcase, SeasonMath.CurrentStartYear());

        // Возраст показываем для сезона, на котором стоит карусель: подпись «12 year (2014)»
        // обязана совпадать с тем, что человек видит в панели.
        var displaySeason = dto.Seasons.FirstOrDefault(s => s.IsDisplayDefault)?.Season ?? showcase;
        dto.AgeInSeason = SeasonMath.AgeInSeason(displaySeason, dto.BirthYear);

        dto.AgeGroup = await _swims.GetLadderGroupAsync(rows.Select(r => r.CompetitionId));
        // Счётчик и список — из ОДНОГО запроса: разными они рано или поздно разошлись бы,
        // и бейдж «4 records» стоял бы над секцией с тремя строками.
        var held = await _swims.GetRecordsHeldAsync(id);
        dto.Records = held.Select(r => new SwimmerHeldRecordDto
        {
            RegionType = r.RegionType,
            RegionCode = r.RegionCode,
            Category = r.Category,
            AgeKey = r.AgeKey,
            Gender = r.Gender,
            PoolType = r.PoolType,
            Stroke = r.Style,
            // Справочник пишет «200m», протокол — «200». Клиент рисует дистанцию одной
            // плитой для обоих источников, поэтому «m» снимаем здесь — та же нормализация,
            // что в SeasonAggregator.DisciplineKey.
            Distance = r.Distance.TrimEnd('m', 'M'),
            Time = r.Time,
            Date = r.RecordDate,
            Quality = r.IssueReason is null
                ? null
                : new SwimQualityDto { Kind = "record", Reason = r.IssueReason },
        }).ToList();
        dto.RecordsHeld = dto.Records.Count;

        // Открытая вода появится, когда заведут Competition.WaterKind
        // (docs/plans/open-water-course-plan.md): третьего PoolType не будет.
        dto.Programs = rows.Count > 0 ? ["pool"] : [];

        return dto;
    }

    /// <summary>
    /// KPI-плитки, шапка панели и список стартов сезона. <c>?season=all</c> — за карьеру
    /// (этим же ответом живёт таб History).
    /// </summary>
    [HttpGet("/api/swimmers/{id:int}/summary")]
    public async Task<IActionResult> GetSummary(int id, [FromQuery] string? season = null)
        => await this.CachedJson(_cache,
            $"http:swimmer:{id}:summary:{season ?? "default"}",
            () => BuildSummaryAsync(id, season),
            PayloadTtl, CacheControlValue);

    private async Task<SwimmerSummaryDto> BuildSummaryAsync(int id, string? season)
    {
        var rows = await _swims.GetSwimsAsync(id);
        var selected = await ResolveSeasonAsync(rows, season);

        var kinds = await _swims.GetStandingKindsAsync(rows.Select(r => r.CompetitionId));
        return SwimmerPageBuilder.Summary(rows, selected, kinds);
    }

    /// <summary>
    /// Таб Results: одна дистанция — одна строка, лучшее время за сезон.
    /// <c>?season=all</c> — лучшее за карьеру.
    /// </summary>
    [HttpGet("/api/swimmers/{id:int}/best-times")]
    public async Task<IActionResult> GetBestTimes(int id, [FromQuery] string? season = null)
        => await this.CachedJson(_cache,
            $"http:swimmer:{id}:best-times:{season ?? "default"}",
            async () =>
            {
                var rows = await _swims.GetSwimsAsync(id);
                var profile = await _results.GetSwimmerProfileAsync(id);
                var selected = await ResolveSeasonAsync(rows, season);
                return SwimmerPageBuilder.BestTimes(rows, selected, profile?.BirthYear ?? 0);
            },
            PayloadTtl, CacheControlValue);

    /// <summary>
    /// Таб Records &amp; PB: личные рекорды карьеры в выбранном бассейне плюс дельты до
    /// лучшего времени клуба и до рекорда страны своего возраста.
    /// <paramref name="poolType"/> — «25m»/«50m»; без него оба бассейна.
    /// </summary>
    [HttpGet("/api/swimmers/{id:int}/personal-bests")]
    public async Task<IActionResult> GetPersonalBests(int id, [FromQuery] string? poolType = null)
        => await this.CachedJson(_cache,
            $"http:swimmer:{id}:personal-bests:{poolType ?? "any"}",
            () => BuildPersonalBestsAsync(id, poolType),
            PayloadTtl, CacheControlValue);

    private async Task<List<SwimmerPersonalBestDto>> BuildPersonalBestsAsync(int id, string? poolType)
    {
        var rows = await _swims.GetSwimsAsync(id);
        var profile = await _results.GetSwimmerProfileAsync(id);
        if (profile is null || rows.Count == 0) return [];

        // Возраст берём тот же, что подписан в шапке: обе дельты отвечают на вопрос «сколько
        // мне осталось до эталона МОЕЙ ступени», а не «до ступени того года, когда я плыл».
        var displaySeason = await DefaultSeasonAsync(rows) ?? SeasonMath.CurrentStartYear();
        var age = SeasonMath.AgeInSeason(displaySeason, profile.BirthYear);

        // ОБЕ дельты считаются по ОДНОЙ и той же ступени (решение Влада 2026-08-27):
        // раньше клубная брала минимум по всему клубу любого возраста, и две цифры рядом
        // меряли разное, выглядя одинаково. Нет года рождения — нет ни одной из них.
        var clubBest = profile.ClubId is int clubId && age is int clubAge
            ? await _swims.GetClubBestMsAsync(clubId, clubAge)
            : new Dictionary<string, int>();

        // Ступени справочника — ПО ЗАПЛЫВАМ, как в сравнении: мастерский старт меряется
        // полосой «45-49», обычный взрослый — открытым рекордом страны, ребёнок — своей
        // возрастной ступенью. Один запрос «age/{возраст}» оставлял всех взрослых без дельты.
        var axis = RecordAgeAxisSetting.From(_settings);
        var records = await RecordStepsAsync(rows, profile, axis);

        return SwimmerPageBuilder.PersonalBests(
            rows, poolType, clubBest, records, profile?.BirthYear ?? 0, axis);
    }

    /// <summary>
    /// Фильтр «Season best» таба Results: где пловец стоит среди сверстников (тот же год
    /// рождения, тот же пол) по лучшим временам сезона. Строки результатов НЕ дублируются —
    /// клиент склеивает ответ с <c>/best-times</c> того же сезона по <c>disciplineKey</c>.
    /// <c>?season=all</c> — мест нет: сравнение живёт внутри одного сезона.
    /// </summary>
    [HttpGet("/api/swimmers/{id:int}/season-ranks")]
    public async Task<IActionResult> GetSeasonRanks(int id, [FromQuery] string? season = null)
        => await this.CachedJson(_cache,
            $"http:swimmer:{id}:season-ranks:{season ?? "default"}",
            () => BuildSeasonRanksAsync(id, season),
            PayloadTtl, CacheControlValue);

    private async Task<SwimmerSeasonRankDto> BuildSeasonRanksAsync(int id, string? season)
    {
        var rows = await _swims.GetSwimsAsync(id);
        var profile = await _results.GetSwimmerProfileAsync(id);
        var selected = await ResolveSeasonAsync(rows, season);

        // Когорта тянется только под конкретный сезон: за карьеру сравнивать не с чем, а
        // выборка недешёвая (её ключ — сезон + год рождения, общий на всех сверстников).
        var cohort = selected is int year && profile is not null
            ? await _swims.GetAgeCohortSeasonBestsAsync(year, profile.BirthYear)
            : [];

        var dto = SwimmerPageBuilder.SeasonRanks(
            rows, selected, profile?.BirthYear ?? 0, profile?.Gender, cohort);

        // Пока новый сезон не открыт витриной, панель Season best обязана сказать это вслух:
        // у пловца в сентябре ещё нет заплывов, и пустая панель иначе читается как поломка.
        dto.SeasonNotice = await _showcase.PendingNoticeAsync();
        return dto;
    }

    /// <summary>
    /// Таб Progress: история всех заплывов одной дисциплины по возрастанию даты.
    /// <paramref name="disciplineKey"/> — ключ из <c>/best-times</c>: связка
    /// стиль × дистанция × бассейн × пол, а не «eventId» — событий-дисциплин в модели нет.
    /// </summary>
    [HttpGet("/api/swimmers/{id:int}/progress")]
    public async Task<IActionResult> GetProgress(int id, [FromQuery] string? disciplineKey = null)
    {
        if (string.IsNullOrWhiteSpace(disciplineKey))
            return BadRequest(new { error = "disciplineKey is required" });

        return await this.CachedJson(_cache,
            $"http:swimmer:{id}:progress:{disciplineKey}",
            async () =>
            {
                var rows = await _swims.GetSwimsAsync(id);
                var profile = await _results.GetSwimmerProfileAsync(id);
                return SwimmerPageBuilder.Progress(rows, disciplineKey, profile?.BirthYear ?? 0);
            },
            PayloadTtl, CacheControlValue);
    }

    /// <summary>
    /// Какой сезон считать выбранным: явный <c>?season=2025</c>, <c>all</c> — карьера,
    /// ничего — ВИТРИННЫЙ сезон (до зимних чемпионатов это прошлый, см.
    /// docs/season-boundary-rule.md). Мусор в параметре трактуем как умолчание, а не как 400:
    /// это витрина, а не форма.
    /// </summary>
    /// <summary>
    /// Поиск пловца для селектора таба H2H. Публичный: пловцы и их результаты и так
    /// открыты, ничего сверх того, что уже отдаёт страница, здесь нет.
    /// </summary>
    [HttpGet("/api/swimmers/search")]
    public async Task<IActionResult> Search([FromQuery] string? q = null, [FromQuery] int limit = 12)
    {
        var query = (q ?? string.Empty).Trim();
        // Пустой и односимвольный запрос — не ошибка, а «ещё не набрали»: селектор просит
        // подсказки на каждой букве, и 400 в консоли на каждый первый символ бесполезен.
        if (query.Length < 2) return Ok(Array.Empty<SwimmerSearchHitDto>());

        return Ok(await _swims.SearchSwimmersAsync(query, limit));
    }

    /// <summary>
    /// Таб H2H: лучшие времена хозяина страницы и выбранного соперника бок о бок за один
    /// период — сезон карусели либо <c>?season=all</c> (карьера).
    ///
    /// Соперник приходит как <c>h2h_b</c> — тем же именем, что в адресе страницы
    /// (<c>/h2h?h2h_a=&amp;h2h_b=</c>) и таба (<c>?tab=h2h&amp;h2h_b=</c>): на одну сущность
    /// одно имя во всём h2h (решение Влада 03.09.2026). Левая сторона здесь в пути, поэтому
    /// <c>h2h_a</c> эндпоинту не нужен. Прежнее <c>rivalId</c> принимаем на чтение — по нему
    /// уже ходят открытые вкладки.
    ///
    /// 404 — соперника нет; соперник = <paramref name="id"/> отклоняем: сравнение с самим
    /// собой не значит ничего.
    /// </summary>
    [HttpGet("/api/swimmers/{id:int}/compare")]
    public async Task<IActionResult> GetCompare(
        int id,
        [FromQuery(Name = "h2h_b")] int? h2hB = null,
        [FromQuery] int? rivalId = null,
        [FromQuery] string? season = null)
    {
        var rivalSwimmerId = h2hB ?? rivalId ?? 0;
        if (rivalSwimmerId <= 0 || rivalSwimmerId == id) return BadRequest(new { error = "h2h_b" });

        var rivalProfile = await _results.GetSwimmerProfileAsync(rivalSwimmerId);
        if (rivalProfile is null) return NotFound();

        return await this.CachedJson(_cache,
            $"http:swimmer:{id}:compare:{rivalSwimmerId}:{season ?? "default"}",
            async () =>
            {
                // Обе выборки идут через тот же кэшированный шов, что и остальные табы:
                // страница соперника, открытая следом, читает уже готовые заплывы.
                var mineRows = await _swims.GetSwimsAsync(id);
                var rivalRows = await _swims.GetSwimsAsync(rivalSwimmerId);
                var mineProfile = await _results.GetSwimmerProfileAsync(id);

                // Сезон приземляется на заплывы ХОЗЯИНА страницы: карусель сверху — его,
                // и таб обязан показывать тот же период, что остальные табы страницы.
                var selected = await ResolveSeasonAsync(mineRows, season);

                // Места среди сверстников живут внутри сезона: в режиме ∞ считаем их за
                // ВИТРИННЫЙ сезон (как фильтр «Season best» страницы пловца), а подпись
                // ответа говорит, за какой именно.
                var sbSeason = selected ?? await _showcase.CurrentStartYearAsync();
                var axis = RecordAgeAxisSetting.From(_settings);

                var mine = await CompareSideAsync(mineRows, mineProfile, selected, sbSeason, axis);
                var rival = await CompareSideAsync(rivalRows, rivalProfile, selected, sbSeason, axis);
                return SwimmerPageBuilder.Compare(mine, rival, selected, sbSeason, axis);
            },
            PayloadTtl, CacheControlValue);
    }

    /// <summary>
    /// Вход одной стороны сравнения: заплывы, профиль, СВОЯ когорта сверстников и СВОЙ срез
    /// справочника рекордов. Своя у каждого, потому что год рождения и возрастная ступень у
    /// соперника другие — общий срез выдал бы ему чужие бейджи.
    ///
    /// Когорта тянется только под конкретный сезон (за карьеру сравнивать не с чем) и
    /// кэшируется НА ГРУППУ, а не на пловца, поэтому вторая сторона обычно достаётся даром.
    /// </summary>
    private async Task<SwimmerPageBuilder.SwimmerCompareInput> CompareSideAsync(
        IReadOnlyList<SeasonSwimRow> rows, SwimmerProfileDto? profile, int? season,
        int? seasonBestSeason, RecordAgeAxis axis)
    {
        // Когорта нужна под сезон, за который считаются места: в режиме ∞ это витринный.
        var cohort = seasonBestSeason is int year && profile is { BirthYear: > 0 }
            ? await _swims.GetAgeCohortSeasonBestsAsync(year, profile.BirthYear)
            : [];

        var records = await RecordStepsAsync(SwimmerPageBuilder.InSeason(rows, season), profile, axis);

        // Что пловец ДЕРЖИТ — отдельный вопрос от «бьёт ли показанное время рекорд»:
        // рекордный заплыв может быть поставлен до периода импорта, и счётчики шапки
        // обязаны его учитывать (тот же список, что в плитке достижений профиля).
        var held = profile is null ? [] : await _swims.GetRecordsHeldAsync(profile.Id);

        return new SwimmerPageBuilder.SwimmerCompareInput(rows, profile, cohort, records, held);
    }

    /// <summary>
    /// Срезы справочника рекордов для набора заплывов: ступени собираются ПО ЗАПЛЫВАМ
    /// (<see cref="SwimmerPageBuilder.RecordStepsOf"/>), а не по возрасту пловца, потому что
    /// мастерский старт меряется полосой возраста, обычный взрослый — открытым рекордом
    /// страны, а ребёнок — своей возрастной ступенью.
    ///
    /// Один и тот же набор нужен и личникам, и сравнению — отсюда общий метод: две копии
    /// правил разъехались бы, и «Δ Israel» на одной странице спорила бы с бейджем на другой.
    /// </summary>
    private async Task<IReadOnlyDictionary<
        SwimmerPageBuilder.RecordStep, IReadOnlyDictionary<string, NationalAgeRecordRow>>> RecordStepsAsync(
        IReadOnlyList<SeasonSwimRow> rows, SwimmerProfileDto? profile, RecordAgeAxis axis)
    {
        var records = new Dictionary<
            SwimmerPageBuilder.RecordStep, IReadOnlyDictionary<string, NationalAgeRecordRow>>();
        if (profile is null) return records;

        var steps = rows
            .Where(SeasonAggregator.IsCountable)
            .SelectMany(r => SwimmerPageBuilder.RecordStepsOf(r, profile.BirthYear, axis))
            .Distinct();

        foreach (var step in steps)
        {
            records[step] = await _swims.GetNationalRecordsAsync(
                profile.CountryCode, profile.Gender, step.Category, step.AgeKey);
        }

        return records;
    }

    private async Task<int?> ResolveSeasonAsync(IReadOnlyList<SeasonSwimRow> rows, string? season)
    {
        if (string.Equals(season, AllSeasons, StringComparison.OrdinalIgnoreCase)) return null;
        if (int.TryParse(season, out var explicitYear)) return explicitYear;

        return await DefaultSeasonAsync(rows);
    }

    /// <summary>Витринный сезон, приземлённый на сезоны, в которых пловец действительно плавал.</summary>
    private async Task<int?> DefaultSeasonAsync(IReadOnlyList<SeasonSwimRow> rows)
    {
        if (rows.Count == 0) return null;

        var showcase = await _showcase.CurrentStartYearAsync();

        var seasons = SwimmerPageBuilder.Seasons(rows, showcase, SeasonMath.CurrentStartYear());
        return seasons.FirstOrDefault(s => s.IsDisplayDefault)?.Season;
    }
}
