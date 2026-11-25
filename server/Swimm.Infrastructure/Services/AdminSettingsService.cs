using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Настройки /Admin/Settings (thread-safe). Реестр ключей, тип, описание и дефолты — в коде
/// (<see cref="SeedDefaults"/>); ИЗМЕНЁННЫЕ значения — в БД, таблица <see cref="TableName"/>
/// (решение В7, docs/plans/azure-deploy-plan.md §10). Читается всё из памяти: база — только
/// источник при загрузке и место записи при правке.
///
/// Зачем БД. С В17 админка работает в другом процессе (локально, ProdAdmin): режим обслуживания,
/// включённый ею, прод видит только через общую базу. Перечитать велит мост кэша
/// (<see cref="CacheBridgeListener"/>: правка пишет строку → сброс метки таблицы уходит в канал);
/// на случай потерянного сообщения слушатель перечитывает и сам, раз в 5 минут.
///
/// Без хранилища (тесты, <c>new AdminSettingsService(cache)</c>) — чисто в памяти, как раньше.
/// </summary>
public class AdminSettingsService : ISettingsService, IReloadableSettings
{
    /// <summary>Таблица изменённых значений — её метку кэша слушает мост, чтобы перечитать.</summary>
    public const string TableName = "Sys_AdminSettings";

    /// <summary>База лежит — не долбим её на каждом GetValue: следующая попытка не раньше чем через…</summary>
    private static readonly TimeSpan LoadRetry = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, AdminSetting> _settings = new();
    private readonly Dictionary<string, AdminSetting> _defaults = new(StringComparer.Ordinal);
    private readonly IMemoryCache _cache;
    private readonly IAdminSettingsStore? _store;
    private readonly ILogger? _logger;

    private readonly object _loadGate = new();
    private volatile bool _loaded;
    private bool _loading;
    private DateTime _retryAfterUtc;

    /// <param name="development">
    /// Среда Development: от неё зависят дефолты отладочного толка (доля сверки кэша на
    /// попадании — там ощутимая, на проде 0).
    /// </param>
    /// <param name="store">Где лежат изменённые значения; null — только память (тесты).</param>
    public AdminSettingsService(IMemoryCache cache, bool development = false,
        IAdminSettingsStore? store = null, ILogger<AdminSettingsService>? logger = null)
    {
        _cache = cache;
        _store = store;
        _logger = logger;
        SeedDefaults(development);
        foreach (var (key, setting) in _settings) _defaults[key] = setting;
    }

