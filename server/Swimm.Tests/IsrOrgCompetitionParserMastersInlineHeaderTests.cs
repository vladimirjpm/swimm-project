using System.Collections.Generic;
using System.Linq;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Регресс: мастерс-категория в самом заголовке заплыва, одной строкой —
/// «50 גב - מאסטרס נ 21-99». Реальный протокол «מאסטר פאסטר 2025» (compID 13665, loglig 12499):
/// ни один шаблон такую строку не брал, файл разбирался в ноль событий, и пакетное затягивание
/// сочло соревнование пустым («∅ нет протокола»).
///
/// Строки — сырой текст страницы (до RTL-реверса), как его отдаёт PdfPig.
/// </summary>
public class IsrOrgCompetitionParserMastersInlineHeaderTests
{
    private const string TableHeader =
        "ימואלניב דוקינ האצות ןודעומ הדיל תנש יטרפ םש החפשמ םש לולסמ הצקמ םוקימ";

    private static readonly string[] Page =
    {
        "2025 רטסאפ רטסאמ",
        "30/05/2025",
        "תואצות",
        "21-99 נ סרטסאמ - בג 50",
        "16:00 30/05/2025",
        TableHeader,
        "113 00:52.13 םיקיתווקא 1957 ןלא רנדוב-לשיפ 5 1 2",
        "21-99 ג סרטסאמ - בג 50",
        "16:03 30/05/2025",
        TableHeader,
        "257 00:34.75 םיקיתווקא 1974 ןורוד ץיבורזיוא 5 1 2",
        "256 00:34.78 םיקיתווקא 1969 דדלא רודגיבא 4 3 3",
    };

    private static List<IsrOrgCompetitionResult> Parse() =>
        IsrOrgCompetitionParser.ParseLines(new List<string[]> { Page }, "HE").ToList();

    [Fact]
    public void InlineMastersHeader_StartsEventPerGender()
    {
        var events = Parse();

        Assert.Equal(2, events.Count);
        Assert.All(events, e =>
        {
            Assert.Equal("50", e.EventStyleLen);
            Assert.Equal("backstroke", e.EventStyleName);
            Assert.Equal("21-99", e.EventStyleAge);
            Assert.Equal("30/05/2025", e.Date);
        });

        var women = events.Single(e => e.EventStyleGender == "female");
        Assert.Single(women.Results);
        Assert.Equal("00:52.13", women.Results[0].Time);

        var men = events.Single(e => e.EventStyleGender == "male");
        Assert.Equal(2, men.Results.Count);
        Assert.Contains(men.Results, r => r.Time == "00:34.78");
    }
}
