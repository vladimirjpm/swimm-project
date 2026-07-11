using System.Collections.Generic;
using System.Linq;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Регресс на класс багов "двуязычный PDF, разбираемый по HE-ветке":
/// англоязычный заголовок заплыва + перенос длинной фамилии.
/// См. memory/parser-bilingual-en-header-in-he.md.
///
/// Входные строки — это СЫРОЙ текст страницы (до RTL-реверса), ровно как его
/// выдаёт PdfPig и как он виден в debug-логе парсера (raw=...). ParseLines сам
/// нормализует (реверсит) строки, поэтому тест не требует PDF-фикстуры: чтобы
/// воспроизвести новый кейс — просто вставь сюда строки raw=... из лога.
/// </summary>
public class IsrOrgCompetitionParserTests
{
    // Страница 1 из "Maccabiah 2025 - Swimming", 50m Freestyle U17 Girls.
    // Заголовок заплыва на английском ("50m Freestyle - U17 Girls"), при этом
    // обвязка страницы на иврите => файл идёт по HE-ветке.
    private static readonly string[] MaccabiahPage1 =
    {
        "Maccabiah 2025 -",
        "Swimming",
        "OPEN & JUNIOR",
        "07/07/2026 - 05/07/2026",
        "תואצת",                                  // תואצות (реверс "תוצאות")
        "50m Freestyle - U17 Girls",
        "17:00 05/07/2026",
        "מיקום מקצה מסלול", // шапка таблицы (иврит)
        "4 3 1",
        "676 00:26.89 Israel 2009 Eva Goltsov",
        "613 00:27.79 Israel 2011 Mika SHTIFT 5 3 2",
        "575 00:28.38 Israel 2010 Ori COHEN 3 3 3",
        "567 00:28.51 Israel 2009 Tatyana MAHMOUD 6 3 4",
        "6 2 5",
        "560 00:28.64 USA 2009 Sabine BARRINGER",
        "538 00:29.02 USA 2009 Charlotte ROTH 7 3 6",
        "536 00:29.06 USA 2010 Eryn LEE 4 2 7",
        "535 00:29.08 USA 2009 Sienna SPODEK 2 3 8",
        "2 2 9",
        "531 00:29.14 Ukraine 2009 Mariia ELKINA",
        "509 00:29.57 M25 2010 Mia GOODRIDGE 9 3 10",
        "479 00:30.16 Germany 2012 Eliana STEIN 8 3 11",
        "471 00:30.34 Mexico 2013 Francis OVSEYEVITZ 1 3 12",
        "0 3 13",
        "447 00:30.86 Germany 2009 Isabelle STEIN",
        "307 00:34.99 Brazil 2009 Mirela BITRAN 5 2 14",
        "286 00:35.83 M25 2011 Tulip Awad 1 2 15",
        "279 00:36.12 M25 2011 Seren Abo Sah 8 2 16",
        "7 2 17",
        "252 00:37.37 M25 2009 Leen Keesh",
        "TSCHERKOWS",                                                     // фамилия перенесена (часть 1)
        "210 00:39.66 Germany 2013 Esther 3 2 18",                        // строка данных без фамилии
        "KI",                                                             // фамилия перенесена (часть 2)
        "208 00:39.79 M25 2012 Tatyana Makt 3 1 19",
        "5 1 20",
        "8 01:54.69 M25 2011 Masa Alwily",
        "8 01:57.11 USA 2011 Eden BRESSLER 4 1 21",
        "Powered By",
    };

