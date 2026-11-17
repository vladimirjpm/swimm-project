using System.Globalization;
using System.Text.Json;
using Swimm.Application.Dtos;

namespace Swimm.Infrastructure.Services.AppInsights;

/// <summary>
/// Строки результатов KQL (<see cref="AppInsightsQueries"/>) → DTO. Значение колонки может прийти
/// типом .NET или <see cref="JsonElement"/> (SDK отдаёт как есть) — разбор терпим к обоим; нет колонки
/// или null — 0 / null / «».
/// </summary>
internal static class AppInsightsRowMapper
{
    public static AppInsightsTotals Totals(AppInsightsRows rows)
    {
        // Запрос итогов всегда даёт одну строку; пустой ответ — как «ничего не было».
        var r = rows.Rows.Count > 0 ? rows.Rows[0] : new Dictionary<string, object?>();
        return new AppInsightsTotals(
            Long(r, "Requests"), Long(r, "Errors5xx"), Double(r, "P50Ms"), Double(r, "P95Ms"),
            Long(r, "SlowOperations"), Long(r, "DbCalls"), Double(r, "DbP95Ms"), Long(r, "DbFailed"),
            Long(r, "Exceptions"), Long(r, "BackgroundFailures"), Long(r, "AdminFailures"),
            Date(r, "LastTelemetryAt"), Double(r, "IngestedTodayMb") ?? 0);
    }

    public static IReadOnlyList<AppInsightsExceptionRow> Exceptions(AppInsightsRows rows) =>
        rows.Rows.Select(r => new AppInsightsExceptionRow(
            Text(r, "ExceptionType"), Text(r, "Message"), Long(r, "Count"), Date(r, "LastAt") ?? default, Text(r, "Operation"))).ToList();

    public static IReadOnlyList<AppInsightsOperationRow> Slow(AppInsightsRows rows) =>
        rows.Rows.Select(r => new AppInsightsOperationRow(
            Text(r, "Operation"), Long(r, "Count"), Double(r, "P50Ms") ?? 0, Double(r, "P95Ms") ?? 0, Long(r, "Errors5xx"))).ToList();

    public static IReadOnlyList<AppInsightsErrorRow> Errors(AppInsightsRows rows) =>
        rows.Rows.Select(r => new AppInsightsErrorRow(
            Text(r, "Operation"), (int)Long(r, "Code"), Long(r, "Count"), Date(r, "LastAt") ?? default, Bool(r, "ServerError"))).ToList();

    public static IReadOnlyList<AppInsightsBackgroundRow> Background(AppInsightsRows rows) =>
        rows.Rows.Select(r => new AppInsightsBackgroundRow(
            Text(r, "Kind"), Text(r, "Source"), Text(r, "Message"), Long(r, "Count"), Date(r, "LastAt") ?? default)).ToList();

    // ── Значения ────────────────────────────────────────────────────────────

    internal static long Long(IReadOnlyDictionary<string, object?> r, string column) =>
        Value(r, column) switch
        {
            null => 0,
            JsonElement { ValueKind: JsonValueKind.Number } j => j.TryGetInt64(out var l) ? l : (long)j.GetDouble(),
            JsonElement { ValueKind: JsonValueKind.String } j => long.TryParse(j.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : 0,
            JsonElement => 0,
            string s => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : 0,
            IConvertible c => Convert.ToInt64(c, CultureInfo.InvariantCulture),
            _ => 0,
        };

    /// <summary>null — пусто, NaN или бесконечность (перцентиль на пустых данных).</summary>
    internal static double? Double(IReadOnlyDictionary<string, object?> r, string column)
    {
        double? value = Value(r, column) switch
        {
            null => null,
            JsonElement { ValueKind: JsonValueKind.Number } j => j.GetDouble(),
            JsonElement { ValueKind: JsonValueKind.String } j =>
                double.TryParse(j.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null,
            JsonElement => null,
            string s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null,
            IConvertible c => Convert.ToDouble(c, CultureInfo.InvariantCulture),
            _ => null,
        };
        return value is { } v && double.IsFinite(v) ? v : null;
    }

    internal static bool Bool(IReadOnlyDictionary<string, object?> r, string column) =>
        Value(r, column) switch
        {
            bool b => b,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.String } j => bool.TryParse(j.GetString(), out var b) && b,
            string s => bool.TryParse(s, out var b) && b,
            _ => false,
        };

    internal static string Text(IReadOnlyDictionary<string, object?> r, string column) =>
        Value(r, column) switch
        {
            null => "",
            JsonElement { ValueKind: JsonValueKind.String } j => j.GetString() ?? "",
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => "",
            JsonElement j => j.GetRawText(),
            var v => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
        };

    /// <summary>Время UTC (<see cref="DateTimeKind.Utc"/>) или null.</summary>
    internal static DateTime? Date(IReadOnlyDictionary<string, object?> r, string column) =>
        Value(r, column) switch
        {
            DateTimeOffset o => o.UtcDateTime,
            DateTime d => d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : d.ToUniversalTime(),
            JsonElement { ValueKind: JsonValueKind.String } j => ParseDate(j.GetString()),
            string s => ParseDate(s),
            _ => null,
        };

    private static DateTime? ParseDate(string? s) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var o) ? o.UtcDateTime : null;

    private static object? Value(IReadOnlyDictionary<string, object?> r, string column) =>
        r.TryGetValue(column, out var v) ? v : null;
}