    private void SeedDefaults(bool development)
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
            // Потолки групп (Ш3.0, 28.09.2026): аккаунты дешёвые — у каждой самообслуживаемой
            // вставки есть потолок. Правило и тексты — HubGroupQuotaRules.
            new(HubGroupQuotaRules.MaxAccountMembersKey, HubGroupQuotaRules.DefaultMaxAccountMembers.ToString(), "int", "livesite",
                "Сколько АККАУНТОВ (участники + заявки) может быть в одной группе, 1..5000. Действует " +
                "и на самозапись, и на добавление по email; на админа сайта тоже"),
            new(HubGroupQuotaRules.MaxManualSwimmersKey, HubGroupQuotaRules.DefaultMaxManualSwimmers.ToString(), "int", "livesite",
                "Сколько ПЛОВЦОВ владелец может добавить в состав группы руками, 1..5000. Пловцы из " +
                "подписки на клуб не в счёт"),
            new(HubGroupQuotaRules.MaxMembershipsPerUserKey, HubGroupQuotaRules.DefaultMaxMembershipsPerUser.ToString(), "int", "livesite",
                "В скольких группах может СОСТОЯТЬ один аккаунт (участник + заявки), 1..5000. " +
                "Группы, которыми он владеет или админит, не в счёт"),
            new(HubGroupQuotaRules.MaxPendingPerUserKey, HubGroupQuotaRules.DefaultMaxPendingPerUser.ToString(), "int", "livesite",
                "Сколько ЗАЯВОК на вступление аккаунт может держать одновременно, 1..5000"),
            new(HubGroupQuotaRules.SelfJoinEnabledKey, "true", "bool", "livesite",
                "Самозапись в группы: false — кнопка «Join» закрыта всем (рубильник на волну спама). " +
                "Добавление участника админом группы по email работает"),
            // Права на контент (docs/plans/content-permissions-plan.md): политика для тех, у кого
            // в /Admin/Users нет своего исключения. Дефолт true — сайт ведёт себя как до плана
            // (решение Влада 18.11.2026). Правило — ContentPermissionRules.
            new(ContentPermissionRules.DefaultCanAddPhotosKey, "true", "bool", "livesite",
                "Права на контент: может ли пользователь без своего исключения в /Admin/Users добавлять " +
                "ФОТО и альбомы (ссылки). Админу сайта всегда можно"),
            new(ContentPermissionRules.DefaultCanAddVideosKey, "true", "bool", "livesite",
                "Права на контент: может ли пользователь без своего исключения в /Admin/Users добавлять " +
                "ВИДЕО (ссылки). Админу сайта всегда можно"),
            new(ContentPermissionRules.DefaultCanPublishKey, "true", "bool", "livesite",
                "Права на контент: может ли пользователь без своего исключения в /Admin/Users показывать " +
                "медиа другим — заявки на публикацию в клуб/группу, галерея группы, ссылки к тренировкам. " +
                "false — «новым только после одобрения админом» (исключение «да» в /Admin/Users). " +
                "Уже опубликованное смена не трогает"),
            // Жалобы «Report» (Р62, 29.09.2026): правило и пределы — MediaReportRules.
            new(MediaReportRules.HideThresholdKey, MediaReportRules.DefaultHideThreshold.ToString(), "int", "livesite",
                "Жалобы на медиа: сколько ОТКРЫТЫХ жалоб от разных аккаунтов прячет медиа со всех витрин " +
                "(включая страницу группы) до решения админа сайта на /Admin/MediaReports, 1..100. " +
                "Действует на новые жалобы: уже спрятанное смена порога не возвращает"),
            // Дефолт perGroup (решение 11.09.2026, §6-6): иначе галочка «Public group» у группы
            // ни на что не влияла. Приватная = только для участников, остальным — заглушка.
            new(HubGroupVisibilityRules.SettingKey, HubGroupVisibilityRules.Default, "string", "livesite",
                "Видимость групп: perGroup — решает галочка «Public group» у группы (снята — группу " +
                "видят только участники, остальным страница «только для участников», в каталоге её нет); " +
                "public — все группы открыты всем, галочка не действует; private — все группы только для участников"),
            // Бегущая лента внизу /, /competitions, /groups (16.10.2026): пока имена и времена в
            // ней выдуманные, поэтому дефолт — выключена. Читает клиент через /api/client-config.
            new("RecordTickerEnabled", "false", "bool", "livesite",
                "Бегущая лента внизу главной, /competitions и /groups: true — показывать. Пока в ней " +
                "ВЫДУМАННЫЕ имена и времена (заглушка до реальных данных), поэтому на проде её нет и при true"),
            new("DiscoveryEnabled", "false", "bool", "admin",
                "Автозабор isr.org.il: true — фоновая проверка списка соревнований по расписанию"),
            new("DiscoveryIntervalHours", "24", "int", "admin",
                "Интервал фоновой проверки isr.org.il в часах (минимум 1). Список стартов меняется " +
                "редко — раз в сутки (В12); «последний запуск» хранится в базе (Sys_JobRuns), рестарт " +
                "API заново не проверяет. Работает только там, где включены фоновые расписания (ProdAdmin)"),
            new("StartListEnabled", "false", "bool", "admin",
                "Автозабор стартового протокола (docs/plans/start-list-plan.md): true — фоновый " +
                "проход добывает logligId будущих стартов и тянет их стартовые протоколы по расписанию"),
            new("StartListDaysAhead", "14", "int", "admin",
                "Окно вперёд (в днях) для автозабора стартового протокола: сколько дней до старта " +
                "считается «предстоящим» для догрузки деталей/протокола"),
            // П3 (prod-auto-pull-plan §4.3, Р-4): свой шаг, раньше был общий с DiscoveryIntervalHours.
            new("StartListIntervalHours", "3", "int", "admin",
                "Шаг обхода стартовых протоколов в часах (минимум 1). Это НЕ частота забора одного " +
                "старта: её решает правило по близости старта — 3–14 дней раз в сутки, ближайшие два дня " +
                "каждые 3 ч (посев уже есть — 6 ч), день старта каждые 3 ч до первого заплыва. Шаг больше " +
                "3 ч делает ближайшие старты реже. loglig-id дочитывается раз в сутки (первый тик дня)"),
            // П5 (prod-auto-pull-plan §4.4, Р-2): results-auto — результаты прошедших стартов.
            new(ResultsAutoModes.SettingKey, ResultsAutoModes.Off, "string", "admin",
                "Результаты прошедших стартов сами (results-auto): off — задача не работает; report — за " +
                "тик берёт одну прошедшую строку «Входящих» (не старше 7 дней, с loglig-id), разбирает как " +
                "кнопка пакетного затягивания, прогоняет гейты (🟢, не чемпионат, один день, соревнование " +
                "закончено, бассейн известен) и пишет строке вердикт «взяла бы сама» / «ждёт тебя» с причиной, " +
                "НЕ импортирует; import — пока то же, что report (импорт — этап П6). Идёт там же, где discovery"),
            new("ResultsAutoEnabled", "true", "bool", "admin",
                "Аварийный выключатель results-auto: false — задача не работает при любом ResultsAutoMode"),
            new("ResultsAutoIntervalHours", "1", "int", "admin",
                "Шаг тика results-auto в часах (минимум 1); за тик одна строка. Перепроверка строки — своя: " +
                "«ещё идёт» через 2 ч, сбой через 3 ч, «протокола нет» через 6 ч, остальное раз в сутки"),
            new("LogligStampOnImport", "true", "bool", "admin",
                "После импорта проставлять пловцам loglig-id из протокола соревнования " +
                "(на странице заплыва loglig имя — ссылка на карточку). Уже привязанных не " +
                "трогает, тёзок пропускает. Стоит запрос на заплыв — выключается, если сайт " +
                "лежит или импортов много подряд"),
            // Доклейка промежуточных сама после импорта (решение Влада 23.11.2026).
            new(Swimm.Application.Mapping.SplitAutoAttachRules.SettingKey, "true", "bool", "admin",
                "После импорта доклеивать промежуточные с loglig к строкам соревнования (тот же " +
                "сервис, что у кнопки «Промежуточные»: пишет только время этапов, строк не создаёт). " +
                "Идёт в фоне за импортом, по одному соревнованию; запрос на дисциплину (у чемпионата " +
                "~2.5 мин). Выключается, если сайт лежит или пачка большая — тогда кнопкой в шаге 9"),
            // Пол пловца с карточки loglig (Р125) — закрывает «Результаты без пола» без кликов.
            new(Swimm.Application.Mapping.LogligCardGenderRule.SettingKey, "true", "bool", "admin",
                "После импорта брать пол с карточки loglig тем пловцам соревнования, у кого он пуст " +
                "(смешанный заплыв не печатает пол в шапке). Только при подтверждённом loglig-id, " +
                "совпавших имени и годе рождения; уже проставленный пол не трогает. Серьёзные случаи " +
                "(изменился бы рекорд, место, медаль, очки, топ; личность не подтверждена заплывом) не пишет — " +
                "они ждут в /Admin/Health, «Пол с loglig ждёт ручной проверки». Идёт в фоне после игроков по " +
                "строкам, запрос на пловца с паузой 1 с, не больше 40 запросов на импорт. " +
                "Выключается, если сайт лежит, — тогда кнопкой «взять пол с карточек loglig» в /Admin/Health"),
            // Игрок loglig у строки (Р122, И-69) — данные для проверки «два игрока на одной карточке».
            new(Swimm.Application.Mapping.LogligRowPlayerRules.SettingKey, "true", "bool", "admin",
                "После импорта записывать каждой личной строке id игрока loglig со страницы её " +
                "дисциплины (совпадение по заплыву, дорожке и времени; неоднозначным не пишет). По " +
                "этим id /Admin/Health находит карточку, в которой слиты два человека. Идёт в фоне " +
                "после доклеек и штамповок всей пачки; ~2 с на дисциплину (лига ~40 с, чемпионат 5–9 мин). Выключается, " +
                "если сайт лежит или пачка большая — тогда CLI --stamp-row-players <compID>"),
            // Ночная сверка предложенных loglig-id (docs/loglig-id-plan.md, шаг 6). Джоб читал эти
            // ключи и раньше, но в реестре их не было — из админки было не выключить (§8.2 плана деплоя).
            new("LogligVerifyEnabled", "true", "bool", "admin",
                "Ночная сверка предложенных пользователями loglig-id: true — фоновый джоб раз в " +
                "LogligVerifyIntervalHours сверяет карточки loglig и переводит предложения в Verified / " +
                "Rejected. Джоб работает только там, где включены фоновые джобы (локальная админка, не прод)"),
            new("LogligVerifyIntervalHours", "24", "int", "admin",
                "Интервал ночной сверки loglig-id в часах (минимум 1)"),
            // Проверка источников рекордов по расписанию (records-freshness-plan §4, 16.11.2026).
            new("RecordsCheckEnabled", "true", "bool", "admin",
                "Проверка источников рекордов по расписанию: true — фоновый джоб раз в " +
                "RecordsCheckIntervalHours делает то же, что кнопка «Проверить все источники» на " +
                "/Admin/Import (скачать, построить дифф, записать журнал). Apply НЕ делает. Работает " +
                "только там, где включены фоновые расписания (ProdAdmin)"),
            new("RecordsCheckIntervalHours", "24", "int", "admin",
                "Интервал проверки источников рекордов в часах (минимум 1)"),
            // Сверка смены клуба пловца с loglig (Р98, §6 swimmer-club-change-plan, 18.11.2026).
            new("SwimmerClubCheckEnabled", "true", "bool", "admin",
                "Сверка кандидатов на смену клуба пловца с карточкой loglig: true — фоновый джоб " +
                "раз в SwimmerClubCheckIntervalHours (и сразу после импорта — только новых) открывает " +
                "карточки loglig (~1 в секунду, ответ кэшируется на сутки) и применяет смену, только " +
                "если loglig показывает новый клуб. Без джоба кандидаты ждут вечно. Работает только " +
                "там, где включены фоновые расписания (ProdAdmin)"),
            new("SwimmerClubCheckIntervalHours", "24", "int", "admin",
                "Интервал сверки смены клуба с loglig в часах (минимум 1)"),
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
            // Точность сброса кэша (docs/plans/cache-row-precision-plan.md §2.5, §4-6).
            new(CacheSettings.RowPrecision, CacheSettings.DefaultRowPrecision ? "true" : "false", "bool", "livesite",
                "Кэш: сужение до строк (по умолчанию вкл.). true — страница одной группы или клуба " +
                "зависит от своих строк и падает только от их правки; false — от всей таблицы, как " +
                "раньше. Аварийный рычаг: всплыл недосброс (подозрения сверки на /Admin/Cache) — выключить"),
            new(CacheSettings.ColumnPrecision, CacheSettings.DefaultColumnPrecision ? "true" : "false", "bool", "livesite",
                "Кэш: точность по служебным колонкам — loglig-привязка пловца, штамп проверки качества, " +
                "объединённые места, отметки «обновлено» (по умолчанию вкл.). true — правка только таких " +
                "колонок роняет лишь записи, которые их читают (season-best и витрины loglig-правка больше не " +
                "роняет); false — всю таблицу, как обычная правка. Аварийный рычаг, как и сужение до строк"),
            new(CacheSettings.HitVerifyPercent, CacheSettings.DefaultHitVerifyPercent(development).ToString(), "int", "livesite",
                "Кэш: сверка на попадании, % (0–100). Попадание в запись, суженную до строк (или читавшую " +
                "служебные колонки, пока включена точность по ним), с этой вероятностью строится заново " +
                "мимо кэша и сравнивается; расхождение — «подозрение на недосброс» в журнале /Admin/Cache " +
                "и сброс записи. В Development 20, на проде 0"),
        };

        foreach (var s in defaults)
            _settings.TryAdd(s.Key, s);
    }

    public IReadOnlyList<AdminSetting> GetAll()
    {
        EnsureLoaded();
        return _settings.Values.OrderBy(s => s.Scope).ThenBy(s => s.Key).ToList();
    }

    public AdminSetting? Get(string key)
    {
        EnsureLoaded();
        return _settings.GetValueOrDefault(key);
    }

    public T GetValue<T>(string key, T fallback)
    {
        EnsureLoaded();
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
        EnsureLoaded();
        if (!_settings.TryGetValue(key, out var existing))
            return false;

        if (!IsValid(existing, newValue))
            return false;

        // Сначала база, потом память: не записалось — значит, не изменилось нигде, и другой
        // процесс (прод при правке из ProdAdmin) не разойдётся с этим.
        if (_store is not null)
        {
            try { _store.Save(key, newValue); }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Настройка {Key} не записана в БД — значение не изменено.", key);
                return false;
            }
        }

        Apply(key, newValue);
        return true;
    }

    /// <summary>
    /// Перечитать изменённые значения из БД (мост кэша, страховка раз в 5 минут). Ключа в базе
    /// нет — возвращается дефолт: удалённая строка значит «вернуть как было».
    /// </summary>
    public void Reload()
    {
        if (_store is null) return;
        var stored = _store.LoadAll();
        foreach (var (key, def) in _defaults)
        {
            var value = def.Value;
            if (stored.TryGetValue(key, out var v))
            {
                if (IsValid(def, v)) value = v;
                else _logger?.LogWarning("Настройка {Key}: в БД недопустимое значение «{Value}» — действует дефолт.", key, v);
            }
            Apply(key, value);
        }
        _loaded = true;
    }

    /// <summary>
    /// Ленивая первая загрузка: на первом обращении, а не в конструкторе, — синглтон собирается и
    /// там, где базы нет (или она ещё лежит). Не загрузилось — работаем на дефолтах и пробуем снова
    /// через <see cref="LoadRetry"/>.
    /// </summary>
    private void EnsureLoaded()
    {
        if (_loaded || _store is null || DateTime.UtcNow < _retryAfterUtc) return;
        lock (_loadGate)
        {
            // Повторный вход тем же потоком: загрузка ходит в БД через EF, а EF может спросить
            // настройку (точность сброса кэша) — отвечаем дефолтом, а не рекурсией.
            if (_loaded || _loading) return;
            _loading = true;
            try { Reload(); }
            catch (Exception ex)
            {
                _retryAfterUtc = DateTime.UtcNow + LoadRetry;
                _logger?.LogWarning(ex, "Настройки не загрузились из БД — пока действуют дефолты, повтор через {Seconds} с.",
                    (int)LoadRetry.TotalSeconds);
            }
            finally { _loading = false; }
        }
    }

    private void Apply(string key, string value)
    {
        if (!_settings.TryGetValue(key, out var existing) || existing.Value == value) return;
        _settings[key] = existing with { Value = value };

        if (key is "SchemaCacheTTL" or "ForceRefresh" or "ShowSystemTables" or "DefaultSchema")
            _cache.Remove("DbSchema");
    }

    /// <summary>Тип и допустимые значения — одни правила для правки из админки и для загрузки из БД.</summary>
    private static bool IsValid(AdminSetting existing, string newValue)
    {
        var key = existing.Key;
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
        if (HubGroupQuotaRules.LimitKeys.Contains(key)
            && !HubGroupQuotaRules.IsValidLimit(int.Parse(newValue)))
            return false;
        if (key == CacheSettings.HitVerifyPercent && int.Parse(newValue) is < 0 or > 100)
            return false;
        if (key == MediaReportRules.HideThresholdKey
            && int.Parse(newValue) is < MediaReportRules.MinHideThreshold or > MediaReportRules.MaxHideThreshold)
            return false;

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
