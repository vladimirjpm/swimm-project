using System.Globalization;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Плитки страницы /Admin/AppInsights, тон здоровья и главная цифра блока дашборда
/// (docs/plans/application-insights-plan.md, Б1–Б3). Чистые функции: итоги KQL и время приходят
/// снаружи (<c>AppInsightsReader</c>), поэтому пороги и тона проверяются тестами без Azure.
/// Пороги — <see cref="AppInsightsThresholds"/>.
/// </summary>
public static class AppInsightsOverviewBuilder
{
    // Десятичная запятая без зависимости от ICU системы (CultureInfo "ru-RU" в контейнере без ICU не найдётся).
    private static readonly NumberFormatInfo Ru = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = " " };

    // ── Тоны ────────────────────────────────────────────────────────────────

    /// <summary>Доля ответов 5xx; null — запросов не было.</summary>
    public static double? ErrorRate(long errors5xx, long requests) =>
        requests <= 0 ? null : (double)errors5xx / requests;

    /// <summary>ok — ни одной 5xx; danger — доля выше 5 %; иначе warning.</summary>
    public static string ErrorTone(long errors5xx, long requests) =>
        errors5xx <= 0 ? "ok"
        : ErrorRate(errors5xx, requests) is { } rate && rate <= AppInsightsThresholds.ErrorRateDanger ? "warning"
        : "danger";

    /// <summary>
    /// Тон «последней телеметрии». Сайт (<paramref name="watch"/>) шлёт метрики раз в минуту и без
    /// трафика: тишина 15+ мин — warning, 30+ мин или ничего за 7 дней — danger. ProdAdmin запускают
    /// руками — его тишина нормальна (neutral).
    /// </summary>
    public static string SilenceTone(DateTime? lastAt, DateTime now, bool watch)
    {
        if (!watch) return "neutral";
        if (lastAt is not { } last) return "danger";
        var silent = now - DateTime.SpecifyKind(last, DateTimeKind.Utc);
        return silent > AppInsightsThresholds.SilentDangerAfter ? "danger"
            : silent > AppInsightsThresholds.SilentWarnAfter ? "warning"
            : "ok";
    }

    /// <summary>Приём от дневного лимита: 80 %+ — warning (как письмо «Объём»), 100 %+ — danger (приём стоит).</summary>
    public static string IngestionTone(double ingestedMb, double capMb) =>
        capMb <= 0 ? "neutral"
        : ingestedMb >= capMb ? "danger"
        : ingestedMb >= capMb * AppInsightsThresholds.IngestionWarnShare ? "warning"
        : "ok";

    /// <summary>
    /// Здоровье роли за период — тон страницы и блока дашборда (Б3): красный — сайт молчит дольше
    /// 30 мин или 5xx выше 5 %; жёлтый — есть 5xx, исключения, сбои вне запроса или приём выше 80 %.
    /// </summary>
    /// <param name="watchSilence">Следить за тишиной — только у сайта.</param>
    /// <param name="includeAdmin">Считать сбои ProdAdmin (<see cref="AppInsightsTotals.AdminFailures"/>) — блок дашборда.</param>
    public static (string Tone, IReadOnlyList<string> Reasons) Health(
        AppInsightsTotals t, bool watchSilence, bool includeAdmin, double capMb, DateTime now)
    {
        var reasons = new List<(string Tone, string Text)>();

        var silence = SilenceTone(t.LastTelemetryAt, now, watchSilence);
        if (silence is "warning" or "danger")
            reasons.Add((silence, t.LastTelemetryAt is { } last
                ? $"сайт молчит: последняя телеметрия {JobTimeText.Ago(last, now)}"
                : "сайт молчит: телеметрии нет за 7 дней"));

        var errors = ErrorTone(t.Errors5xx, t.Requests);
        if (errors != "ok")
            reasons.Add((errors, $"ошибок сервера (5xx): {t.Errors5xx} из {t.Requests} — {Percent(ErrorRate(t.Errors5xx, t.Requests))}"));

        if (t.Exceptions > 0) reasons.Add(("warning", $"исключений: {t.Exceptions}"));
        if (t.BackgroundFailures > 0) reasons.Add(("warning", $"сбоев вне запроса (фон): {t.BackgroundFailures}"));
        if (includeAdmin && t.AdminFailures > 0) reasons.Add(("warning", $"ошибок ProdAdmin: {t.AdminFailures}"));

        var ingestion = IngestionTone(t.IngestedTodayMb, capMb);
        if (ingestion == "danger") reasons.Add(("danger", "дневной лимит приёма исчерпан — телеметрия не принимается до сброса"));
        else if (ingestion == "warning") reasons.Add(("warning", $"принято {Share(t.IngestedTodayMb, capMb)} дневного лимита"));

        var tone = reasons.Any(r => r.Tone == "danger") ? "danger" : reasons.Count > 0 ? "warning" : "ok";
        return (tone, reasons.OrderBy(r => r.Tone == "danger" ? 0 : 1).Select(r => r.Text).ToList());
    }

    // ── Страница ────────────────────────────────────────────────────────────

    /// <summary>Плитки страницы (§4 Б2 п. 2). Href — якорь таблицы на странице.</summary>
    public static IReadOnlyList<OverviewTile> Tiles(
        AppInsightsTotals t, TimeSpan range, bool watchSilence, double capMb, int capResetHourUtc, DateTime now)
    {
        var period = AppInsightsRanges.Title(range);
        var noRequests = t.Requests == 0;

        return
        [
            new("requests", "Запросов", t.Requests.ToString(Ru),
                noRequests ? period : $"{period}: посетители {t.Visitors.ToString(Ru)} · роботы {t.Bots.ToString(Ru)} · наши {t.Internal.ToString(Ru)}",
                "neutral", "#slow"),
            new("errors", "Ошибки 5xx", noRequests ? "—" : Percent(ErrorRate(t.Errors5xx, t.Requests)),
                noRequests ? $"запросов {period} не было" : $"{t.Errors5xx} из {t.Requests}",
                noRequests ? "neutral" : ErrorTone(t.Errors5xx, t.Requests), "#errors"),
            // p95 — без наших запросов и первых 10 минут после старта (решение Влада 19.11.2026).
            new("latency", "Ответ p95", Ms(t.P95Ms),
                (t.P50Ms is null ? "нет тёплых запросов" : $"медиана {Ms(t.P50Ms)}")
                + (t.Cold > 0 ? $" · без {t.Cold.ToString(Ru)} холодных (стартов: {t.Starts.ToString(Ru)})" : ""),
                t.P95Ms is not { } p95 ? "neutral" : p95 > AppInsightsThresholds.ResponseP95WarnMs ? "warning" : "ok", "#slow"),
            new("exceptions", "Исключения", t.Exceptions.ToString(Ru),
                $"вне запроса (фон): {t.BackgroundFailures}",
                t.Exceptions > 0 || t.BackgroundFailures > 0 ? "warning" : "ok", "#exceptions"),
            new("db", "Postgres p95", Ms(t.DbP95Ms), $"вызовов {t.DbCalls}, сбоев {t.DbFailed}",
                t.DbCalls == 0 ? "neutral"
                : t.DbFailed > 0 || t.DbP95Ms > AppInsightsThresholds.DbP95WarnMs ? "warning" : "ok", "#slow"),
            new("slow", "Медленные операции", t.SlowOperations.ToString(Ru),
                $"p95 выше {Ms(AppInsightsThresholds.SlowOperationMs)}", t.SlowOperations > 0 ? "info" : "ok", "#slow"),
            new("ingestion", "Принято сегодня", $"{Mb(t.IngestedTodayMb)} / {Mb(capMb)} МБ",
                $"{Share(t.IngestedTodayMb, capMb)} дневного лимита, сброс в {capResetHourUtc:00}:00 UTC; все роли",
                IngestionTone(t.IngestedTodayMb, capMb), "#ingestion"),
            new("last", "Последняя телеметрия", t.LastTelemetryAt is { } last ? JobTimeText.Ago(last, now) : "нет за 7 дней",
                t.LastTelemetryAt is { } at ? JobTimeText.Format(at)
                : watchSilence ? "сайт не шлёт телеметрию" : "ProdAdmin не запускался",
                SilenceTone(t.LastTelemetryAt, now, watchSilence), "#last"),
        ];
    }

    /// <summary>
    /// Ответ страницы. <paramref name="data"/> — null, когда прочитать не удалось: тогда плитки и
    /// таблицы пустые, тон neutral, смысл несёт <paramref name="connection"/>.
    /// </summary>
    public static AppInsightsOverview Overview(
        AppInsightsConnection connection, AppInsightsProcessInfo process, TimeSpan range, string role,
        AppInsightsOverviewData? data, AppInsightsLinks? links, double capMb, int capResetHourUtc, DateTime now)
    {
        var roleKey = AppInsightsRoles.Normalize(role) ?? AppInsightsRoles.Web;
        var roleName = AppInsightsRoles.RoleName(roleKey)!;
        var rangeKey = AppInsightsRanges.Key(range);

        if (data is null)
            return new AppInsightsOverview(connection, process, rangeKey, roleKey, roleName, now, "neutral", [],
                null, null, [], [], [], [], [], links);

        var watchSilence = roleKey == AppInsightsRoles.Web;
        var t = data.Totals;
        var (tone, reasons) = Health(t, watchSilence, includeAdmin: false, capMb, now);
        return new AppInsightsOverview(connection, process, rangeKey, roleKey, roleName, now, tone, reasons,
            t.LastTelemetryAt, t.LastTelemetryAt is { } last ? JobTimeText.Ago(last, now) : "нет за 7 дней",
            Tiles(t, range, watchSilence, capMb, capResetHourUtc, now),
            data.Exceptions, data.SlowOperations, data.Errors, data.Background, links);
    }

    // ── Блок дашборда ───────────────────────────────────────────────────────

    /// <summary>
    /// Блок 10 «App Insights» (Б3): главная цифра — 5xx сайта за 24 ч (при нуле «✓»), подпись
    /// «N запросов · p95 · телеметрия N назад», метрики. Не настроено / нет входа / не прочитано — серый
    /// блок с текстом состояния, не красный. <paramref name="totals"/> — сайт за 24 ч.
    /// </summary>
    public static AppInsightsSummary Summary(AppInsightsConnection connection, AppInsightsTotals? totals, double capMb, DateTime now)
    {
        if (totals is null)
            return new AppInsightsSummary(connection, now, "neutral", [], "—", "neutral", connection.Title, null, null, null, null, []);

        var t = totals;
        var (tone, reasons) = Health(t, watchSilence: true, includeAdmin: true, capMb, now);
        var caption = $"{t.Requests.ToString(Ru)} {Plural(t.Requests, "запрос", "запроса", "запросов")}"
                      + (t.P95Ms is null ? "" : $" · p95 {Ms(t.P95Ms)}")
                      + " · телеметрия " + (t.LastTelemetryAt is { } last ? JobTimeText.Ago(last, now) : "нет за 7 дней");

        List<AppInsightsMetric> metrics =
        [
            new("errors", "Ошибки 5xx", t.Errors5xx.ToString(Ru), ErrorTone(t.Errors5xx, t.Requests)),
            new("exceptions", "Исключения", t.Exceptions.ToString(Ru), t.Exceptions > 0 ? "warning" : "ok"),
            new("background", "Сбои фона и ProdAdmin", (t.BackgroundFailures + t.AdminFailures).ToString(Ru),
                t.BackgroundFailures + t.AdminFailures > 0 ? "warning" : "ok"),
            new("slow", "Медленные операции", t.SlowOperations.ToString(Ru), t.SlowOperations > 0 ? "info" : "ok"),
            new("ingestion", "Принято сегодня", $"{Share(t.IngestedTodayMb, capMb)} лимита", IngestionTone(t.IngestedTodayMb, capMb)),
        ];

        return new AppInsightsSummary(connection, now, tone, reasons,
            t.Errors5xx == 0 ? "✓" : t.Errors5xx.ToString(Ru), ErrorTone(t.Errors5xx, t.Requests),
            caption, t.Errors5xx, t.Requests, t.P95Ms, t.LastTelemetryAt, metrics);
    }

    // ── Формат ──────────────────────────────────────────────────────────────

    /// <summary>«320 мс», «4,5 с»; null — «—».</summary>
    public static string Ms(double? ms) => ms switch
    {
        null => "—",
        < 10_000 => $"{Math.Round(ms.Value).ToString("0", Ru)} мс",
        _ => $"{(ms.Value / 1000).ToString("0.#", Ru)} с",
    };

    /// <summary>«0,4 %», «12 %»; null — «—».</summary>
    public static string Percent(double? share) => share switch
    {
        null => "—",
        0 => "0 %",
        < 0.1 => $"{(share.Value * 100).ToString("0.#", Ru)} %",
        _ => $"{(share.Value * 100).ToString("0", Ru)} %",
    };

    public static string Mb(double mb) => mb.ToString(mb < 10 ? "0.##" : "0", Ru);

    private static string Share(double mb, double capMb) => capMb <= 0 ? "—" : Percent(mb / capMb);

    private static string Plural(long n, string one, string few, string many)
    {
        var mod100 = n % 100;
        var mod10 = n % 10;
        return mod100 is >= 11 and <= 14 ? many : mod10 == 1 ? one : mod10 is >= 2 and <= 4 ? few : many;
    }
}
