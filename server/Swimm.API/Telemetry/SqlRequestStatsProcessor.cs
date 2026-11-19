using System.Diagnostics;
using OpenTelemetry;

namespace Swimm.API.Telemetry;

/// <summary>
/// Считает SQL-команды каждого запроса к сайту и пишет итог тегами на спан запроса
/// (<see cref="SqlSpans.QueryCountTag"/>, <see cref="SqlSpans.QueryMsTag"/>). Нужен потому, что
/// быстрые SQL-спаны в Azure больше не уходят (<see cref="FastSqlSpanFilter"/>): без счётчика
/// пропал бы ответ на «какой эндпоинт сколько ходит в базу и что не в кэше». Строк в Azure не
/// добавляет — два поля в уже существующей строке запроса.
///
/// ⚠ Стоит в цепочке РАНЬШЕ фильтра: считает команду до того, как фильтр её выбросит.
/// Команды вне запроса (фоновые службы) не считаются — у них нет спана запроса.
/// </summary>
internal sealed class SqlRequestStatsProcessor : BaseProcessor<Activity>
{
    private const string StatsProperty = "swimm.db.stats";

    public override void OnStart(Activity activity)
    {
        if (activity.Kind == ActivityKind.Server)
            activity.SetCustomProperty(StatsProperty, new Stats());
    }

    public override void OnEnd(Activity activity)
    {
        if (SqlSpans.IsSql(activity))
        {
            // Параллельные команды одного запроса (разные DbContext в Task.WhenAll) — отсюда Interlocked.
            if (FindRequestStats(activity) is { } stats)
            {
                Interlocked.Increment(ref stats.Count);
                Interlocked.Add(ref stats.Ticks, activity.Duration.Ticks);
            }
            return;
        }

        if (activity.Kind == ActivityKind.Server && activity.GetCustomProperty(StatsProperty) is Stats own)
        {
            activity.SetTag(SqlSpans.QueryCountTag, Volatile.Read(ref own.Count));
            activity.SetTag(SqlSpans.QueryMsTag,
                Math.Round(TimeSpan.FromTicks(Volatile.Read(ref own.Ticks)).TotalMilliseconds, 1));
        }
    }

    private static Stats? FindRequestStats(Activity activity)
    {
        for (var a = activity.Parent; a != null; a = a.Parent)
            if (a.Kind == ActivityKind.Server)
                return a.GetCustomProperty(StatsProperty) as Stats;
        return null;
    }

    private sealed class Stats
    {
        public int Count;
        public long Ticks;
    }
}
