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

    private const string CacheControlValue = "public, max-age=60";
    private static readonly TimeSpan PayloadTtl = TimeSpan.FromMinutes(5);

    /// <summary>Значение <c>?season=</c>, означающее «за карьеру».</summary>
    private const string AllSeasons = "all";

    public SwimmersPublicController(
        ISwimmerPageRepository swims, IResultRepository results, ICacheService cache)
    {
        _swims = swims;
        _results = results;
        _cache = cache;
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

        var showcase = ShowcaseSeason.StartYearOf(
            await _swims.GetWinterChampionshipDatesAsync(), DateTime.UtcNow);

        dto.Seasons = SwimmerPageBuilder.Seasons(rows, showcase, SeasonMath.CurrentStartYear());

        // Возраст показываем для сезона, на котором стоит карусель: подпись «12 year (2014)»
        // обязана совпадать с тем, что человек видит в панели.
        var displaySeason = dto.Seasons.FirstOrDefault(s => s.IsDisplayDefault)?.Season ?? showcase;
        dto.AgeInSeason = SeasonMath.AgeInSeason(displaySeason, dto.BirthYear);

        dto.AgeGroup = await _swims.GetLadderGroupAsync(rows.Select(r => r.CompetitionId));
        dto.RecordsHeld = await _swims.CountRecordsHeldAsync(id);

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

        var clubBest = profile.ClubId is int clubId
            ? await _swims.GetClubBestMsAsync(clubId)
            : new Dictionary<string, int>();

        // Возраст берём тот же, что подписан в шапке: колонка отвечает на вопрос «сколько
        // мне осталось до рекорда МОЕЙ ступени», а не «до ступени того года, когда я плыл».
        var displaySeason = await DefaultSeasonAsync(rows) ?? SeasonMath.CurrentStartYear();
        var age = SeasonMath.AgeInSeason(displaySeason, profile.BirthYear);

        var records = age is int a
            ? await _swims.GetNationalAgeRecordsAsync(profile.CountryCode, profile.Gender, a)
            : new Dictionary<string, NationalAgeRecordRow>();

        return SwimmerPageBuilder.PersonalBests(rows, poolType, clubBest, records);
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

        var showcase = ShowcaseSeason.StartYearOf(
            await _swims.GetWinterChampionshipDatesAsync(), DateTime.UtcNow);

        var seasons = SwimmerPageBuilder.Seasons(rows, showcase, SeasonMath.CurrentStartYear());
        return seasons.FirstOrDefault(s => s.IsDisplayDefault)?.Season;
    }
}
