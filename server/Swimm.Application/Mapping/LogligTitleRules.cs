using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Swimm.Application.Mapping;

/// <summary>
/// Названия соревнований loglig для поиска соседнего соревнования-дня (Р123). Дни одного старта
/// loglig называет одинаково, отличая день недели и дату:
/// «תחרות חלופית ל"מילניום" יום שישי 6.12.24 - 06/12/2024» и «… יום שבת 7.12.24 - 07/12/2024».
/// Тесты — <c>ProgrammeCheckRulesTests</c>.
/// </summary>
public static partial class LogligTitleRules
{
    /// <summary>
    /// Ключ сравнения: без дня недели («יום שישי», «שבת»…), дат (6.12.24, 07/12/2024), кавычек
    /// (", ״, ', ׳, &amp;quot;), тире и лишних пробелов. Пусто — сравнивать нечего.
    /// </summary>
    public static string Normalize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var s = WebUtility.HtmlDecode(title);
        s = DateRx().Replace(s, " ");
        s = WeekdayRx().Replace(s, " ");
        s = QuoteRx().Replace(s, "");
        s = DashRx().Replace(s, " ");
        return SpaceRx().Replace(s, " ").Trim();
    }

    /// <summary>Дата в хвосте заголовка loglig («… - 07/12/2024») — дата соревнования; нет — null.</summary>
    public static DateOnly? TitleDate(string? title)
    {
        var m = TailDateRx().Match(WebUtility.HtmlDecode(title ?? ""));
        return m.Success && DateOnly.TryParseExact(m.Groups[1].Value, "dd/MM/yyyy", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : null;
    }

    // «יום שישי», «יום ו'»; без «יום» — только «שישי»/«שבת» («שני» без «יום» — это и «второй»: «סבב שני»).
    [GeneratedRegex(@"(?<!\w)(?:יום\s+(?:ראשון|שני|שלישי|רביעי|חמישי|שישי|שבת|[אבגדהו][׳']?)|שישי|שבת)(?!\w)")]
    private static partial Regex WeekdayRx();

    // 6.12.24, 06.12.2024, 6/12/24, 07/12/2024, 6-12-24.
    [GeneratedRegex(@"\b\d{1,2}[./-]\d{1,2}[./-]\d{2,4}\b")]
    private static partial Regex DateRx();

    [GeneratedRegex("[\"״'׳`]")]
    private static partial Regex QuoteRx();

    [GeneratedRegex(@"\s[-–—]+(?=\s|$)|^[-–—]+|[-–—]+$")]
    private static partial Regex DashRx();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRx();

    [GeneratedRegex(@"[-–—]\s*(\d{2}/\d{2}/\d{4})\s*$")]
    private static partial Regex TailDateRx();
}
