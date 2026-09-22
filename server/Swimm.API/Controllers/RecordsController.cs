using Microsoft.AspNetCore.Mvc;
using Swimm.API.Http;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;

namespace Swimm.API.Controllers;

/// <summary>
/// Публичные рекорды и нормативы (замена клиентских normative*.js).
/// Кэш и выборки — по регионам: ?region=world | EU | ISR | USA …
///
/// HTTP-кэширование (часть этапа 3.1, вытащена вперёд): ответ отдаётся с ETag +
/// Cache-Control. Сериализованный JSON и его хэш кэшируются рядом с данными в
/// ICacheService; правка рекорда сбрасывает метку Records сама (К4), поэтому браузеры получают
/// свежие данные первой же ревалидацией.
/// </summary>
[ApiController]
public class RecordsController : ControllerBase
{
    private readonly IRecordRepository _records;
    private readonly ICacheService _cache;
    private readonly IDebugOptionsService _debugOptions;

    // max-age=300: браузер 5 минут не ходит в сеть вообще, потом дешёвая ревалидация
    // по ETag (304 без тела). Компромисс «свежесть после правки в админке» ↔ «ноль
    // запросов на каждую загрузку страницы».
    private const string CacheControlValue = "public, max-age=300";
    private static readonly TimeSpan PayloadTtl = TimeSpan.FromHours(24);

    // Страна в дисциплине ровно одна строка, стран ~215 — дефолт покрывает весь рейтинг
    // целиком, и пагинация нужна только тому, кто её попросит.
    private const int DefaultRankingLimit = 250;
    private const int MaxRankingLimit = 500;
    private const int MaxRegionsInFilter = 50;

    public RecordsController(
        IRecordRepository records, ICacheService cache, IDebugOptionsService debugOptions)
    {
        _records = records;
        _cache = cache;
        _debugOptions = debugOptions;
    }

    /// <summary>
    /// Когда справочник сверяли с каждым источником — подпись «checked …» на витрине
    /// (docs/plans/records-freshness-plan.md, U5). Дата ПО КАЖДОМУ источнику, не свёрнутый
    /// минимум: сворачивать (по табу `/records`, по карточкам пловца) — дело клиента.
    ///
    /// Наружу — только даты: текст сбоев и «ждёт Apply» остаются админке. Журнал — Sys_-таблица
    /// вне грантов swimm_ro, поэтому читает его сервис, а не публичный read-контекст.
    /// Серверного кэша нет (пять источников — три лёгких запроса на каждый), браузер
    /// ревалидирует каждый раз: дата должна сдвинуться сразу после «Проверить все».
    /// </summary>
    [HttpGet("/api/records/freshness")]
    public async Task<IActionResult> GetFreshness([FromServices] IRecordSourceCheckService checks)
    {
        var all = await checks.GetFreshnessAsync(HttpContext.RequestAborted);
        Response.Headers.CacheControl = "public, no-cache";
        return Ok(all.Select(f => new { source = f.Source, checkedAt = f.CheckedAt, changedAt = f.ChangedAt }));
    }

    /// <summary>
    /// Рекорды региона. region обязателен (world | код континента | код страны),
    /// category: open/age/junior/masters (опционально).
    /// </summary>
    [HttpGet("/api/records")]
    public async Task<IActionResult> GetRecords(
        [FromQuery] string? region,
        [FromQuery] string? category)
    {
        if (string.IsNullOrWhiteSpace(region))
            return BadRequest("region is required: 'world', continent code (EU/AS) or country code (ISR/USA/…)");

        if (category != null && !Record.Categories.Contains(category))
            return BadRequest($"category must be one of: {string.Join(", ", Record.Categories)}");

        // Подробности держателя — отладочная опция; она же попадает в ключ кэша, иначе
        // после выключения витрина показывала бы их ещё сутки.
        var withDetails = await _debugOptions.IsEnabledAsync(
            DebugOptionKeys.ShowAgeRecordsDetails, HttpContext.RequestAborted);

        return await this.CachedJson(_cache,
            $"http:records:{region.Trim().ToUpperInvariant()}:{category ?? "all"}"
                + (withDetails ? ":details" : ""),
            () => _records.GetRecordsAsync(region, category, withDetails),
            PayloadTtl, CacheControlValue);
    }

