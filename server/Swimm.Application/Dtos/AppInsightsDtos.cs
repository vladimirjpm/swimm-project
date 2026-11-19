namespace Swimm.Application.Dtos;

// Контракт чтения Application Insights для админки (docs/plans/application-insights-plan.md, Б1):
// страница /Admin/AppInsights (GetOverviewAsync) и блок 10 дашборда (GetSummaryAsync).
// Тоны — те же слова, что у /Admin/Jobs: ok | warning | danger | info | neutral.

/// <summary>Состояние подключения к рабочей области Log Analytics (§4.1 плана).</summary>
public static class AppInsightsStates
{
    /// <summary>Нет ключей <c>AppInsights:WorkspaceId</c> / <c>AppInsights:ResourceId</c>.</summary>
    public const string NotConfigured = "not-configured";

    /// <summary><c>az login</c> не сделан, просрочен или <c>az</c> не установлен.</summary>
    public const string NoLogin = "no-login";

    /// <summary>Вход есть, но запрос не прошёл: таймаут, нет прав, неверный id, ошибка HTTP/KQL от Azure.</summary>
    public const string Failed = "failed";

    /// <summary>
    /// Ответ пустой, неполный или не разбирается — так бывает первые минуты после выкатки сайта или
    /// перезапуска ProdAdmin, пока данные не дошли (17.11, 19.11). Не ошибка: «No data yet», нейтрально
    /// (решение Влада 19.11.2026).
    /// </summary>
    public const string NoData = "no-data";

    /// <summary>Данные прочитаны.</summary>
    public const string Ok = "ok";
}

/// <summary>Чья телеметрия: сайт или локальная админка ProdAdmin (А2.3 — имя роли).</summary>
public static class AppInsightsRoles
{
    public const string Web = "web";
    public const string ProdAdmin = "prodadmin";

    /// <summary>Имя роли в телеметрии (<c>AppRoleName</c>) — как его ставит <c>TelemetryRoleName</c> в API.</summary>
    public const string WebRoleName = "swimhub-web";
    public const string ProdAdminRoleName = "swimhub-prodadmin";

    /// <summary>Ключ из адреса → имя роли; чужое значение — null (контроллер отвечает 400).</summary>
    public static string? RoleName(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        null or "" or Web => WebRoleName,
        ProdAdmin => ProdAdminRoleName,
        _ => null,
    };

    /// <summary>Нормализованный ключ (web | prodadmin) или null.</summary>
    public static string? Normalize(string? key) => RoleName(key) switch
    {
        WebRoleName => Web,
        ProdAdminRoleName => ProdAdmin,
        _ => null,
    };
}

/// <summary>Период страницы: 1 ч / 24 ч / 7 дней.</summary>
public static class AppInsightsRanges
{
    public const string Hour = "1h";
    public const string Day = "24h";
    public const string Week = "7d";

    /// <summary>Ключ из адреса → длительность; чужое значение — null (контроллер отвечает 400).</summary>
    public static TimeSpan? Parse(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        null or "" or Day => TimeSpan.FromHours(24),
        Hour => TimeSpan.FromHours(1),
        Week => TimeSpan.FromDays(7),
        _ => null,
    };

    /// <summary>Нормализованный ключ (1h | 24h | 7d) или null.</summary>
    public static string? Normalize(string? key) => Parse(key) is { } span ? Key(span) : null;

    public static string Key(TimeSpan span) => span.TotalHours switch
    {
        <= 1 => Hour,
        <= 24 => Day,
        _ => Week,
    };

    /// <summary>Подпись периода для плиток: «за 1 ч», «за 24 ч», «за 7 дней».</summary>
    public static string Title(TimeSpan span) => Key(span) switch
    {
        Hour => "за 1 ч",
        Day => "за 24 ч",
        _ => "за 7 дней",
    };
}

