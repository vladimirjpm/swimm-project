namespace Swimm.Domain;

/// <summary>
/// ВИТРИННЫЙ сезон — какой сезон витрина считает ТЕКУЩИМ: season best, сезонные KPI и
/// значение по умолчанию в карусели сезонов (решение Влада 2026-08-09, уточнено 2026-08-13,
/// docs/season-boundary-rule.md).
///
/// Правило: витрина переключается на новый сезон не с его началом, а после ПОСЛЕДНЕГО
/// зимнего чемпионата — самого позднего из всех возрастных ступеней (детский, юношеский,
/// бугрим, мастерс). До этого показывается ПРОШЛЫЙ сезон.
///
/// Смысл не в переносе границы, а в том, что до зимнего чемпионата стартов слишком мало,
/// чтобы «лучшее в сезоне» что-то значило: в сезоне 2025/26 до 26 февраля это три
/// декабрьских старта плюс январь.
///
/// ⚠ Принадлежность заплыва сезону это НЕ меняет — она остаётся календарной
/// (<see cref="SeasonMath"/>), и на ней держатся возраст в сезоне, разбивка по сезонам,
/// ростер и импорт. Заплыв октября сразу лежит в новом сезоне, но витрина покажет его
/// только после зимнего чемпионата; выбрать новый сезон руками можно всегда — данные не
/// прячутся, меняется только умолчание. Два понятия сезона в одном продукте — тот же класс
/// ошибок, что <c>StartYearOf</c> против <c>FederationYearOf</c>: обе законны, ошибка —
/// молча смешать.
///
/// Момент переключения вычисляется ПО ДАННЫМ, поэтому он разный год от года, известен
/// только постфактум, а импорт зимнего чемпионата задним числом переключает витрину.
/// </summary>
public static class ShowcaseSeason
{
    /// <summary>
    /// Год НАЧАЛА витринного сезона (метка — <see cref="SeasonMath.Label"/>): самый свежий
    /// сезон, чей последний зимний чемпионат уже проплыли.
    ///
    /// Зимних чемпионатов в данных нет вовсе (не импортированы, отменены) → календарный
    /// сезон: прятать свежие данные, потому что нечем подтвердить границу, хуже, чем
    /// показать их.
    /// </summary>
    public static int StartYearOf(IEnumerable<DateTime> winterChampionshipDates, DateTime now)
    {
        var calendar = SeasonMath.StartYearOf(now);

        // Последний зимний чемпионат КАЖДОГО сезона: ступени плывут врозь (в 2025/26 мастерс
        // 10 января, возрастные 13–26 февраля), поэтому сезон закрывает самая поздняя дата.
        var lastWinter = new Dictionary<int, DateTime>();
        foreach (var d in winterChampionshipDates)
        {
            var season = SeasonMath.StartYearOf(d);
            if (!lastWinter.TryGetValue(season, out var cur) || d > cur) lastWinter[season] = d;
        }

        var closed = lastWinter
            .Where(kv => kv.Key <= calendar && kv.Value.Date <= now.Date)
            .Select(kv => kv.Key)
            .ToList();

        return closed.Count > 0 ? closed.Max() : calendar;
    }

    /// <summary>Полуинтервал витринного сезона <c>[Start, EndExclusive)</c> — целый сезон,
    /// а не «с даты чемпионата»: зимний чемпионат закрывает сезон, а не отрезает его начало.</summary>
    public static (DateTime Start, DateTime EndExclusive) RangeOf(
        IEnumerable<DateTime> winterChampionshipDates, DateTime now) =>
        SeasonMath.RangeOf(StartYearOf(winterChampionshipDates, now));
}
