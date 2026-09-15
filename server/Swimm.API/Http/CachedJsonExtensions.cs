using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Swimm.Application.Abstractions;

namespace Swimm.API.Http;

/// <summary>
/// Общий хелпер HTTP-кэша для публичных GET (этап 3.1, вынесен из RecordsController).
/// Сериализованный JSON и его ETag (SHA-256) кэшируются в ICacheService; клиенту — Cache-Control
/// + ETag, повтор с If-None-Match на совпавший ETag → 304 без тела. Сбрасывать конкретные ключи
/// не нужно: запись получает метки таблиц сама (К3), а сохранение через EF сбрасывает их само (К4).
/// </summary>
public static class CachedJsonExtensions
{
    /// <param name="tags">
    /// Явные метки сверх тех, что запись получит сама из SQL, — метки страниц для ручного сброса
    /// из админки (<c>CacheTags.ClubPages</c>, <c>CacheTags.ClubPage(id)</c>).
    /// </param>
    public static async Task<IActionResult> CachedJson<T>(
        this ControllerBase controller,
        ICacheService cache,
        string cacheKey,
        Func<Task<T>> load,
        TimeSpan payloadTtl,
        string cacheControl,
        params string[] tags)
    {
        // GetOrCreate, а не Get + Set: параллельные промахи одного ключа ждут ОДНУ сборку.
        // После общего сброса витрину открывают сразу многие, и каждый строил тяжёлый ответ
        // заново (docs/plans/cache-tags-plan.md, К1).
        var entry = await cache.GetOrCreateAsync(cacheKey, async () =>
        {
            var json = JsonSerializer.Serialize(await load());
            var etag = $"\"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..32]}\"";
            return new CachedPayload(json, etag);
        }, payloadTtl, tags);

        controller.Response.Headers[HeaderNames.CacheControl] = cacheControl;
        controller.Response.Headers[HeaderNames.ETag] = entry.ETag;

        if (controller.Request.Headers.IfNoneMatch.Contains(entry.ETag))
            return controller.StatusCode(StatusCodes.Status304NotModified);

        return controller.Content(entry.Json, "application/json; charset=utf-8");
    }

    /// <summary>
    /// Готовый ответ. Свой размер знает без сериализации (/Admin/Cache): System.Text.Json по
    /// умолчанию экранирует всё не-ASCII (иврит — <c>י</c>), так что символы = байты.
    /// </summary>
    private sealed record CachedPayload(string Json, string ETag) : ICacheSizedValue
    {
        long ICacheSizedValue.SizeBytes => Json.Length + ETag.Length;
    }
}