/// <summary>
/// Карточка подключения. <paramref name="Hint"/> — что сделать (команда <c>az login</c> для копирования
/// или ключи для <c>appsettings.*.Local.json</c>); у рабочего состояния null.
/// </summary>
public sealed record AppInsightsConnection(string State, string Title, string Text, string? Hint);

/// <summary>
/// Шлёт ли телеметрию ЭТОТ процесс (строка подключения задана и не выключена) и под каким именем роли.
/// Заполняет API-слой из <c>HostingMode.TelemetryEnabled</c> и <c>TelemetryRoleName</c>.
/// </summary>
public sealed record AppInsightsProcessInfo(bool Sending, string RoleName);

/// <summary>Ссылки в портал Azure — собираются во время работы из <c>AppInsights:ResourceId</c>, в репо их нет.</summary>
public sealed record AppInsightsLinks(string Overview, string Failures, string Performance, string LiveMetrics, string Logs);

/// <summary>
/// Сырые итоги одного запроса KQL (<c>AppInsightsQueries.Totals</c>) — вход сборщика плиток.
/// Перцентили — null, когда за период нет ни одного запроса / вызова.
/// </summary>
/// <param name="Errors5xx">Ответы с кодом 5xx. <c>Success=false</c> не годится: он стоит и у 404.</param>
/// <param name="SlowOperations">Операций (маршрутов) с p95 выше <c>AppInsightsThresholds.SlowOperationMs</c>.</param>
/// <param name="BackgroundFailures">Исключения и логи Error+ роли вне запроса (пустой <c>OperationId</c>).</param>
/// <param name="AdminFailures">Исключения и логи Error+ роли ProdAdmin — как правило оповещения <c>swimhub-bg-errors</c>.</param>
/// <param name="LastTelemetryAt">Последняя запись роли за 7 дней (метрики идут раз в минуту и без трафика); null — нет.</param>
/// <param name="IngestedTodayMb">Принято рабочей областью (все роли) с последнего сброса дневного лимита, МБ.</param>
/// <param name="P50Ms">Перцентили и <paramref name="SlowOperations"/> — по ВНЕШНЕМУ тёплому трафику: посетители и
///   роботы, без наших запросов (прогрев, мониторинг) и без первых 10 минут после старта процесса
///   (решение Влада 19.11.2026). Тег <c>swimm.source</c>/<c>swimm.cold</c> пишет сайт.</param>
/// <param name="Visitors">Запросов от посетителей (без тега источника — тоже сюда: строки до 19.11).</param>
/// <param name="Bots">Запросов от роботов (User-Agent).</param>
/// <param name="Internal">Наших служебных запросов: прогрев после старта, внешний мониторинг.</param>
/// <param name="Cold">Запросов в первые 10 минут после старта процесса — в перцентили не входят.</param>
/// <param name="Starts">Стартов процесса сайта за период (разных <c>AppRoleInstance</c>): деплои и перезапуски.</param>
public sealed record AppInsightsTotals(
    long Requests,
    long Errors5xx,
    double? P50Ms,
    double? P95Ms,
    long SlowOperations,
    long DbCalls,
    double? DbP95Ms,
    long DbFailed,
    long Exceptions,
    long BackgroundFailures,
    long AdminFailures,
    DateTime? LastTelemetryAt,
    double IngestedTodayMb,
    long Visitors = 0,
    long Bots = 0,
    long Internal = 0,
    long Cold = 0,
    long Starts = 0);

/// <summary>Частое исключение: тип, начало сообщения (последнего), сколько, когда последний раз, операция («» — фон).</summary>
public sealed record AppInsightsExceptionRow(string Type, string Message, long Count, DateTime LastAt, string Operation);

/// <summary>Операция (маршрут, числа в пути заменены на <c>{id}</c>): сколько, p50, p95, ответов 5xx.</summary>
public sealed record AppInsightsOperationRow(string Operation, long Count, double P50Ms, double P95Ms, long Errors5xx);