    // Реальные строки со стр. 17 экспорта Maccabiah ARENA 2026 (masters).
    // Отличия от MaccabiahPage1: (1) английский заголовок заплыва БЕЗ пола/возраста
    // ("400m Freestyle"); (2) пол/возраст отдельной ивритской masters-строкой
    // ("21-29 ג סרטסאמ" => norm "מאסטרס ג 21-29"); (3) латиница результатов УЖЕ в
    // нормальном порядке токенов (rank heat lane FAMILY name year club time points) —
    // безусловный RTL-реверс её ломал => терялись все индивидуальные заплывы.
    // См. memory/parser-maccabiah-token-order.md.
    private static readonly string[] Maccabiah2026Page17 =
    {
        "לארשי תופילאו היבכמ ARENA 2026 ץיק סרטסאמל",
        "07/07/2026 - 09/07/2026",
        "Results",
        "400m Freestyle",
        "Year Of International",
        "Rank Heat Lane Last name First name Club Result",
        "Birth Score",
        "21-29 ג סרטסאמ",          // norm: מאסטרס ג 21-29 (male)
        "1 7 4 TURGEMAN Yarin 2003 Macabbi Kiryat Biyalik 04:27.96 553",
        "Maccabi Kfar",                                              // клуб-перенос (часть 1)
        "2 5 9 YADGAROV Oren 2005 05:42.68 264",                     // клуб пуст в строке данных
        "Hamaccabbiah",                                              // клуб-перенос (часть 2)
        "3 6 8 ARIEL Asaf 2004 Maccabi Olam Hamaim 05:48.00 252",
        "30-34 ג סרטסאמ",          // смена категории => norm: מאסטרס ג 30-34
        "1 7 5 COHEN Sagi 1992 Macabbi Haifa 04:39.74 486",
        "2 7 3",                                                     // место на отдельной строке
        "SALOMON Ori 1992 Macabbi Haifa 04:54.70 415",               // данные без места
    };

    private static IReadOnlyList<IsrOrgCompetitionResult> Parse(params string[][] pages) =>
        IsrOrgCompetitionParser.ParseLines(pages, "HE").ToList();

    [Fact]
    public void EnglishHeaderInHebrewFile_IsRecognized_AndAllResultsParsed()
    {
        var comps = Parse(MaccabiahPage1);

        var comp = Assert.Single(comps);
        Assert.Equal("freestyle", comp.EventStyleName);
        Assert.Equal("female", comp.EventStyleGender);
        Assert.Equal("50", comp.EventStyleLen);
        Assert.Equal("17", comp.EventStyleAge);
        Assert.Equal("05/07/2026", comp.Date);         // дата берётся из строки времени, не из "end date"
        Assert.Contains("Maccabiah", comp.Competition);

        // Раньше не парсилось НИ ОДНОГО результата (current == null). Должно быть 21.
        Assert.Equal(21, comp.Results.Count);
        Assert.Equal(Enumerable.Range(1, 21), comp.Results.Select(r => (int)r.Position!));
    }

    [Fact]
    public void EnglishNoGenderHeader_WithMastersLine_CreatesEvents_AndParsesRawOrientedResults()
    {
        // Заголовок без пола/возраста + masters-строка => два события (21-29 и 30-34),
        // оба male freestyle 400. Раньше не создавалось НИ ОДНОГО (0 competitions).
        var comps = Parse(Maccabiah2026Page17);

        Assert.Equal(2, comps.Count);
        Assert.All(comps, c =>
        {
            Assert.Equal("freestyle", c.EventStyleName);
            Assert.Equal("male", c.EventStyleGender);
            Assert.Equal("400", c.EventStyleLen);
        });

        var g1 = comps[0];
        Assert.Equal("21-29", g1.EventStyleAge);
        Assert.Equal(3, g1.Results.Count);
        Assert.Equal(Enumerable.Range(1, 3), g1.Results.Select(r => (int)r.Position!));

        var first = g1.Results[0];
        Assert.Equal("TURGEMAN", first.LastName);
        Assert.Equal("Yarin", first.FirstName);
        Assert.Equal(2003, first.BirthYear);
        Assert.Equal("Macabbi Kiryat Biyalik", first.Club);
        Assert.Equal("04:27.96", first.Time);
        Assert.Equal(553, first.InternationalPoints);
        Assert.Equal(7, first.Heat);
        Assert.Equal(4, first.Lane);

        // Смена категории по masters-строке.
        var g2 = comps[1];
        Assert.Equal("30-34", g2.EventStyleAge);
        Assert.Equal(2, g2.Results.Count);

        // Место на отдельной строке склеивается с данными (raw-ориентация).
        var salomon = g2.Results.Single(r => r.LastName == "SALOMON");
        Assert.Equal(2, (int)salomon.Position!);
        Assert.Equal("Ori", salomon.FirstName);
        Assert.Equal("04:54.70", salomon.Time);
        Assert.Equal(415, salomon.InternationalPoints);
    }

    [Fact]
    public void SplitPlaceLine_IsMergedWithData()
    {
        // Строка 1: место "1 3 4" лежит на отдельной строке от данных пловца.
        var first = Parse(MaccabiahPage1).Single().Results[0];

        Assert.Equal(1, (int)first.Position!);
        Assert.Equal("Eva", first.FirstName);
        Assert.Equal("Goltsov", first.LastName);
        Assert.Equal("00:26.89", first.Time);
        Assert.Equal(676, first.InternationalPoints);
        Assert.Equal(3, first.Heat);
        Assert.Equal(4, first.Lane);
    }

