namespace Swimm.Application.Mapping;

/// <summary>Соревнование, на котором проплыт официальный рекорд.</summary>
/// <param name="EventId">Многодневный старт — ссылка ведёт на весь турнир, а не на день.</param>
/// <param name="ResultId">Строка заплыва (у эстафеты — строка команды).</param>
public sealed record RecordMeet(int CompetitionId, int? EventId, string Name, bool IsChampionship, long ResultId);

/// <summary>Личный заплыв пловца — кандидат в «рекорд проплыт здесь».</summary>
public sealed record RecordMeetSwim(int TimeMs, DateTime Date, string Distance, string Style, string PoolType, RecordMeet Meet);

/// <summary>
/// Где проплыт рекорд, которого в справочнике нет: у <c>Records</c> поля «соревнование» нет,
/// федерация его не даёт, а держатель — строка имени (docs/plans/record-card-plan.md §2).
/// Ищем среди заплывов САМОГО пловца: то же время до сотой, стиль, дистанция, бассейн и
/// дата ±1 день. Нет совпадения — рекорд до наших данных или заграничный, подпись не
/// выдумываем. Эстафетный рекорд (первый этап) ищется тем же правилом, что метка lead-off.
/// </summary>
public static class RecordMeetMatcher
{
    public static RecordMeet? Find(
        string recordTime, string? recordDate, string recordDistance, string recordStyle, string recordPool,
        IEnumerable<RecordMeetSwim> swims, IEnumerable<RelayLeadOffLeg> legs)
    {
        if (RelayLeadOffMatcher.ToMs(recordTime) is not long recMs) return null;
        var distance = recordDistance.TrimEnd('m', 'M');

        // Личный заплыв сильнее эстафеты: одно и то же время бывает и там, и там, а рекорд
        // засчитывается прежде всего за личный старт. Из нескольких — ближайший по дате.
        var swim = swims
            .Where(s => s.TimeMs == recMs
                        && string.Equals(s.PoolType, recordPool, StringComparison.OrdinalIgnoreCase)
                        && s.Distance.TrimEnd('m', 'M') == distance
                        && string.Equals(s.Style, recordStyle, StringComparison.OrdinalIgnoreCase)
                        && RelayLeadOffMatcher.DateClose(s.Date, recordDate))
            .OrderBy(s => DateGap(s.Date, recordDate))
            .FirstOrDefault();
        if (swim is not null) return swim.Meet;

        return legs
            .Where(l => l.Meet is not null
                        && RelayLeadOffMatcher.Matches(recordTime, recordDate, recordDistance, recordStyle, recordPool, l))
            .OrderBy(l => DateGap(l.Date, recordDate))
            .Select(l => l.Meet)
            .FirstOrDefault();
    }

    private static double DateGap(DateTime date, string? recordDate) =>
        DateTime.TryParseExact(recordDate?.Trim(), "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d)
            ? Math.Abs((date.Date - d.Date).TotalDays)
            : double.MaxValue;
}
