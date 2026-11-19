namespace Swimm.Application.Constants;

/// <summary>
/// Пределы исходящих запросов забора (isr.org.il, loglig) — docs/plans/prod-auto-pull-plan.md §6:
/// с П2/П5 чужие PDF и HTML разбирает публичный процесс, который отдаёт сайт. Ответ больше
/// <see cref="MaxResponseBytes"/> обрывается (HttpRequestException) раньше, чем съест память
/// процесса на B1 (1,75 ГБ).
/// </summary>
public static class OutboundLimits
{
    /// <summary>
    /// 32 МБ: протокол чемпионата на loglig — 1–3 МБ, регламент — сотни КБ, страница программы —
    /// ~120 КБ. Запас на порядок, но не «сколько пришлёт».
    /// </summary>
    public const long MaxResponseBytes = 32L * 1024 * 1024;
}