    /// <summary>
    /// Рейтинг стран по одной дисциплине (этап 11.2.1): кто быстрее на 50 вольным в короткой
    /// воде и насколько отстаёт от мирового рекорда.
    ///
    /// Четыре оси дисциплины обязательны — <c>style</c>, <c>distance</c>, <c>gender</c>,
    /// <c>pool</c>: рейтинг, в котором смешаны дистанции или полы, показывать нечего.
    /// Категория зафиксирована <c>open</c> и параметром не выносится (у других стран
    /// age/masters не бывает).
    ///
    /// <c>regions</c> — через запятую, чтобы сузить список (для 11.3 это же поле с двумя
    /// кодами). <c>limit</c>/<c>offset</c> — пагинация; в дисциплине не больше одной строки
    /// на страну, так что по умолчанию влезает всё.
    /// </summary>
    [HttpGet("/api/records/ranking")]
    public async Task<IActionResult> GetRanking(
        [FromQuery] string? style,
        [FromQuery] string? distance,
        [FromQuery] string? gender,
        [FromQuery] string? pool,
        [FromQuery] string? regions,
        [FromQuery] int? limit,
        [FromQuery] int? offset)
    {
        if (string.IsNullOrWhiteSpace(style) || string.IsNullOrWhiteSpace(distance))
            return BadRequest("style and distance are required, e.g. ?style=freestyle&distance=50m");

        var genderKey = (gender ?? "").Trim().ToLowerInvariant();
        if (genderKey.Length == 0 || Record.ValidateGender(genderKey, distance) is not null)
            return BadRequest("gender is required: 'male', 'female', or 'mixed' (relay distances only)");

        var poolKey = (pool ?? "").Trim().ToLowerInvariant();
        if (poolKey is not ("25m" or "50m"))
            return BadRequest("pool is required and must be '25m' or '50m'");

        // Коды нормализуем и СОРТИРУЕМ: ключ кэша обязан быть один и тот же для ?regions=USA,FRA
        // и ?regions=FRA,USA, иначе одна выборка греется дважды. Потолок — чтобы произвольно
        // длинный список не размазывал кэш и не уезжал в ключ целиком.
        var regionCodes = (regions ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => c.ToUpperInvariant())
            .Distinct()
            .Order(StringComparer.Ordinal)
            .Take(MaxRegionsInFilter)
            .ToList();

        var query = RecordRankingQuery.Create(
            style, distance, genderKey, poolKey, regionCodes,
            limit: Math.Clamp(limit ?? DefaultRankingLimit, 1, MaxRankingLimit),
            offset: Math.Max(0, offset ?? 0));

        return await this.CachedJson(_cache,
            "http:records:ranking:"
                + $"{query.Style}:{query.Distance}:{query.Gender}:{query.PoolType}:"
                + $"{(regionCodes.Count == 0 ? "all" : string.Join(",", regionCodes))}:"
                + $"{query.Limit}:{query.Offset}",
            () => _records.GetRankingAsync(query),
            PayloadTtl, CacheControlValue);
    }

    /// <summary>
    /// Страны, у которых в справочнике есть рекорды — список для выбора сторон сравнения.
    /// Что есть В БАЗЕ; живой источник и права здесь ни при чём (это не админский эндпоинт).
    /// </summary>
    [HttpGet("/api/records/countries")]
    public Task<IActionResult> GetRecordCountries()
        => this.CachedJson(_cache, "http:records:countries",
            () => _records.GetRecordCountriesAsync(), PayloadTtl, CacheControlValue);

    /// <summary>
    /// Число мировых рекордов по категориям — подписи табов /records (WR, World Junior, Masters).
    /// </summary>
    [HttpGet("/api/records/world-counts")]
    public Task<IActionResult> GetWorldCounts()
        => this.CachedJson(_cache, "http:records:world-counts",
            () => _records.GetWorldCountsAsync(), PayloadTtl, CacheControlValue);

    /// <summary>
    /// Сравнение двух стран по рекордам (этап 11.3.1): общая ось дисциплин, время каждой
    /// стороны, дельта и сводный счёт.
    ///
    /// <c>pool</c> и <c>gender</c> — необязательные разрезы, в отличие от рейтинга: смысл
    /// экрана в обходе ВСЕХ дисциплин сразу.
    ///
    /// ⚠ Дисциплина, где рекорда нет у одной из сторон, остаётся строкой «нет данных» и в
    /// счёт не идёт ни в чью пользу — иначе страна с половинным покрытием выигрывала бы
    /// пустотами (требование 11.3.3).
    /// </summary>
    [HttpGet("/api/records/compare")]
    public async Task<IActionResult> GetCompare(
        [FromQuery] string? a,
        [FromQuery] string? b,
        [FromQuery] string? pool,
        [FromQuery] string? gender)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return BadRequest("a and b are required country codes, e.g. ?a=ISR&b=USA");

        if (pool != null && pool.Trim().ToLowerInvariant() is not ("25m" or "50m" or ""))
            return BadRequest("pool must be '25m' or '50m' when given");

        if (gender != null && gender.Trim().ToLowerInvariant() is not ("male" or "female" or "mixed" or ""))
            return BadRequest("gender must be 'male', 'female' or 'mixed' when given");

        var query = RecordCompareQuery.Create(a, b, pool, gender);

        if (query.A == query.B)
            return BadRequest("a and b must be different countries");

        return await this.CachedJson(_cache,
            $"http:records:compare:{query.A}:{query.B}"
                + $":{query.PoolType ?? "all"}:{query.Gender ?? "all"}",
            () => _records.GetCompareAsync(query),
            PayloadTtl, CacheControlValue);
    }

    /// <summary>
    /// Нормативы уровней. kind: regular/masters (опционально — иначе все).
    /// country: alpha-3 код системы нормативов (опционально — иначе легаси-поведение без фильтра).
    /// </summary>
    [HttpGet("/api/normative-standards")]
    public async Task<IActionResult> GetStandards([FromQuery] string? kind, [FromQuery] string? country)
    {
        if (kind != null && !NormativeStandard.Kinds.Contains(kind))
            return BadRequest($"kind must be one of: {string.Join(", ", NormativeStandard.Kinds)}");

        var countryKey = string.IsNullOrWhiteSpace(country) ? null : country.Trim().ToUpperInvariant();

        return await this.CachedJson(_cache,
            $"http:normative-standards:{kind ?? "all"}:{countryKey ?? "all"}",
            () => _records.GetStandardsAsync(kind, countryKey),
            PayloadTtl, CacheControlValue);
    }
}
