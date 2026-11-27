using System.Collections.Generic;
using System.Linq;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Регресс: личный заплыв с «מיקס» вместо пола и возрастом цифрами — «800 חופשי - מיקס 14-18».
///
/// Реальный протокол «משחה המילניום ה25» (compID 16862, loglig 15972, стр. 131–132, 27.11.2026):
/// шаблон обычного заголовка ждал пол из «בנים/בנות/נשים/גברים», шаблон текстовой категории —
/// категорию без цифр. Заголовок не брал никто, и шесть строк 800 м дописались в предыдущую
/// эстафету «4X100 מעורב שליחים - מיקס 14-15» — с её дистанцией и датой 28/11 вместо 26/11.
/// В админке это шесть ошибок «Эстафетная дистанция у личного результата».
///
/// Строки ниже — сырой текст страниц (до RTL-реверса), как его отдаёт PdfPig.
/// </summary>
public class IsrOrgCompetitionParserMixAgeHeaderTests
{
    private const string Title = "-25ה םוינלימה החשמ";
    private const string Range = "28/11/2026 - 26/11/2026";
    private const string TableHeader =
        "ימואלניב דוקינ האצות ןודעומ הדיל תנש יטרפ םש החפשמ םש לולסמ הצקמ םוקימ";

    private static readonly string[] RelayPage =
    {
        Title, Range, "תואצות",
        "14-15 סקימ - םיחילש ברועמ 4X100",
        "19:46 28/11/2026",
        "1 םוקימ 04:17.52 הינתנ פמילוא לעופה 4 1",
    };

    private static readonly string[] Free800Page =
    {
        Title, Range, "תואצות",
        "14-18 סקימ - ישפוח 800",
        "19:04 26/11/2026",
        TableHeader,
        "561 08:53.93 קילאיב תירק יבכמ 2011 ןוסינ בוליגזיח 3 1 2",
        "624 09:18.48 קילאיב תירק יבכמ 2011 הלייא יול 6 1 3",
        "0 NS קילאיב תירק יבכמ 2012 הנא וקנברוג 5 1 -",
    };

    private static List<IsrOrgCompetitionResult> Parse() =>
        IsrOrgCompetitionParser.ParseLines(new List<string[]> { RelayPage, Free800Page }, "HE").ToList();

    [Fact]
    public void MixAgeHeader_StartsNewIndividualEvent_NotAppendedToRelay()
    {
        var events = Parse();

        var free800 = events.SingleOrDefault(e => e.EventStyleLen == "800");
        Assert.NotNull(free800);
        Assert.Equal("freestyle", free800!.EventStyleName);
        Assert.Equal(3, free800.Results.Count);
        Assert.Contains(free800.Results, r => r.Time == "08:53.93");

        // Эстафета осталась при своей команде: личные 800 м в неё больше не попадают.
        var relay = events.Single(e => e.EventStyleLen == "4X100");
        Assert.DoesNotContain(relay.Results, r => r.Time is "08:53.93" or "09:18.48");
        Assert.DoesNotContain(relay.Results, r => r.LastName == "גורבנקו");
    }

    [Fact]
    public void MixAgeHeader_GenderNone_AgeKept_DateFromOwnHeader()
    {
        var free800 = Parse().Single(e => e.EventStyleLen == "800");

        // Пол не выдумываем — импорт возьмёт его с пловца (как у «שומרי שבת»).
        Assert.Equal("none", free800.EventStyleGender);
        Assert.Equal("14-18", free800.EventStyleAge);
        Assert.Equal("14-18", free800.AgeGroup);
        // Своя дата под заголовком, а не дата эстафеты сверху (28/11).
        Assert.Equal("26/11/2026", free800.Date);
    }
}
