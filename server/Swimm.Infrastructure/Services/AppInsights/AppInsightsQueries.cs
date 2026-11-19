using System.Globalization;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.Infrastructure.Services.AppInsights;

/// <summary>Один запрос KQL пакета: имя (ключ результата) и текст.</summary>
internal sealed record AppInsightsQuery(string Name, string Kql);

/// <summary>
/// Весь KQL страницы /Admin/AppInsights и блока дашборда — в одном файле (план Б1). Каждый запрос
/// сверен на живых данных `swimhub-logs` 17.11.2026 (REST api.loganalytics.io).
///
/// Что важно о данных (живая проверка А2/А3):
/// <list type="bullet">
/// <item>таблицы — <c>AppRequests</c>, <c>AppDependencies</c>, <c>AppExceptions</c>, <c>AppTraces</c>,
///   <c>AppMetrics</c>, <c>Usage</c>; роль — <c>AppRoleName</c>;</item>
/// <item><c>Success=false</c> стоит и у 404 — ошибки сервера только по <c>ResultCode</c> 5xx;</item>
/// <item>фон (вне запроса) — пустой <c>OperationId</c>; <c>OperationName</c> пуст и у исключений из
///   запросов, по нему фон не отличить (правило оповещения <c>swimhub-bg-errors</c>, А4);</item>
/// <item>страницы SPA приходят с конкретным адресом в <c>Name</c> (<c>GET /swimmers/8318</c>), маршруты
///   API — шаблоном (<c>GET api/clubs/{id:int}/overview</c>): числа в пути сводим к <c>{id}</c>;</item>
/// <item>метрики (<c>AppMetrics</c>) сайт шлёт раз в минуту и без трафика — по ним видна тишина;</item>
/// <item><c>Usage.Quantity</c> — МБ, строки почасовые (<c>TimeGenerated</c> — начало часа).</item>
/// </list>
/// Выборка выключена (SamplingRatio=1, А2), поэтому считаем строки, а не <c>ItemCount</c>.
/// Подстановки — только из белых списков (имя роли, длительность числом): ввод пользователя в KQL не попадает.
/// </summary>
internal static class AppInsightsQueries
{
    public const string TotalsName = "totals";
    public const string ExceptionsName = "exceptions";
    public const string SlowName = "slow";
    public const string ErrorsName = "errors";
    public const string BackgroundName = "background";

    /// <summary>Сколько строк в таблицах страницы.</summary>
    public const int TopExceptions = 10;
    public const int TopSlow = 10;
    public const int TopErrors = 15;
    public const int TopBackground = 15;

    /// <summary>
    /// Теги сайта на строке запроса (<c>Swimm.API/Telemetry</c>: RequestSource, ColdStart): кто прислал
    /// (visitor | bot | warmup | uptime) и «холодный» ли — первые 10 минут после старта процесса.
    /// Строки до 19.11.2026 тегов не имеют — считаются посетителями и тёплыми.
    /// </summary>
    internal const string SourceTag = "swimm.source";
    internal const string ColdTag = "swimm.cold";

    /// <summary>Фильтр «внешний тёплый трафик» для перцентилей и медленных операций (нужны колонки Src, Cold).</summary>
    internal const string ExternalWarm = "Src !in ('warmup', 'uptime') and not(Cold)";

    /// <summary>Окно запроса для API (timespan): самый длинный период — 7 дней «последней телеметрии» с запасом.</summary>
    public static readonly TimeSpan QueryWindow = TimeSpan.FromDays(8);

    /// <summary>Пять запросов страницы — одним пакетом (один HTTP-вызов, лимит частоты не тратится).</summary>
    public static IReadOnlyList<AppInsightsQuery> Overview(string roleName, TimeSpan range, int capResetHourUtc) =>
    [
        new(TotalsName, Totals(roleName, range, capResetHourUtc)),
        new(ExceptionsName, Exceptions(roleName, range)),
        new(SlowName, SlowOperations(roleName, range)),
        new(ErrorsName, Errors(roleName, range)),
        new(BackgroundName, Background(roleName, range)),
    ];

    /// <summary>Блок дашборда — один запрос: итоги сайта за 24 ч.</summary>
    public static IReadOnlyList<AppInsightsQuery> Summary(int capResetHourUtc) =>
        [new(TotalsName, Totals(AppInsightsRoles.WebRoleName, TimeSpan.FromHours(24), capResetHourUtc))];

