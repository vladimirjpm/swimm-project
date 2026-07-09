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
}
