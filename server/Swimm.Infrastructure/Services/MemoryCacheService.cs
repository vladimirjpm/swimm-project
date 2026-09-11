using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Swimm.Application.Abstractions;
using Swimm.Application.Validation;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// IMemoryCache реализация ICacheService — подходит для одного инстанса.
/// Для Redis: реализовать RedisCacheService через IDistributedCache + System.Text.Json
/// и заменить регистрацию в DependencyInjection.cs (docs/plans/cache-tags-plan.md §5).
///
/// Метки — токены отмены: у каждой метки свой <see cref="CancellationTokenSource"/>, и запись
/// подписана на токены своих меток плюс общий (метка all). Сброс метки = отмена её токена:
/// все подписанные записи вылетают разом, без перебора ключей. В Redis то же самое делает
/// версия метки (INCR), поэтому потребителям разница не видна.
/// </summary>
public class MemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;

    // Общий токен (метка all) и токены меток. Старые токены только ОТМЕНЯЕМ, но не Dispose:
    // соседний поток мог уже взять ссылку на источник и вот-вот спросит у него Token —
    // у освобождённого источника это ObjectDisposedException. Без таймеров и связанных
    // регистраций неосвобождённый источник ничего не держит.
    private CancellationTokenSource _all = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tags = new();

    // Сборки в полёте: ключ → Lazy<Task<T>>. Параллельные промахи одного ключа ждут одну сборку.
    private readonly ConcurrentDictionary<string, object> _inflight = new();

    public MemoryCacheService(IMemoryCache cache) => _cache = cache;

    public Task<T?> GetAsync<T>(string key)
    {
        _cache.TryGetValue(key, out T? value);
        return Task.FromResult(value);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan ttl) => SetAsync(key, value, ttl, []);

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, params string[] tags)
    {
        Store(key, value, ttl, CaptureTokens(tags));
        return Task.CompletedTask;
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, params string[] tags)
        where T : class
    {
        if (_cache.TryGetValue(key, out T? hit) && hit is not null) return hit;

        var build = (Lazy<Task<T>>)_inflight.GetOrAdd(key,
            _ => new Lazy<Task<T>>(() => BuildAsync(key, factory, ttl, tags)));
        try
        {
            return await build.Value;
        }
        finally
        {
            // Снимаем ровно СВОЮ сборку: упавшую — чтобы следующий запрос попробовал заново,
            // удачную — потому что результат уже лежит в кэше.
            _inflight.TryRemove(new KeyValuePair<string, object>(key, build));
        }
    }

    private async Task<T> BuildAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, string[] tags)
    {
        // Токены — ДО сборки. Сбросили данные, пока ответ строился (правка в админке посреди
        // тяжёлого запроса), — снятый токен уже отменён, и собранное из старых данных в кэш
        // не ляжет. Со снятием ПОСЛЕ сборки оно пролежало бы до конца TTL.
        var tokens = CaptureTokens(tags);
        var value = await factory();
        Store(key, value, ttl, tokens);
        return value;
    }

    public Task RemoveAsync(string key)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }

    public Task InvalidateTagsAsync(params string[] tags)
    {
        foreach (var tag in tags)
        {
            if (tag == Application.Constants.CacheTags.All)
            {
                InvalidateAll();
                continue;
            }
            if (_tags.TryRemove(tag, out var source)) source.Cancel();
        }
        return Task.CompletedTask;
    }

    public Task InvalidateAllAsync()
    {
        InvalidateAll();
        return Task.CompletedTask;
    }

    private void InvalidateAll()
    {
        // Атомарно заменяем общий токен: новые записи получат новый, старые вылетают по отмене.
        Interlocked.Exchange(ref _all, new CancellationTokenSource()).Cancel();

        // Токены меток тоже отменяем и выбрасываем. Просто очистить словарь нельзя: запись,
        // которая собиралась в этот момент, могла взять новый общий токен и СТАРЫЙ токен метки —
        // после очистки сброс этой метки её бы уже не нашёл. Отмена даёт лишний сброс, не
        // недосброс, и заодно не даёт словарю копить метки строк (row:Swimmers:…) бесконечно.
        foreach (var tag in _tags.Keys)
            if (_tags.TryRemove(tag, out var source)) source.Cancel();
    }

    private CancellationToken[] CaptureTokens(string[] tags)
    {
        var tokens = new CancellationToken[tags.Length + 1];
        tokens[0] = Volatile.Read(ref _all).Token;
        for (var i = 0; i < tags.Length; i++)
            tokens[i + 1] = _tags.GetOrAdd(tags[i], _ => new CancellationTokenSource()).Token;
        return tokens;
    }

    private void Store<T>(string key, T value, TimeSpan ttl, CancellationToken[] tokens)
    {
        // Правило Redis проверяется уже на памяти: память проглотит кортеж, а Redis запишет его
        // как {} (CacheValueRules). Вердикт кэшируется на тип — проверка бесплатна.
        CacheValueRules.EnsureStorable(typeof(T));

        var options = new MemoryCacheEntryOptions().SetAbsoluteExpiration(ttl);
        foreach (var token in tokens)
            options.AddExpirationToken(new CancellationChangeToken(token));
        // Токен уже отменён (сброс пришёл во время сборки) — IMemoryCache сочтёт запись
        // протухшей сразу, и в кэш она не ляжет.
        _cache.Set(key, value, options);
    }
}
