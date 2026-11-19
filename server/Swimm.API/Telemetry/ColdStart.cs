using System.Diagnostics;

namespace Swimm.API.Telemetry;

/// <summary>
/// «Холодный» запрос — пришёл в первые <see cref="Window"/> после старта процесса: кэш в памяти пуст,
/// соединений с базой нет, код не прогрет. Тег <see cref="Tag"/> = <c>true</c> (у тёплых тега нет —
/// экономим объём). Решение Влада 19.11.2026: 18.11 из 125 запросов дольше 3 с 82 пришлись на первые
/// 10 минут после старта (21 старт за двое суток — деплои и перезапуски), и они желтили p95.
/// Страница /Admin/AppInsights считает p95 без них.
/// </summary>
internal static class ColdStart
{
    internal const string Tag = "swimm.cold";
    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private static readonly long StartedAt = Stopwatch.GetTimestamp();

    /// <summary>Вызвать как можно раньше при старте, чтобы отсчёт пошёл от запуска, а не от первого запроса.</summary>
    internal static void Touch() => _ = StartedAt;

    internal static bool IsCold() => Stopwatch.GetElapsedTime(StartedAt) < Window;
}
