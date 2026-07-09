using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// In-memory хранилище настроек (thread-safe).
/// Значения сбрасываются при перезапуске сервера.
/// </summary>
public class AdminSettingsService : ISettingsService
{
    private readonly ConcurrentDictionary<string, AdminSetting> _settings = new();
    private readonly IMemoryCache _cache;

    public AdminSettingsService(IMemoryCache cache)
    {
        _cache = cache;
        SeedDefaults();
    }

    private void SeedDefaults()
    {
        var defaults = new AdminSetting[]
        {
            new("MaintenanceMode", "false", "bool", "livesite",
                "Режим обслуживания: true — сайт закрыт для всех кроме админов, показывается заглушка"),
            new("SchemaCacheTTL", "10", "int", "admin",
                "Время жизни кеша схемы БД в минутах. После истечения — схема перечитывается из PostgreSQL"),
            new("ForceRefresh", "false", "bool", "admin",
                "Если true — кеш схемы сбрасывается при каждом запросе (отключает кеширование)"),
            new("ShowSystemTables", "false", "bool", "admin",
                "Показывать системные объекты PostgreSQL (схемы pg_catalog/information_schema) в схеме БД"),
            new("DefaultSchema", "public", "string", "both",
                "SQL-схема для фильтрации таблиц. Используется в admin (db.html) и может использоваться в публичных запросах"),
            new("ResultsLoadMode", "client", "string", "livesite",
                "Режим загрузки результатов клиентом: full — всё соревнование целиком (как сейчас); " +
                "paged — постранично с фильтрами на сервере (включится в фазе 3); " +
                "client — клиент выбирает сам через ?loadMode= (по умолчанию full). " +
                "full/paged принудительны — URL-параметр клиента игнорируется"),
        };

        foreach (var s in defaults)
            _settings.TryAdd(s.Key, s);
    }

    public IReadOnlyList<AdminSetting> GetAll()
        => _settings.Values.OrderBy(s => s.Scope).ThenBy(s => s.Key).ToList();

    public AdminSetting? Get(string key)
        => _settings.GetValueOrDefault(key);

    public T GetValue<T>(string key, T fallback)
    {
        if (!_settings.TryGetValue(key, out var setting))
            return fallback;

        try
        {
            return (T)Convert.ChangeType(setting.Value, typeof(T));
        }
        catch
        {
            return fallback;
        }
    }

    public bool Update(string key, string newValue)
    {
        if (!_settings.TryGetValue(key, out var existing))
            return false;

        if (!ValidateType(existing.DataType, newValue))
            return false;

        // Перечислимые настройки: опечатка здесь молча сломала бы клиент — валидируем явно.
        if (key == "ResultsLoadMode" && newValue is not ("full" or "paged" or "client"))
            return false;

        _settings[key] = existing with { Value = newValue };

        if (key is "SchemaCacheTTL" or "ForceRefresh" or "ShowSystemTables" or "DefaultSchema")
            _cache.Remove("DbSchema");

        return true;
    }

    private static bool ValidateType(string dataType, string value) => dataType switch
    {
        "bool" => bool.TryParse(value, out _),
        "int" => int.TryParse(value, out _),
        "string" => true,
        _ => true
    };
}
