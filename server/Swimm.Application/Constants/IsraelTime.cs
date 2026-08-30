namespace Swimm.Application.Constants;

/// <summary>
/// Местное время соревнований — Израиль. Источники печатают часы БЕЗ пояса («שעת הזנקה 09:30»),
/// и админ вводит разминку тоже по стенным часам; в базу и дальше по системе идёт момент
/// времени (UTC). Перевод делается ровно здесь, чтобы двух разных ответов не появилось.
///
/// Пришло из <c>StartListPullService</c>, когда у перевода появился второй потребитель —
/// ручной ввод разминки в админке (шаг Т1).
/// </summary>
public static class IsraelTime
{
    /// <summary>
    /// Пояс соревнований. IANA-идентификатор работает и на Windows (.NET 6+ ходит в ICU),
    /// но у машин с отключённым ICU остаётся только windows-имя — поэтому фоллбек, а не
    /// одно имя. Не нашлось ни то, ни другое — UTC: пусть время «поедет», но не упадёт.
    /// </summary>
    public static readonly TimeZoneInfo Zone = Resolve();

    /// <summary>
    /// Местное время → UTC. null, если такого момента не существует (несуществующий час при
    /// переводе часов): на витрине время и так помечено «≈», а врать точным моментом нельзя.
    /// </summary>
    public static DateTime? ToUtc(DateTime local)
    {
        try
        {
            return TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>UTC → местное время: обратный ход для форм админки, где человек правит то,
    /// что сам когда-то ввёл.</summary>
    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "Asia/Jerusalem", "Israel Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.Utc;
    }
}
