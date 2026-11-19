using System.Diagnostics;

namespace Swimm.API.Telemetry;

/// <summary>
/// Общее для процессоров SQL-спанов: что считать SQL-спаном и имена тегов. Спаны Postgres даёт
/// <c>AddNpgsql()</c> (источник <c>Npgsql</c>) — один на команду, плюс <c>CONNECT</c> на открытие
/// физического соединения.
/// </summary>
internal static class SqlSpans
{
    internal const string NpgsqlSourceName = "Npgsql";

    /// <summary>Конфиг: SQL быстрее стольких мс в Azure не отправляется (см. <see cref="FastSqlSpanFilter"/>).</summary>
    internal const string MinDurationMsKey = "Telemetry:SqlSpanMinMs";
    internal const int DefaultMinDurationMs = 400;

    /// <summary>
    /// Теги на спане запроса к сайту (в Azure — <c>AppRequests.Properties</c>): сколько SQL-команд
    /// сделал запрос и сколько мс они шли в сумме. Считаются по ВСЕМ командам, включая быстрые,
    /// которые сами в Azure не уходят. 0 — ответ целиком из кэша. Префикс свой, чтобы не спутать с
    /// семантическими соглашениями OpenTelemetry.
    /// </summary>
    internal const string QueryCountTag = "swimm.db.query_count";
    internal const string QueryMsTag = "swimm.db.query_ms";

    internal static bool IsSql(Activity activity) => activity.Source.Name == NpgsqlSourceName;
}
