using System.Globalization;

namespace Swimm.Application.Mapping;

/// <summary>Первый этап эстафеты пловца с промежуточным — кандидат в «рекорд проплыт на эстафете».</summary>
/// <param name="Split">Время этапа из <c>RelayMembers.SplitTime</c> («00:30.25»).</param>
/// <param name="RelayDistance">Дистанция эстафеты как в Results («4X50»).</param>
/// <param name="RelayStyle">Стиль эстафеты («freestyle» / «individual_medley»).</param>
public sealed record RelayLeadOffLeg(string Split, DateTime Date, string RelayDistance, string RelayStyle, string PoolType);

/// <summary>
/// Узнаёт рекорд, проплытый ПЕРВЫМ ЭТАПОМ эстафеты (время первого этапа засчитывается
/// личным): у пловца есть эстафета, где он плыл первым, и его этап совпал с рекордом.
///
/// Совпадать обязаны: время до сотой, бассейн, дистанция этапа (4X50 → 50), стиль этапа
/// (у комплексной эстафеты первый этап — на спине), дата ±1 день — федерация датирует
/// рекорды многодневного старта его первым днём (30.25 Гостомельской: рекорд 9/1, эстафета
/// 10.01.2026, И-28). Нужна эстафета С промежуточными — без них сравнивать нечего.
/// </summary>
public static class RelayLeadOffMatcher
{
    public static bool Matches(
        string recordTime, string? recordDate, string recordDistance, string recordStyle, string recordPool,
        RelayLeadOffLeg leg)
    {
        if (!string.Equals(leg.PoolType, recordPool, StringComparison.OrdinalIgnoreCase)) return false;
        if (LegDistance(leg.RelayDistance) != recordDistance.TrimEnd('m', 'M')) return false;
        if (!string.Equals(LegStyle(leg.RelayStyle), recordStyle, StringComparison.OrdinalIgnoreCase)) return false;
        if (ToMs(leg.Split) is not long legMs || ToMs(recordTime) is not long recMs || legMs != recMs) return false;

        if (!DateTime.TryParseExact(recordDate?.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var recDate))
            return false;
        return Math.Abs((leg.Date.Date - recDate.Date).TotalDays) <= 1;
    }

    /// <summary>«4X50» → «50».</summary>
    private static string LegDistance(string relayDistance)
    {
        var i = relayDistance.IndexOfAny(['X', 'x']);
        return (i >= 0 ? relayDistance[(i + 1)..] : relayDistance).TrimEnd('m', 'M');
    }

    /// <summary>Первый этап комплексной эстафеты — на спине; у остальных — стиль эстафеты.</summary>
    private static string LegStyle(string relayStyle) =>
        relayStyle.Contains("medley", StringComparison.OrdinalIgnoreCase) ? "backstroke" : relayStyle;

    private static long? ToMs(string time)
    {
        double seconds = 0;
        foreach (var p in time.Trim().Split(':'))
        {
            if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return null;
            seconds = seconds * 60 + v;
        }
        return (long)Math.Round(seconds * 1000);
    }
}
