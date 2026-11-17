namespace Swimm.Application.Mapping;

/// <summary>
/// Пороги страницы /Admin/AppInsights и блока дашборда (docs/plans/application-insights-plan.md §4,
/// Б3). Согласованы с оповещениями А4: письмо «Ошибки сервера» — 5xx больше 5 за 15 минут, «Объём» —
/// 80 % дневного лимита, «Исключения в фоне» — любое. Страница загорается раньше письма, не позже.
/// </summary>
public static class AppInsightsThresholds
{
    /// <summary>Сайт молчит дольше — жёлтое «сайт молчит» (§4.1). Метрики идут раз в минуту и без трафика.</summary>
    public static readonly TimeSpan SilentWarnAfter = TimeSpan.FromMinutes(15);

    /// <summary>Молчит дольше — красное (Б3). Задержка приёма Azure — 2–3 минуты, запас большой.</summary>
    public static readonly TimeSpan SilentDangerAfter = TimeSpan.FromMinutes(30);

    /// <summary>Доля ответов 5xx выше — красное (Б3). Любая ненулевая — жёлтое.</summary>
    public const double ErrorRateDanger = 0.05;

    /// <summary>Операция медленная, если её p95 выше (мс). Счётчик «медленные операции».</summary>
    public const double SlowOperationMs = 1000;

    /// <summary>p95 всех ответов выше (мс) — плитка «Ответ» жёлтая.</summary>
    public const double ResponseP95WarnMs = 1000;

    /// <summary>p95 вызовов Postgres выше (мс) — плитка «Postgres» жёлтая.</summary>
    public const double DbP95WarnMs = 500;

    /// <summary>Принято за сутки от лимита — жёлтое (как письмо «Объём», А4). 100 % — красное: приём стоит.</summary>
    public const double IngestionWarnShare = 0.8;

    /// <summary>Дневной лимит рабочей области по умолчанию, ГБ (А1.3, решение Влада 17.11).</summary>
    public const double DefaultDailyCapGb = 0.15;

    /// <summary>Час сброса дневного лимита, UTC (у <c>swimhub-logs</c> — 10:00).</summary>
    public const int DefaultCapResetHourUtc = 10;
}
