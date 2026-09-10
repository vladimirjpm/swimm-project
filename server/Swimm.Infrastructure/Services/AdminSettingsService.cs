using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

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
            // Дефолты групп = рабочий режим (решение 10.09.2026): значения живут в памяти и после
            // рестарта возвращаются сюда. Исключения по конкретным людям — в /Admin/Users (в БД).
            new("HubGroupCreationPolicy", "any", "string", "livesite",
                "Кто создаёт группы (SwimHub): admin — только админ; coach — админ и тренеры; " +
                "any — любой вошедший пользователь (в пределах лимита)"),
            new("HubGroupMaxPerUser", "3", "int", "livesite",
                "Сколько групп может ВЛАДЕТЬ обычный пользователь (официальные тоже в счёт). " +
                "Персональный лимит в /Admin/Users важнее; на админа не действует"),
            new("HubGroupMaxPerCoach", "3", "int", "livesite",
                "Сколько групп может ВЛАДЕТЬ пользователь с ролью Coach. Персональный лимит в " +
                "/Admin/Users важнее; на админа не действует"),
            // Лимиты избранного (решение 10.09.2026): сердечко должно что-то выделять, а список
            // «весь клуб» — это группа. Правило и тексты — FavoritesRules.
            new(FavoritesRules.MaxSwimmersKey, FavoritesRules.DefaultMaxSwimmers.ToString(), "int", "livesite",
                "Сколько ПЛОВЦОВ можно держать в избранном (звезда «это я» тоже в счёт), 1..200. " +
                "Кто уже выше лимита, ничего не теряет — только не может добавить"),
            new(FavoritesRules.MaxClubsKey, FavoritesRules.DefaultMaxClubs.ToString(), "int", "livesite",
                "Сколько КЛУБОВ можно держать в избранном, 1..200. Избранный клуб в пловцов не " +
                "разворачивается и в лимит пловцов не идёт"),
            new("HubGroupVisibility", "public", "string", "livesite",
                "Видимость групп: public — все видны всем; private — все скрыты; " +
                "perGroup — решает флаг IsPublic у конкретной группы"),
            new("DiscoveryEnabled", "false", "bool", "admin",
                "Автозабор isr.org.il: true — фоновая проверка списка соревнований по расписанию"),
            new("DiscoveryIntervalHours", "12", "int", "admin",
                "Интервал фоновой проверки isr.org.il в часах (минимум 1)"),
            new("StartListEnabled", "false", "bool", "admin",
                "Автозабор стартового протокола (docs/plans/start-list-plan.md): true — фоновый " +
                "проход добывает logligId будущих стартов и тянет их стартовые протоколы по расписанию"),
            new("StartListDaysAhead", "14", "int", "admin",
                "Окно вперёд (в днях) для автозабора стартового протокола: сколько дней до старта " +
                "считается «предстоящим» для догрузки деталей/протокола"),
            new("LogligStampOnImport", "true", "bool", "admin",
                "После импорта проставлять пловцам loglig-id из протокола соревнования " +
                "(на странице заплыва loglig имя — ссылка на карточку). Уже привязанных не " +
                "трогает, тёзок пропускает. Стоит запрос на заплыв — выключается, если сайт " +
                "лежит или импортов много подряд"),
            new("DebugDetails", "false", "bool", "both",
                "Общий тумблер отладочных подробностей на витринах. Пока выключен, ни одна " +
                "опция из Sys_DebugOptions не действует — одним движением гасится всё. " +
                "Сами опции — секция «Debug details» ниже на этой странице"),
            new("RecordAgeAxis", "calendar", "string", "both",
                "Ось возраста ДЛЯ СВЕРКИ С РЕКОРДАМИ: calendar — по году заплыва, как ведёт " +
                "справочник федерация (год заплыва минус год рождения); season — по году " +
                "окончания сезона, как считаем возраст у себя. Оси расходятся только с " +
                "сентября по декабрь. Возраст на страницах (ростер, зачёт, категории) " +
                "настройка НЕ трогает — он всегда сезонный (docs/data-integrity.md §13)"),
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
        if (key == "HubGroupCreationPolicy" && newValue is not ("admin" or "coach" or "any"))
            return false;
        if (key == "HubGroupVisibility" && newValue is not ("public" or "private" or "perGroup"))
            return false;
        if (key == "RecordAgeAxis" && newValue is not ("calendar" or "season"))
            return false;
        if (key is "HubGroupMaxPerUser" or "HubGroupMaxPerCoach"
            && int.Parse(newValue) is < 0 or > HubGroupCreationRules.MaxLimit)
            return false;
        if (key is FavoritesRules.MaxSwimmersKey or FavoritesRules.MaxClubsKey
            && !FavoritesRules.IsValidLimit(int.Parse(newValue)))
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