/// <summary>Ответы с ошибкой (код 400+) по маршруту и коду. <c>ServerError</c> — 5xx, они идут первыми.</summary>
public sealed record AppInsightsErrorRow(string Operation, int Code, long Count, DateTime LastAt, bool ServerError);

/// <summary>Сбой вне запроса: <c>Kind</c> — exception | log; <c>Source</c> — тип исключения или категория лога.</summary>
public sealed record AppInsightsBackgroundRow(string Kind, string Source, string Message, long Count, DateTime LastAt);

/// <summary>Всё прочитанное для страницы за период — вход <c>AppInsightsOverviewBuilder.Overview</c>.</summary>
public sealed record AppInsightsOverviewData(
    AppInsightsTotals Totals,
    IReadOnlyList<AppInsightsExceptionRow> Exceptions,
    IReadOnlyList<AppInsightsOperationRow> SlowOperations,
    IReadOnlyList<AppInsightsErrorRow> Errors,
    IReadOnlyList<AppInsightsBackgroundRow> Background);

/// <summary>Метрика блока дашборда: подпись, значение, тон.</summary>
public sealed record AppInsightsMetric(string Key, string Label, string Value, string Tone);

/// <summary>
/// Страница /Admin/AppInsights: <c>GET /api/admin/appinsights/overview?range=&amp;role=</c>.
/// При состоянии не <c>ok</c> — плитки и таблицы пустые, смысл несёт <see cref="Connection"/>.
/// </summary>
/// <param name="Range">1h | 24h | 7d.</param>
/// <param name="Role">web | prodadmin.</param>
/// <param name="RoleName">swimhub-web | swimhub-prodadmin.</param>
/// <param name="GeneratedAt">Когда прочитано из Azure (UTC) — ответ кэшируется на 2 минуты.</param>
/// <param name="Tone">Здоровье роли за период: ok | warning | danger; neutral — данных нет (не настроено и т. п.).</param>
/// <param name="Reasons">Почему такой тон — строки для подсказки.</param>
/// <param name="LastTelemetryAgo">«3 мин назад» / «нет за 7 дней»; null — данных нет.</param>
public sealed record AppInsightsOverview(
    AppInsightsConnection Connection,
    AppInsightsProcessInfo Process,
    string Range,
    string Role,
    string RoleName,
    DateTime GeneratedAt,
    string Tone,
    IReadOnlyList<string> Reasons,
    DateTime? LastTelemetryAt,
    string? LastTelemetryAgo,
    IReadOnlyList<OverviewTile> Tiles,
    IReadOnlyList<AppInsightsExceptionRow> Exceptions,
    IReadOnlyList<AppInsightsOperationRow> SlowOperations,
    IReadOnlyList<AppInsightsErrorRow> Errors,
    IReadOnlyList<AppInsightsBackgroundRow> Background,
    AppInsightsLinks? Links);

/// <summary>
/// Блок 10 «App Insights» дашборда: <c>GET /api/admin/appinsights/summary</c>. Сайт за 24 ч.
/// </summary>
/// <param name="Tone">Тон блока (§4 Б3): danger | warning | ok; neutral — не настроено / нет входа / не прочитано.</param>
/// <param name="Value">Главная цифра — ошибок сервера (5xx) за 24 ч; при нуле «✓»; без данных «—».</param>
/// <param name="ValueTone">Тон главной цифры: ok при нуле.</param>
/// <param name="Caption">«N запросов · p95 320 мс · телеметрия 2 мин назад» или текст состояния.</param>
public sealed record AppInsightsSummary(
    AppInsightsConnection Connection,
    DateTime GeneratedAt,
    string Tone,
    IReadOnlyList<string> Reasons,
    string Value,
    string ValueTone,
    string Caption,
    long? Errors5xx,
    long? Requests,
    double? P95Ms,
    DateTime? LastTelemetryAt,
    IReadOnlyList<AppInsightsMetric> Metrics);