    /// <summary>
    /// Одна строка итогов: запросы, 5xx, p50/p95, медленные операции, Postgres, исключения, фон, ошибки
    /// ProdAdmin (как правило <c>swimhub-bg-errors</c>), последняя телеметрия роли за 7 дней, приём с
    /// последнего сброса дневного лимита. <c>summarize</c> без <c>by</c> отдаёт строку и на пустых данных
    /// (счётчики 0, перцентили null) — соединение по <c>k=1</c> всегда даёт ровно одну строку.
    /// </summary>
    public static string Totals(string roleName, TimeSpan range, int capResetHourUtc) => $$"""
        let role = '{{Role(roleName)}}';
        let span = {{Span(range)}};
        let slowMs = {{AppInsightsThresholds.SlowOperationMs.ToString(CultureInfo.InvariantCulture)}};
        let capResetHour = {{Math.Clamp(capResetHourUtc, 0, 23)}}h;
        let req = AppRequests | where TimeGenerated > ago(span) and AppRoleName == role
          | extend Src=tostring(Properties['{{SourceTag}}']), Cold=tostring(Properties['{{ColdTag}}']) == 'true';
        let ext = req | where {{ExternalWarm}};
        let r = req | summarize Requests=count(), Errors5xx=countif(toint(ResultCode) >= 500),
            Visitors=countif(Src == 'visitor' or isempty(Src)), Bots=countif(Src == 'bot'),
            Internal=countif(Src in ('warmup', 'uptime')), Cold=countif(Cold), Starts=dcount(AppRoleInstance) | extend k=1;
        let pc = ext | summarize P50Ms=percentile(DurationMs, 50), P95Ms=percentile(DurationMs, 95) | extend k=1;
        let so = ext | extend Op=replace_regex(Name, @'/\d+', '/{id}') | summarize p95=percentile(DurationMs, 95) by Op | summarize SlowOperations=countif(p95 > slowMs) | extend k=1;
        let d = AppDependencies | where TimeGenerated > ago(span) and AppRoleName == role and DependencyType == 'postgresql' | summarize DbCalls=count(), DbP95Ms=percentile(DurationMs, 95), DbFailed=countif(Success == false) | extend k=1;
        let e = AppExceptions | where TimeGenerated > ago(span) and AppRoleName == role | summarize Exceptions=count() | extend k=1;
        let bg = union AppExceptions, (AppTraces | where SeverityLevel >= 3) | where TimeGenerated > ago(span) and AppRoleName == role and isempty(OperationId) | summarize BackgroundFailures=count() | extend k=1;
        let adm = union AppExceptions, (AppTraces | where SeverityLevel >= 3) | where TimeGenerated > ago(span) and AppRoleName == '{{AppInsightsRoles.ProdAdminRoleName}}' | summarize AdminFailures=count() | extend k=1;
        let l = union AppRequests, AppDependencies, AppExceptions, AppTraces, AppMetrics | where TimeGenerated > ago(7d) and AppRoleName == role | summarize LastTelemetryAt=max(TimeGenerated) | extend k=1;
        let resetAt = iff(now() >= startofday(now()) + capResetHour, startofday(now()) + capResetHour, startofday(now()) + capResetHour - 1d);
        let u = Usage | where TimeGenerated >= resetAt and IsBillable == true | summarize IngestedTodayMb=sum(Quantity) | extend k=1;
        r | join kind=inner pc on k | join kind=inner so on k | join kind=inner d on k | join kind=inner e on k | join kind=inner bg on k
          | join kind=inner adm on k | join kind=inner l on k | join kind=inner u on k
        | project Requests, Errors5xx, P50Ms, P95Ms, SlowOperations, DbCalls, DbP95Ms, DbFailed, Exceptions, BackgroundFailures, AdminFailures, LastTelemetryAt, IngestedTodayMb, Visitors, Bots, Internal, Cold, Starts
        """;

