using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Swimm.API.Services;

/// <summary>
/// In-memory хранилище настроек (thread-safe).
/// Значения сбрасываются при перезапуске сервера.
/// 
/// Колонки:
///   Key         — уникальное имя параметра
///   Value       — текущее значение (строка)
///   DataType    — тип: bool, int, string
///   Scope       — к чему относится: admin | livesite | both
///   Description — человекочитаемое описание
/// </summary>
public class AdminSettingsService
{
    private readonly ConcurrentDictionary<string, AdminSetting> _settings = new();
    private readonly IMemoryCache _cache;

    public AdminSettingsService(IMemoryCache cache)
    {
        _cache = cache;
        SeedDefaults();
    }

    /// <summary>
    /// Начальные параметры с описаниями.
    /// </summary>
    private void SeedDefaults()
    {
        var defaults = new AdminSetting[]
        {
            // Maintenance mode: если true — публичная часть сайта закрыта,
            // показывается заглушка. Админ-панель и /auth продолжают работать.
            new("MaintenanceMode", "false", "bool", "livesite",
                "Режим обслуживания: true — сайт закрыт для всех кроме админов, показывается заглушка"),

            // Кеш схемы БД: сколько минут хранить результат /api/admin/db-schema в памяти.
            new("SchemaCacheTTL", "10", "int", "admin",
                "Время жизни кеша схемы БД в минутах. После истечения — схема перечитывается из SQL Server"),

            // Принудительное обновление: если true — кеш сбрасывается при КАЖДОМ запросе к db-schema.
            new("ForceRefresh", "false", "bool", "admin",
                "Если true — кеш схемы сбрасывается при каждом запросе (отключает кеширование)"),

            // Системные таблицы: SQL Server помечает внутренние таблицы флагом is_ms_shipped = 1.
            new("ShowSystemTables", "false", "bool", "admin",
                "Показывать системные таблицы SQL Server (is_ms_shipped = 1) в схеме БД"),

            // Схема по умолчанию: фильтр TABLE_SCHEMA в запросе таблиц.
            // Admin: фильтрует таблицы в db.html.
            // LiveSite: можно использовать для выбора схемы в публичных запросах.
            new("DefaultSchema", "dbo", "string", "both",
                "SQL-схема для фильтрации таблиц. Используется в admin (db.html) и может использоваться в публичных запросах"),
        };

        foreach (var s in defaults)
            _settings.TryAdd(s.Key, s);
    }

    public IReadOnlyList<AdminSetting> GetAll()
        => _settings.Values.OrderBy(s => s.Scope).ThenBy(s => s.Key).ToList();

    public AdminSetting? Get(string key)
        => _settings.GetValueOrDefault(key);

    /// <summary>
    /// Получить типизированное значение с fallback.
    /// </summary>
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

    /// <summary>
    /// Обновить значение. Возвращает false если ключ не найден или тип не совпадает.
    /// При изменении параметров схемы — автоматически сбрасывает кеш.
    /// </summary>
    public bool Update(string key, string newValue)
    {
        if (!_settings.TryGetValue(key, out var existing))
            return false;

        if (!ValidateType(existing.DataType, newValue))
            return false;

        _settings[key] = existing with { Value = newValue };

        // Сброс кеша схемы при изменении связанных параметров
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

/// <summary>
/// Один параметр настроек.
/// Scope: admin — только админ-панель, livesite — только публичная часть, both — и то и другое.
/// </summary>
public record AdminSetting(
    string Key,
    string Value,
    string DataType,
    string Scope,
    string Description
);
