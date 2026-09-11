namespace Swimm.Application.Abstractions;

/// <summary>
/// Абстракция кэша. Текущая реализация — IMemoryCache; для Redis достаточно
/// зарегистрировать RedisCacheService вместо MemoryCacheService в DI.
///
/// Контракт написан так, чтобы Redis лёг без переписывания потребителей
/// (docs/plans/cache-tags-plan.md §4–5):
/// <list type="bullet">
/// <item>значение — только DTO/record, переживающий JSON-круг (<see cref="Validation.CacheValueRules"/>):
/// в Redis лежат байты, не объекты;</item>
/// <item>значение из кэша не мутировать: в памяти правка видна всем следующим читателям, в
/// Redis — нет (там копия), и поведение молча разъедется;</item>
/// <item>сброс — по МЕТКЕ (<see cref="Constants.CacheTags"/>), а не перебором ключей: у Redis
/// нет дешёвого «найди ключи по шаблону», а версия метки — один INCR.</item>
/// </list>
///
/// Методы с метками и <see cref="GetOrCreateAsync{T}"/> имеют реализацию по умолчанию: кэш,
/// который меток не знает (заглушки в тестах), остаётся корректным — он просто сбрасывает всё
/// (лишний сброс безопасен, недосброс — нет) и строит значение без склейки параллельных промахов.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan ttl);

    /// <summary>
    /// Положить значение с метками — из каких данных оно собрано. Сброс любой из меток
    /// (<see cref="InvalidateTagsAsync"/>) выкидывает запись. Метка <see cref="Constants.CacheTags.All"/>
    /// есть у каждой записи неявно.
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan ttl, params string[] tags) => SetAsync(key, value, ttl);

    /// <summary>
    /// Взять из кэша или построить — ОДИН раз на все параллельные промахи одного ключа.
    ///
    /// Без склейки после сброса N одновременных запросов N раз строили один и тот же тяжёлый
    /// ответ (season-best, обзор соревнования). Метки снимаются ДО сборки: если данные сбросили,
    /// пока ответ строился, собранное уже устарело и в кэш не попадает — иначе оно пролежало бы
    /// до конца TTL.
    ///
    /// «Не найдено» — фабрика вернула null (T объявлен nullable) — в кэш не кладётся: следующий
    /// запрос спросит базу снова, как было до перевода мест кэша на этот метод.
    /// </summary>
    async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, params string[] tags)
        where T : class?
    {
        var hit = await GetAsync<T>(key);
        if (hit is not null) return hit;
        var value = await factory();
        if (value is not null) await SetAsync(key, value, ttl, tags);
        return value;
    }

    Task RemoveAsync(string key);

    /// <summary>
    /// Сбросить записи с любой из меток. По умолчанию — весь кэш: для реализации без меток это
    /// единственный честный ответ.
    /// </summary>
    Task InvalidateTagsAsync(params string[] tags) => InvalidateAllAsync();

    /// <summary>
    /// Сбрасывает весь кэш (= метка <see cref="Constants.CacheTags.All"/>). Вызывать после импорта,
    /// удаления или очистки данных.
    /// IMemoryCache: отменяет общий CancellationToken — все записи вылетают разом.
    /// Redis: INCR версии метки all — записи со старой версией становятся промахом.
    /// </summary>
    Task InvalidateAllAsync();
}
