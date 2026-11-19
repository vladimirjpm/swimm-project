using System.Diagnostics;
using OpenTelemetry;

namespace Swimm.API.Telemetry;

/// <summary>
/// Не отправляет в Azure SQL-спаны быстрее порога (по умолчанию 400 мс, ключ
/// <see cref="SqlSpans.MinDurationMsKey"/>), если команда не упала. Решение Влада 19.11.2026: на
/// 18.11 SQL-спаны — ~40 % дневного объёма (10 465 строк при 1 336 запросах), при паре тысяч
/// человек в день они одни съели бы дневной лимит 0,15 ГБ к утру. Медленные и упавшие — остаются:
/// ради них SQL и пишется. Счёт ВСЕХ команд по запросам сохраняет <see cref="SqlRequestStatsProcessor"/>.
///
/// Как выбрасывает: снимает флаг <c>Recorded</c> — экспортёр (пакетный процессор OpenTelemetry)
/// такие спаны пропускает. ⚠ Стоит в цепочке ДО экспортёра (регистрация до <c>UseAzureMonitor</c>)
/// и ПОСЛЕ счётчика.
/// </summary>
internal sealed class FastSqlSpanFilter(TimeSpan minDuration) : BaseProcessor<Activity>
{
    public override void OnEnd(Activity activity)
    {
        if (!SqlSpans.IsSql(activity)) return;
        if (activity.Status == ActivityStatusCode.Error) return;
        if (activity.Duration >= minDuration) return;

        activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
    }
}
