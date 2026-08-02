using System.Collections.Generic;
using System.Linq;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Регресс: ивритский заголовок с ТЕКСТОВОЙ категорией вместо «пол + возраст» —
/// «200 חופשי - שומרי שבת מוקדמות צעירים» (заплывы для соблюдающих субботу).
///
/// Реальный протокол «מוקדמות אליפות הגילאים חורף 2025 מחוז צפון» (loglig 11792, стр. 102–105):
/// такой заголовок не брал ни один шаблон, строка молча игнорировалась, событие не менялось —
/// и 200 вольным вместе с 800 вольным дописались в предыдущую эстафету 4X50 комплексом.
/// В БД это выглядело как 20 личных результатов со стилем individual_medley, дистанцией 4X50
/// и мужским полом у девочек.
///
/// Строки ниже — сырой текст страницы (до RTL-реверса), как его отдаёт PdfPig.
/// </summary>
public class IsrOrgCompetitionParserShabbatHeaderTests
{
    // Хвост эстафетного заплыва (он распознаётся и сейчас) + заголовок «200 חופשי - שומרי שבת».
    private static readonly string[] Page =
    {
        "ןופצ זוחמ 2025 ףרוח םיאליגה תופילא תומדקומ",
        "24/01/2025 - 01/02/2025",
        "תואצות",
        "14-13 םינב - םיחילש ברועמ 4X50",
        "03/02/2025 03:34",
        "1 םוקימ 02:34.27 בגנה ינייחש לעופה 4 1",
        "םיריעצ תומדקומ תבש ירמוש - ישפוח 200",
        "ימואלניב דוקינ האצות ןודעומ הדיל תנש יטרפ םש החפשמ םש לולסמ הצקמ םוקימ",
        "678 01:53.09 םילשורי יבכמ 2012 םירפא ןתיא ידרו 6 1 1",
        "215 02:45.83 ןג תמר םורמ יבכמ 2011 ןתמ ישי ןב 8 1 2",
        "255 02:53.95 תוער .מ.פ יבכמ 2012 הווא ישי-ןב 3 1 3",
    };

    private static List<IsrOrgCompetitionResult> Parse() =>
        IsrOrgCompetitionParser.ParseLines(new List<string[]> { Page }, "HE").ToList();

    [Fact]
    public void TextCategoryHeader_StartsNewEvent_NotAppendedToPreviousRelay()
    {
        var events = Parse();

        var free200 = events.SingleOrDefault(e => e.EventStyleLen == "200");
        Assert.NotNull(free200);
        Assert.Equal("freestyle", free200!.EventStyleName);
        Assert.Equal(3, free200.Results.Count);
        Assert.Contains(free200.Results, r => r.Time == "01:53.09");

        // Эстафета осталась при своих: личные результаты в неё больше не попадают.
        var relay = events.Single(e => e.EventStyleLen == "4X50");
        Assert.DoesNotContain(relay.Results, r => r.Time == "01:53.09");
    }

    [Fact]
    public void TextCategoryHeader_GenderIsNone_AgeEmpty()
    {
        // Пол в шапке не указан, а поплыв смешанный (в примере рядом мальчики и девочки) —
        // выдумывать его нельзя: импорт возьмёт пол с самого пловца.
        var free200 = Parse().Single(e => e.EventStyleLen == "200");

        Assert.Equal("none", free200.EventStyleGender);
        Assert.Equal(string.Empty, free200.AgeGroup);
        Assert.Equal("shabbat", free200.EventStyleAge);
    }

    [Fact]
    public void OrdinaryGenderAgeHeader_StillWins()
    {
        // Фоллбек не должен перехватывать обычные заголовки: «- בנים 13-14» разбирается
        // прежним путём, с полом и возрастом.
        string[] page =
        {
            "ןופצ זוחמ 2025 ףרוח םיאליגה תופילא תומדקומ",
            "24/01/2025 - 01/02/2025",
            "תואצות",
            "13 םינב - ישפוח 200",
            "24/01/2025 09:15",
            "ימואלניב דוקינ האצות ןודעומ הדיל תנש יטרפ םש החפשמ םש לולסמ הצקמ םוקימ",
            "402 02:13.25 הרדח רפילפ לעופה 2012 המלע קינפורק 5 2 1",
        };

        var ev = Assert.Single(IsrOrgCompetitionParser.ParseLines(new List<string[]> { page }, "HE"));

        Assert.Equal("200", ev.EventStyleLen);
        Assert.Equal("freestyle", ev.EventStyleName);
        Assert.Equal("male", ev.EventStyleGender);
        Assert.Equal("13", ev.EventStyleAge);
    }
}
