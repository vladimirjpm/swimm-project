using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;

namespace Swimm.API.Controllers;

/// <summary>
/// Публичные рекорды и нормативы (замена клиентских normative*.js).
/// Кэш и выборки — по регионам: ?region=world | EU | ISR | USA …
///
/// HTTP-кэширование (часть этапа 3.1, вытащена вперёд): ответ отдаётся с ETag +
/// Cache-Control. Сериализованный JSON и его хэш кэшируются рядом с данными в
/// ICacheService — та же токен-инвалидация из админ-CRUD (InvalidateAllAsync), поэтому
/// после правки рекорда браузеры получают свежие данные первой же ревалидацией.
/// </summary>
[ApiController]
public class RecordsController : ControllerBase
{
    private readonly IRecordRepository _records;
    private readonly ICacheService _cache;

    // max-age=300: браузер 5 минут не ходит в сеть вообще, потом дешёвая ревалидация
    // по ETag (304 без тела). Компромисс «свежесть после правки в админке» ↔ «ноль
    // запросов на каждую загрузку страницы».
    private const string CacheControlValue = "public, max-age=300";
    private static readonly TimeSpan PayloadTtl = TimeSpan.FromHours(24);

    public RecordsController(IRecordRepository records, ICacheService cache)
    {
        _records = records;
        _cache = cache;
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

        return await CachedJson(
            $"http:records:{region.Trim().ToUpperInvariant()}:{category ?? "all"}",
            () => _records.GetRecordsAsync(region, category));
    }

    /// <summary>Нормативы уровней. kind: regular/masters (опционально — иначе все).</summary>
    [HttpGet("/api/normative-standards")]
    public async Task<IActionResult> GetStandards([FromQuery] string? kind)
    {
        if (kind != null && !NormativeStandard.Kinds.Contains(kind))
            return BadRequest($"kind must be one of: {string.Join(", ", NormativeStandard.Kinds)}");

        return await CachedJson(
            $"http:normative-standards:{kind ?? "all"}",
            () => _records.GetStandardsAsync(kind));
    }

    /// <summary>Сериализованный ответ + ETag из ICacheService; If-None-Match → 304 без тела.</summary>
    private async Task<IActionResult> CachedJson<T>(string cacheKey, Func<Task<T>> load)
    {
        var entry = await _cache.GetAsync<CachedPayload>(cacheKey);
        if (entry is null)
        {
            var json = JsonSerializer.Serialize(await load());
            var etag = $"\"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..32]}\"";
            entry = new CachedPayload(json, etag);
            await _cache.SetAsync(cacheKey, entry, PayloadTtl);
        }

        Response.Headers[HeaderNames.CacheControl] = CacheControlValue;
        Response.Headers[HeaderNames.ETag] = entry.ETag;

        if (Request.Headers.IfNoneMatch.Contains(entry.ETag))
            return StatusCode(StatusCodes.Status304NotModified);

        return Content(entry.Json, "application/json; charset=utf-8");
    }

    private sealed record CachedPayload(string Json, string ETag);
}