    [Fact]
    public void WrappedSurname_IsRecoveredFromAdjacentLines()
    {
        // Esther TSCHERKOWSKI (место 18): фамилия перенесена на строки до/после данных.
        var esther = Parse(MaccabiahPage1).Single().Results.Single(r => (int)r.Position! == 18);

        Assert.Equal("Esther", esther.FirstName);
        Assert.Equal("TSCHERKOWSKI", esther.LastName);
    }

    // ===== Чистый EN-экспорт (Maccabiah 2026, файл _IL_EN), EN-ветка =====
    // Реальные строки. Заголовок с гибкой категорией после тире ("- U17 Girls"),
    // строки результата в нормальном порядке (isHE=false => реверса нет).
    private static readonly string[] EnIndividualPage =
    {
        "Maccabiah 2025 -",
        "Swimming",
        "OPEN & JUNIOR",
        "05/07/2026 - 07/07/2026",
        "Results",
        "50m Freestyle - U17 Girls",
        "05/07/2026 17:00",
        "Year Of International",
        "Rank Heat Lane Last name First name Club Result",
        "Birth Score",
        "1 3 4 Goltsov Eva 2009 Israel 00:26.89 676",
        "2 3 5 SHTIFT Mika 2011 Israel 00:27.79 613",
        "4 3 6",                                          // место на отдельной строке
        "MAHMOUD Tatyana 2009 Israel 00:28.51 567",       // данные без места
    };

    // Реальная эстафетная страница (4X100m Freestyle Relay - U17 Girls).
    // Пловцы раздроблены по строкам с разным регистром — берём только команду.
    private static readonly string[] EnRelayPage =
    {
        "Maccabiah 2025 -",
        "Swimming",
        "OPEN & JUNIOR",
        "05/07/2026 - 07/07/2026",
        "Results",
        "4X100m Freestyle Relay - U17 Girls",
        "05/07/2026 19:33",
        "1 4 Israel 04:05.04 Rank 1",
        "Year",
        "Last First Reaction Total",
        "LERNE",
        "Hilla 2009",
        "R",
        "1 5 USA 04:13.76 Rank 2",
        "1 3 Maccabiah MIX 04:34.44 Rank 3",
    };

    private static IReadOnlyList<IsrOrgCompetitionResult> ParseEn(params string[][] pages) =>
        IsrOrgCompetitionParser.ParseLines(pages, "EN").ToList();

    [Fact]
    public void En_FlexibleHeader_ParsesIndividualEvent()
    {
        var comp = Assert.Single(ParseEn(EnIndividualPage));

        Assert.Equal("freestyle", comp.EventStyleName);
        Assert.Equal("female", comp.EventStyleGender);
        Assert.Equal("50", comp.EventStyleLen);
        Assert.Equal("17", comp.EventStyleAge);           // "U17" => "17"
        Assert.False(comp.EventStyleLen.Contains('X'));

        Assert.Equal(3, comp.Results.Count);
        var first = comp.Results[0];
        Assert.Equal("Goltsov", first.LastName);
        Assert.Equal("Eva", first.FirstName);
        Assert.Equal("00:26.89", first.Time);
        Assert.Equal(676, first.InternationalPoints);

        // Место на отдельной строке склеивается с данными.
        var mahmoud = comp.Results.Single(r => r.LastName == "MAHMOUD");
        Assert.Equal(4, (int)mahmoud.Position!);
        Assert.Equal("00:28.51", mahmoud.Time);
    }

    [Fact]
    public void En_RelayHeader_ParsesTeamStandings()
    {
        var comp = Assert.Single(ParseEn(EnRelayPage));

        Assert.Equal("4X100", comp.EventStyleLen);
        Assert.Equal("freestyle", comp.EventStyleName);
        Assert.Equal("female", comp.EventStyleGender);

        Assert.Equal(3, comp.Results.Count);
        Assert.All(comp.Results, r => Assert.True(r.IsRelay));

        var gold = comp.Results.Single(r => (int)r.Position! == 1);
        Assert.Equal("Israel", gold.RelayTeamName);
        Assert.Equal("04:05.04", gold.Time);

        var bronze = comp.Results.Single(r => (int)r.Position! == 3);
        Assert.Equal("Maccabiah MIX", bronze.RelayTeamName);
    }
}
