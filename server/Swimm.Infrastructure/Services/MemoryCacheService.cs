using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
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
///
/// Откуда метки (К3): явные — от вызывающего; метки таблиц — САМИ, из SQL, выполненного во
/// время сборки (<see cref="CacheBuildScope"/> + перехватчик команд EF); метки вложенных
/// записей — наследуются при попадании в них. Запись хранит свои метки рядом со значением.
/// </summary>
public class MemoryCacheService : ICacheService, ICacheDiagnostics
{
    private readonly IMemoryCache _cache;

    // Общий токен (метка all) и токены меток. Старые токены только ОТМЕНЯЕМ, но не Dispose:
    // соседний поток мог уже взять ссылку на источник и вот-вот спросит у него Token —
    // у освобождённого источника это ObjectDisposedException. Без таймеров и связанных
    // регистраций неосвобождённый источник ничего не держит.
    private CancellationTokenSource _all = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tags = new();

    // Сборки в полёте: ключ → Lazy<Task<Entry>>. Параллельные промахи одного ключа ждут одну сборку.
    private readonly ConcurrentDictionary<string, object> _inflight = new();

    // Живые записи — для диагностики (/Admin/Cache). Убираются колбэком вытеснения.
    private readonly ConcurrentDictionary<string, Entry> _index = new();

    public MemoryCacheService(IMemoryCache cache) => _cache = cache;

    /// <summary>
    /// Запись в IMemoryCache: значение и его метки с токенами, с которыми оно валидно.
    /// Токены нужны при попадании во вложенный кэш — внешняя сборка наследует ИМЕННО их.
    /// </summary>
    private sealed record Entry(
        object? Value,
        IReadOnlyDictionary<string, CancellationToken> Tokens,
        string ValueType,
        DateTimeOffset StoredAt,
        DateTimeOffset ExpiresAt);

    public Task<T?> GetAsync<T>(string key)
    {
        if (_cache.TryGetValue(key, out Entry? entry) && entry?.Value is T value)
        {
            CacheBuildScope.Current?.Inherit(entry.Tokens);
            return Task.FromResult<T?>(value);
        }
        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan ttl) => SetAsync(key, value, ttl, []);

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, params string[] tags)
    {
        // Ручная запись внутри чужой сборки: какие из её таблиц нужны именно этому значению,
        // уже не отличить — берём все, накопленные на этот момент. Лишний сброс безопасен.
        var tokens = CaptureTokens(tags);
        if (CacheBuildScope.Current is { } scope)
            foreach (var (tag, token) in scope.Tokens) tokens.TryAdd(tag, token);

        Store(key, value, typeof(T), ttl, tokens);
        return Task.CompletedTask;
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, params string[] tags)
        where T : class?
    {
        for (var attempt = 0; ; attempt++)
        {
            if (_cache.TryGetValue(key, out Entry? hit) && hit?.Value is T cached)
            {
                CacheBuildScope.Current?.Inherit(hit.Tokens);
                return cached;
            }

            var mine = new Lazy<Task<Entry>>(() => BuildAsync(key, factory, ttl, tags));
            var build = (Lazy<Task<Entry>>)_inflight.GetOrAdd(key, mine);
            try
            {
                var entry = await build.Value;
                // Наследует и тот, кто строил, и те, кто ждал его сборку: у каждого своя внешняя
                // сборка, и каждой нужно знать, из чего собран этот ответ.
                CacheBuildScope.Current?.Inherit(entry.Tokens);
                return (T)entry.Value!;
            }
            catch (OperationCanceledException) when (!ReferenceEquals(build, mine) && attempt == 0)
            {
                // Сборку отменил её владелец — оборвали ЕГО запрос (фабрики получают токен
                // запроса). Ждавшие не должны падать вместе с ним: строим сами, один раз.
            }
            finally
            {
                // Снимаем ровно ЭТУ сборку: упавшую — чтобы следующий запрос попробовал заново,
                // удачную — потому что результат уже лежит в кэше.
                _inflight.TryRemove(new KeyValuePair<string, object>(key, build));
            }
        }
    }

    private async Task<Entry> BuildAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, string[] tags)
    {
        // Явные метки и общий токен — ДО сборки, метки таблиц — в момент касания (перехватчик).
        // Сбросили данные, пока ответ строился, — снятый токен уже отменён, и собранное из
        // старых данных в кэш не ляжет. Со снятием ПОСЛЕ сборки оно пролежало бы до конца TTL.
        var tokens = CaptureTokens(tags);
        using var scope = CacheBuildScope.Begin(CaptureTagToken);

        var value = await factory();

        foreach (var (tag, token) in scope.Tokens) tokens.TryAdd(tag, token);
        // «Не найдено» (null) не кэшируем — как и до К3: следующий запрос спросит базу снова.
        return value is null
            ? new Entry(null, tokens, typeof(T).Name, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            : Store(key, value, typeof(T), ttl, tokens);
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
            if (tag == CacheTags.All)
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

    public IReadOnlyList<CacheEntryInfo> Snapshot() => _index
        // IMemoryCache вытесняет протухшее лениво — в снимок живые записи, а не хвост индекса.
        .Where(e => e.Value.ExpiresAt > DateTimeOffset.UtcNow
                    && !e.Value.Tokens.Values.Any(t => t.IsCancellationRequested))
        .Select(e => new CacheEntryInfo(
            e.Key,
            e.Value.Tokens.Keys.Where(t => t != CacheTags.All).OrderBy(t => t, StringComparer.Ordinal).ToList(),
            e.Value.ValueType,
            e.Value.StoredAt,
            e.Value.ExpiresAt))
        .OrderBy(e => e.Key, StringComparer.Ordinal)
        .ToList();

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

    private CancellationToken CaptureTagToken(string tag) =>
        _tags.GetOrAdd(tag, _ => new CancellationTokenSource()).Token;

    private Dictionary<string, CancellationToken> CaptureTokens(string[] tags)
    {
        var tokens = new Dictionary<string, CancellationToken>(tags.Length + 1)
        {
            [CacheTags.All] = Volatile.Read(ref _all).Token,
        };
        foreach (var tag in tags) tokens.TryAdd(tag, CaptureTagToken(tag));
        return tokens;
    }

    private Entry Store(string key, object? value, Type valueType, TimeSpan ttl,
        IReadOnlyDictionary<string, CancellationToken> tokens)
    {
        // Правило Redis проверяется уже на памяти: память проглотит кортеж, а Redis запишет его
        // как {} (CacheValueRules). Вердикт кэшируется на тип — проверка бесплатна.
        CacheValueRules.EnsureStorable(valueType);

        var now = DateTimeOffset.UtcNow;
        var entry = new Entry(value, tokens, valueType.Name, now, now + ttl);

        var options = new MemoryCacheEntryOptions().SetAbsoluteExpiration(ttl);
        foreach (var token in tokens.Values)
            options.AddExpirationToken(new CancellationChangeToken(token));
        options.RegisterPostEvictionCallback(static (k, v, _, state) =>
        {
            // Убираем из индекса ровно ЭТУ запись: при перезаписи ключа колбэк старой записи
            // не должен стереть новую.
            if (v is Entry evicted && state is MemoryCacheService self)
                self._index.TryRemove(new KeyValuePair<string, Entry>((string)k, evicted));
        }, this);

        _index[key] = entry;
        // Токен уже отменён (сброс пришёл во время сборки) — IMemoryCache сочтёт запись
        // протухшей сразу, и в кэш она не ляжет.
        _cache.Set(key, entry, options);
        return entry;
    }
}
