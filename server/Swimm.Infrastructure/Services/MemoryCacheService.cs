using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Swimm.Application.Abstractions;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// IMemoryCache реализация ICacheService — подходит для одного инстанса.
/// Для Redis: реализовать RedisCacheService через IDistributedCache + System.Text.Json
/// и заменить регистрацию в DependencyInjection.cs.
/// </summary>
public class MemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    // Токен привязывается ко всем записям; отмена вылетает их все разом.
    private CancellationTokenSource _cts = new();

    public MemoryCacheService(IMemoryCache cache) => _cache = cache;

    public Task<T?> GetAsync<T>(string key)
    {
        _cache.TryGetValue(key, out T? value);
        return Task.FromResult(value);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan ttl)
    {
        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(ttl)
            .AddExpirationToken(new CancellationChangeToken(_cts.Token));
        _cache.Set(key, value, options);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }

    public Task InvalidateAllAsync()
    {
        // Атомарно заменяем токен: новые записи получат новый токен,
        // старые вылетают по отмене предыдущего.
        var old = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
        return Task.CompletedTask;
    }
}
