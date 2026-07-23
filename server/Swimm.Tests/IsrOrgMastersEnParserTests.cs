using System.Linq;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Регресс на masters-EN экспорт loglig (зимние мастерс ARENA, culture-кука _culture=en-US):
/// английский заголовок без пола/возраста ("400m Freestyle"), категории — ивритскими
/// строками "מאסטרס <пол> <возраст>" (в сыром RTL-реверсе). Логика EN-ветки зеркалит HE,
/// чтобы двуязычная пара давала одинаковую последовательность событий (см. «Синхр. языки»).
/// Строки — сырой текст страницы, как в debug-логе парсера (raw=...).
/// </summary>
public class IsrOrgMastersEnParserTests
{
    private const string Title = "לארשי תופילא ARENA 2026 ףרוח סרטסאמ";
    private const string DateRange = "09/01/2026 - 10/01/2026";

    [Fact]
    public void MastersEn_IndividualEvents_SplitByHebrewAgeLines()
    {
        var pages = new[]
        {
            new[]
            {
                Title, DateRange, "Results",
                "400m Freestyle",
                "Rank Heat Lane Last name First name Club Result",
                "21-29 ג סרטסאמ",                       // реверс «מאסטרס ג 21-29»
                "1 1 3 GOOTMAN BAR 2003 ASA TECHNION 04:17.41 560",
                "30-34 ג סרטסאמ",
                "1 4 3 COHEN Sagi 1992 Macabbi Haifa 04:31.47 477",
                "- 4 5 KIESLER Gil 1996 Macabbi Haifa NS 0",
                "21-29 נ סרטסאמ",                       // смена пола → новое событие
                "1 2 4 LEVI Dana 2000 Rehovot Masters 05:01.10 300",
            },
        };

        var comps = IsrOrgCompetitionParser.ParseLines(pages, "en").ToList();

        Assert.Equal(3, comps.Count);
        Assert.All(comps, c => Assert.Equal("freestyle", c.EventStyleName));
        Assert.Equal(("male", "21-29", 1), (comps[0].EventStyleGender, comps[0].EventStyleAge, comps[0].Results.Count));
        Assert.Equal(("male", "30-34", 2), (comps[1].EventStyleGender, comps[1].EventStyleAge, comps[1].Results.Count));
        Assert.Equal(("female", "21-29", 1), (comps[2].EventStyleGender, comps[2].EventStyleAge, comps[2].Results.Count));
        Assert.Equal("GOOTMAN", comps[0].Results[0].LastName);
    }

    [Fact]
    public void MastersEn_DqLine_DoesNotSwallowNextAgeCategory()
    {
        // Баг: DQ-строка "…DQ / SW 4.4 0" не проходит полный матч, и парсер подклеивал
        // к ней следующую строку — съедая строку смены категории. События сливались.
        var pages = new[]
        {
            new[]
            {
                Title, DateRange, "Results",
                "50m Butterfly",
                "55-59 נ סרטסאמ",
                "- 2 7 SLUTZKAY Dafna 1969 aqvatikim DQ / SW 4.4 0",
                "60-64 נ סרטסאמ",
                "1 1 4 Blumenthal Deborah 1964 aqvatikim 00:55.05 82",
            },
        };

        var comps = IsrOrgCompetitionParser.ParseLines(pages, "en").ToList();

        Assert.Equal(2, comps.Count);
        Assert.Equal("55-59", comps[0].EventStyleAge);
        Assert.Equal("60-64", comps[1].EventStyleAge);
        Assert.Equal("Blumenthal", Assert.Single(comps[1].Results).LastName);
    }

    [Fact]
    public void MastersEn_Relay_CreatedFromHebrewAgeLine_MixHeaderIgnored()
    {
        var pages = new[]
        {
            new[]
            {
                Title, DateRange, "Results",
                "4X50m Freestyle Relay",
                "120-159 תוחילש סרטסאמ",                // реверс «מאסטרס שליחות 120-159»
                "4 3 Macabbi Haifa 01:46.96 Rank 1",
                "2 7 Maccabi Olam Hamaim 01:47.85 Rank 2",
            },
            new[]
            {
                // Личная секция между эстафетами — как в реальном файле.
                Title, DateRange, "Results",
                "50m Breaststroke",
                "21-29 ג סרטסאמ",
                "1 1 3 GOOTMAN BAR 2003 ASA TECHNION 00:31.41 560",
            },
            new[]
            {
                Title, DateRange, "Results",
                // Заголовок с хвостом Mix НЕ создаёт события (зеркало HE, где «מיקס»
                // не матчится) — его команды молча пропускаются, включая пустую команду,
                // которая иначе валила парс личных результатов.
                "4X50m Medley Relay Mix",
                "1 5 aqvatikim 02:13.29 Rank 6",
                "1 1 02:19.73 Rank 7",
            },
        };

        var comps = IsrOrgCompetitionParser.ParseLines(pages, "en").ToList();

        Assert.Equal(2, comps.Count);
        var relay = comps[0];
        Assert.Equal("freestyle", relay.EventStyleName);
        Assert.Equal("4X50", relay.EventStyleLen);
        Assert.Equal("none", relay.EventStyleGender);
        Assert.Equal("120-159", relay.EventStyleAge);
        Assert.Equal(2, relay.Results.Count);
        Assert.All(relay.Results, r => Assert.True(r.IsRelay));
        Assert.Equal("Macabbi Haifa", relay.Results[0].RelayTeamName);
        // Личная секция цела, Mix-команды к ней не приклеились.
        var indiv = comps[1];
        Assert.Equal("breaststroke", indiv.EventStyleName);
        Assert.Equal("GOOTMAN", Assert.Single(indiv.Results).LastName);
    }

    [Fact]
    public void MastersHe_RelayHeaderNoAge_DoesNotDoubleYieldPreviousEvent()
    {
        // Баг: HE-заголовок эстафеты «без возраста» отдавал предыдущее событие дважды
        // (на заголовке и снова на строке возраста) — дубль ломал склейку HE+EN пары.
        var pages = new[]
        {
            new[]
            {
                "לארשי תופילא ARENA 2026 ףרוח סרטסאמ",
                DateRange, "תואצות",
                "יפוח 100",                              // реверс «100 חופשי» — simple header
                "21-29 נ סרטסאמ",
                "1 1 4 הנד יול 2000 םירבחה 01:10.10 300", // одна результат-строка
                "םיחילש יפוח 4X50",                       // реверс «4X50 חופשי שליחים» (без возраста)
                "120-159 תוחילש סרטסאמ",
            },
        };

        var comps = IsrOrgCompetitionParser.ParseLines(pages, "HE").ToList();

        // Личное событие ровно один раз + relay-событие.
        Assert.Equal(2, comps.Count);
        Assert.Equal("100", comps[0].EventStyleLen);
        Assert.Equal("4X50", comps[1].EventStyleLen);
    }
}
