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
/// + ETag, повтор с If-None-Match на совпавший ETag → 304 без тела. Инвалидация глобальная
/// (ICacheService.InvalidateAllAsync после админ-мутаций/импорта) — отдельно инвалидировать
/// конкретные ключи не нужно.
/// </summary>
public static class CachedJsonExtensions
{
    public static async Task<IActionResult> CachedJson<T>(
        this ControllerBase controller,
        ICacheService cache,
        string cacheKey,
        Func<Task<T>> load,
        TimeSpan payloadTtl,
        string cacheControl)
    {
        // GetOrCreate, а не Get + Set: параллельные промахи одного ключа ждут ОДНУ сборку.
        // После общего сброса витрину открывают сразу многие, и каждый строил тяжёлый ответ
        // заново (docs/plans/cache-tags-plan.md, К1).
        var entry = await cache.GetOrCreateAsync(cacheKey, async () =>
        {
            var json = JsonSerializer.Serialize(await load());
            var etag = $"\"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..32]}\"";
            return new CachedPayload(json, etag);
        }, payloadTtl);

        controller.Response.Headers[HeaderNames.CacheControl] = cacheControl;
        controller.Response.Headers[HeaderNames.ETag] = entry.ETag;

        if (controller.Request.Headers.IfNoneMatch.Contains(entry.ETag))
            return controller.StatusCode(StatusCodes.Status304NotModified);

        return controller.Content(entry.Json, "application/json; charset=utf-8");
    }

    private sealed record CachedPayload(string Json, string ETag);
}