    /// <summary>
    /// Частые исключения: группа — тип + место броска (<c>ProblemId</c>), сообщение — последнее.
    /// Операция — имя запроса по <c>OperationId</c> (у самого исключения <c>OperationName</c> пуст); «» — фон.
    /// </summary>
    public static string Exceptions(string roleName, TimeSpan range) => $$"""
        let role = '{{Role(roleName)}}';
        let span = {{Span(range)}};
        let ops = AppRequests | where TimeGenerated > ago(span) and AppRoleName == role | summarize ReqName=take_any(Name) by OperationId;
        AppExceptions | where TimeGenerated > ago(span) and AppRoleName == role
        | join kind=leftouter ops on OperationId
        | extend Op = iff(isempty(OperationId), '', replace_regex(coalesce(ReqName, ''), @'/\d+', '/{id}'))
        | summarize arg_max(TimeGenerated, OuterMessage, Op), Count=count() by ExceptionType, ProblemId
        | order by Count desc, TimeGenerated desc | take {{TopExceptions}}
        | project ExceptionType, Message=substring(OuterMessage, 0, 300), Count, LastAt=TimeGenerated, Operation=Op
        """;

    /// <summary>
    /// Самые медленные операции по p95 (числа в пути — <c>{id}</c>) — внешний тёплый трафик, как p95 в итогах
    /// (<see cref="ExternalWarm"/>): без прогрева, мониторинга и первых 10 минут после старта.
    /// </summary>
    public static string SlowOperations(string roleName, TimeSpan range) => $$"""
        let role = '{{Role(roleName)}}';
        let span = {{Span(range)}};
        AppRequests | where TimeGenerated > ago(span) and AppRoleName == role
        | extend Src=tostring(Properties['{{SourceTag}}']), Cold=tostring(Properties['{{ColdTag}}']) == 'true'
        | where {{ExternalWarm}}
        | extend Op = replace_regex(Name, @'/\d+', '/{id}')
        | summarize Count=count(), P50Ms=percentile(DurationMs, 50), P95Ms=percentile(DurationMs, 95), Errors5xx=countif(toint(ResultCode) >= 500) by Op
        | order by P95Ms desc | take {{TopSlow}}
        | project Operation=Op, Count, P50Ms, P95Ms, Errors5xx
        """;

    /// <summary>Ответы с кодом 400+ по маршруту и коду; 5xx первыми.</summary>
    public static string Errors(string roleName, TimeSpan range) => $$"""
        let role = '{{Role(roleName)}}';
        let span = {{Span(range)}};
        AppRequests | where TimeGenerated > ago(span) and AppRoleName == role and toint(ResultCode) >= 400
        | extend Op = replace_regex(Name, @'/\d+', '/{id}'), Code = toint(ResultCode)
        | summarize Count=count(), LastAt=max(TimeGenerated) by Op, Code
        | extend ServerError = Code >= 500
        | order by ServerError desc, Count desc | take {{TopErrors}}
        | project Operation=Op, Code, Count, LastAt, ServerError
        """;

    /// <summary>
    /// Сбои вне запроса (фоновые службы): исключения и логи Error+ с пустым <c>OperationId</c>.
    /// Группа — вид + тип исключения / категория лога, сообщение — последнее.
    /// </summary>
    public static string Background(string roleName, TimeSpan range) => $$"""
        let role = '{{Role(roleName)}}';
        let span = {{Span(range)}};
        union
          (AppExceptions | project TimeGenerated, AppRoleName, OperationId, Kind='exception', Source=ExceptionType, Message=OuterMessage),
          (AppTraces | where SeverityLevel >= 3 | project TimeGenerated, AppRoleName, OperationId, Kind='log', Source=tostring(Properties.CategoryName), Message)
        | where TimeGenerated > ago(span) and AppRoleName == role and isempty(OperationId)
        | summarize arg_max(TimeGenerated, Message), Count=count() by Kind, Source
        | order by TimeGenerated desc | take {{TopBackground}}
        | project Kind, Source, Message=substring(Message, 0, 300), Count, LastAt=TimeGenerated
        """;

    /// <summary>Только известные имена ролей — подстановка в KQL из белого списка.</summary>
    internal static string Role(string roleName) => roleName switch
    {
        AppInsightsRoles.WebRoleName or AppInsightsRoles.ProdAdminRoleName => roleName,
        _ => throw new ArgumentOutOfRangeException(nameof(roleName), roleName, "Неизвестная роль телеметрии"),
    };

    /// <summary>Длительность литералом KQL в минутах (<c>1440m</c>).</summary>
    internal static string Span(TimeSpan range) =>
        range > TimeSpan.Zero && range <= TimeSpan.FromDays(31)
            ? $"{(long)Math.Ceiling(range.TotalMinutes)}m"
            : throw new ArgumentOutOfRangeException(nameof(range), range, "Период — от минуты до 31 дня");
}
