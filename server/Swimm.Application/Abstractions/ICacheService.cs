namespace Swimm.Application.Abstractions;

/// <summary>
/// Абстракция кэша. Текущая реализация — IMemoryCache; для Redis достаточно
/// зарегистрировать RedisCacheService вместо MemoryCacheService в DI.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan ttl);
    Task RemoveAsync(string key);

    /// <summary>
    /// Сбрасывает весь кэш. Вызывать после импорта, удаления или очистки данных.
    /// IMemoryCache: отменяет CancellationToken — все записи вылетают разом.
    /// Redis: SCAN + DEL по префиксу или FLUSHDB на выделенном инстансе.
    /// </summary>
    Task InvalidateAllAsync();
}
