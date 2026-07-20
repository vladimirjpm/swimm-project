using System.Collections.Generic;
using System.Linq;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;
using PW = Swimm.Parsing.Parsers.IsrOrg.IsrOrgCompetitionParser.PositionedWord;

namespace Swimm.Tests;

/// <summary>
/// Реконструкция перенесённых имён/фамилий эстафетной таблицы EN-экспорта
/// (Maccabiah). Данные — реальные X/Y-координаты слов со стр. 17 PDF
/// "Maccabiah-2026_IL_EN.pdf" (событие "4X100m Freestyle Relay - U17 Girls",
/// команда Israel), полученные напрямую через UglyToad.PdfPig (page.GetWords()),
/// т.е. это ровно то, что видит production-конвейер парсера.
///
/// В сыром виде фамилии/имена, не помещающиеся в узкую колонку таблицы,
/// переносятся на СОСЕДНЮЮ Y-строку одним словом-обрывком:
///   "LERNE" / "Hilla 2009" / "R"        => LERNER, Hilla, 2009
///   "Zhukovs" / "Alina 2011" / "kyy"    => Zhukovskyy, Alina, 2011
/// а также случай переноса ИМЕНИ (не фамилии):
///   "Charlott" / "ROTH 2009" / "e"      => ROTH, Charlotte, 2009
/// Комментарий "нельзя реконструировать" в IsrOrgCompetitionParser.cs был неверным —
/// эти тесты фиксируют, что реконструкция по X-координатам колонок работает.
/// </summary>
public class IsrOrgCompetitionParserRelayNameReconstructionTests
{
    // Заголовок таблицы (повторяется перед каждой командой в реальном PDF).
    private static List<PW> HeaderLastFirst() => new()
    {
        new PW("Last", 32.1),
        new PW("First", 72.9),
        new PW("Reaction", 137.6),
        new PW("Total", 393.7),
    };

    [Fact]
    public void SurnameWrapsAroundRow_IsReassembled()
    {
        // "LERNE" / "Hilla 2009" / "R" => LERNER Hilla 2009
        var groups = new List<List<PW>>
        {
            HeaderLastFirst(),
            new() { new PW("LERNE", 24.9) },
            new() { new PW("Hilla", 71.1), new PW("2009", 103.0) },
            new() { new PW("R", 36.7) },
        };
        var lines = new List<string> { "Last First Reaction Total", "LERNE", "Hilla 2009", "R" };

        var result = IsrOrgCompetitionParser.ReconstructEnRelaySwimmerNames(groups, lines);

        Assert.Equal(new[] { "Last First Reaction Total", "LERNER Hilla 2009" }, result);
    }

    [Fact]
    public void FirstNameWrapsAroundRow_IsReassembled()
    {
        // "Charlott" / "ROTH 2009" / "e" => ROTH Charlotte 2009
        var groups = new List<List<PW>>
        {
            HeaderLastFirst(),
            new() { new PW("Charlott", 64.1) },
            new() { new PW("ROTH", 27.2), new PW("2009", 103.0) },
            new() { new PW("e", 77.3) },
        };
        var lines = new List<string> { "Last First Reaction Total", "Charlott", "ROTH 2009", "e" };

        var result = IsrOrgCompetitionParser.ReconstructEnRelaySwimmerNames(groups, lines);

        Assert.Equal(new[] { "Last First Reaction Total", "ROTH Charlotte 2009" }, result);
    }

    [Fact]
    public void SurnameWrapsOnBothSides_IsReassembled()
    {
        // "LEEBER" / "Dalia 2009" / "MAN" => LEEBERMAN Dalia 2009 (prefix + suffix)
        var groups = new List<List<PW>>
        {
            HeaderLastFirst(),
            new() { new PW("LEEBER", 22.0) },
            new() { new PW("Dalia", 68.0), new PW("2009", 103.0) },
            new() { new PW("MAN", 30.0) },
        };
        var lines = new List<string> { "Last First Reaction Total", "LEEBER", "Dalia 2009", "MAN" };

        var result = IsrOrgCompetitionParser.ReconstructEnRelaySwimmerNames(groups, lines);

        Assert.Equal(new[] { "Last First Reaction Total", "LEEBERMAN Dalia 2009" }, result);
    }

    [Fact]
    public void UnwrappedRow_IsLeftAsSingleLine()
    {
        // "LEE Eryn 2010" — оба имени короткие, помещаются в одну строку без переноса.
        var groups = new List<List<PW>>
        {
            HeaderLastFirst(),
            new() { new PW("LEE", 31.4), new PW("Eryn", 70.6), new PW("2010", 103.0) },
        };
        var lines = new List<string> { "Last First Reaction Total", "LEE Eryn 2010" };

        var result = IsrOrgCompetitionParser.ReconstructEnRelaySwimmerNames(groups, lines);

        Assert.Equal(lines, result);
    }

    [Fact]
    public void FullIsraelTeamBlock_AllFourLegsReassembled()
    {
        // Полный блок реальных строк со стр. 17 (команда Israel, 4 ноги).
        var groups = new List<List<PW>>
        {
            new() { new PW("1", 42.2), new PW("4", 92.6), new PW("Israel", 183.2), new PW("04:05.04", 274.3), new PW("Rank", 526.5), new PW("1", 555.3) },
            HeaderLastFirst(),
            new() { new PW("LERNE", 24.9) },
            new() { new PW("Hilla", 71.1), new PW("2009", 103.0) },
            new() { new PW("R", 36.7) },
            new() { new PW("Zhukovs", 22.9) },
            new() { new PW("Alina", 69.8), new PW("2011", 103.0) },
            new() { new PW("kyy", 33.2) },
            new() { new PW("Harfenis", 23.2) },
            new() { new PW("Zohar", 68.1), new PW("2009", 103.0) },
            new() { new PW("t", 38.7) },
            new() { new PW("MAHMO", 22.7) },
            new() { new PW("Tatyana", 63.6), new PW("2009", 103.0) },
            new() { new PW("UD", 33.4) },
            new() { new PW("1", 42.2), new PW("5", 92.6), new PW("USA", 185.3), new PW("04:13.76", 274.3), new PW("Rank", 526.5), new PW("2", 555.3) },
        };
        var lines = new List<string>
        {
            "1 4 Israel 04:05.04 Rank 1",
            "Last First Reaction Total",
            "LERNE", "Hilla 2009", "R",
            "Zhukovs", "Alina 2011", "kyy",
            "Harfenis", "Zohar 2009", "t",
            "MAHMO", "Tatyana 2009", "UD",
            "1 5 USA 04:13.76 Rank 2",
        };

        var result = IsrOrgCompetitionParser.ReconstructEnRelaySwimmerNames(groups, lines);

        Assert.Equal(new[]
        {
            "1 4 Israel 04:05.04 Rank 1",
            "Last First Reaction Total",
            "LERNER Hilla 2009",
            "Zhukovskyy Alina 2011",
            "Harfenist Zohar 2009",
            "MAHMOUD Tatyana 2009",
            "1 5 USA 04:13.76 Rank 2",
        }, result);
    }

    [Fact]
    public void SeedColumns_ReconstructsContinuationPageWithNoHeaderRow()
    {
        // Разрыв страницы посреди состава команды: продолжение (2 ноги) идёт с
        // начала СЛЕДУЮЩЕЙ страницы без повторной шапки "Last First ...".
        // Колонки Last/First переданы через seedLastColX/seedFirstColX — как их
        // передаёт ParseCompetitionsInternal, перенося lastColX/firstColX с
        // предыдущей relay-страницы. Данные — реальный кейс "Maccabiah MIX"
        // 4X50, comp 1484: DABBAH (обрыв "DABBA"/"H") и ACUNA (обрыв
        // "Constan"/"za" в имени) на второй странице.
        var groups = new List<List<PW>>
        {
            new() { new PW("DABBA", 22.7) },
            new() { new PW("Alan", 63.6), new PW("2008", 103.0) },
            new() { new PW("H", 33.4) },
            new() { new PW("Constan", 68.0) },
            new() { new PW("ACUNA", 24.0), new PW("2006", 103.0) },
            new() { new PW("za", 76.0) },
        };
        var lines = new List<string>
        {
            "DABBA", "Alan 2008", "H",
            "Constan", "ACUNA 2006", "za",
        };

        var result = IsrOrgCompetitionParser.ReconstructEnRelaySwimmerNames(
            groups, lines, seedLastColX: 32.1, seedFirstColX: 72.9,
            out var finalLastColX, out var finalFirstColX);

        Assert.Equal(new[] { "DABBAH Alan 2008", "ACUNA Constanza 2006" }, result);
        // Шапки на этой странице не было -> колонки не должны обновиться относительно seed.
        Assert.Equal(32.1, finalLastColX);
        Assert.Equal(72.9, finalFirstColX);
    }

    [Fact]
    public void NoSeedAndNoHeader_LeavesLinesUntouched()
    {
        // Без seed-колонок и без шапки на странице реконструкция не должна
        // ничего трогать (иначе рискуем ложно сработать на обычной таблице).
        var groups = new List<List<PW>>
        {
            new() { new PW("DABBA", 22.7) },
            new() { new PW("Alan", 63.6), new PW("2008", 103.0) },
            new() { new PW("H", 33.4) },
        };
        var lines = new List<string> { "DABBA", "Alan 2008", "H" };

        var result = IsrOrgCompetitionParser.ReconstructEnRelaySwimmerNames(groups, lines);

        Assert.Equal(lines, result);
    }

    [Fact]
    public void DoubleWrap_BothLastAndFirstBreakOnSameFollowingLine_IsReassembled()
    {
        // Реальный кейс со стр. 18 PDF (координаты — реальные, из
        // page.GetWords()): "STRIMO Jonatha" / "2010" / "VSKY n" ->
        // Jonathan STRIMOVSKY. И Last (STRIMO+VSKY), И First (Jonatha+n)
        // переносятся ОДНОВРЕМЕННО: префикс-обрывки ОБЕИХ колонок стоят на
        // одной Y-строке ДО года, суффикс-обрывки ОБЕИХ колонок — на одной
        // Y-строке ПОСЛЕ (год печатается совсем один, без Last/First вообще).
        // Раньше это не чинилось (TryFillFromFragmentGroup принимала только
        // группы из одного слова).
        var groups = new List<List<PW>>
        {
            HeaderLastFirst(),
            new() { new PW("STRIMO", 22.5), new PW("Jonatha", 63.8) },
            new() { new PW("2010", 103.0) },
            new() { new PW("VSKY", 27.9), new PW("n", 77.3) },
        };
        var lines = new List<string> { "Last First Reaction Total", "STRIMO Jonatha", "2010", "VSKY n" };

        var result = IsrOrgCompetitionParser.ReconstructEnRelaySwimmerNames(groups, lines);

        Assert.Equal(new[] { "Last First Reaction Total", "STRIMOVSKY Jonathan 2010" }, result);
    }

    [Fact]
    public void DoubleWrap_RosenthalExample_IsReassembled()
    {
        // "ROSEN Frederic" / "2008" / "THAL k" -> Frederick ROSENTHAL.
        var groups = new List<List<PW>>
        {
            HeaderLastFirst(),
            new() { new PW("ROSEN", 22.9), new PW("Frederic", 62.5) },
            new() { new PW("2008", 103.0) },
            new() { new PW("THAL", 28.4), new PW("k", 76.5) },
        };
        var lines = new List<string> { "Last First Reaction Total", "ROSEN Frederic", "2008", "THAL k" };

        var result = IsrOrgCompetitionParser.ReconstructEnRelaySwimmerNames(groups, lines);

        Assert.Equal(new[] { "Last First Reaction Total", "ROSENTHAL Frederick 2008" }, result);
    }

    [Fact]
    public void DoubleWrap_SpieglerExample_IsReassembled()
    {
        // "SPIEGL Benjami" / "2008" / "ER n" -> Benjamin SPIEGLER.
        var groups = new List<List<PW>>
        {
            HeaderLastFirst(),
            new() { new PW("SPIEGL", 23.1), new PW("Benjami", 64.2) },
            new() { new PW("2008", 103.0) },
            new() { new PW("ER", 33.6), new PW("n", 78.0) },
        };
        var lines = new List<string> { "Last First Reaction Total", "SPIEGL Benjami", "2008", "ER n" };

        var result = IsrOrgCompetitionParser.ReconstructEnRelaySwimmerNames(groups, lines);

        Assert.Equal(new[] { "Last First Reaction Total", "SPIEGLER Benjamin 2008" }, result);
    }

    [Fact]
    public void DoubleWrap_AmbiguousTwoWordSuffixGroup_SuffixGroupRejected()
    {
        // Двухсловная соседняя группа-суффикс, где ОБА слова близки к ОДНОЙ и
        // той же (last) колонке — не пристыковывается однозначно ни First, ни
        // второй Last-обрывок, поэтому вся группа-суффикс бракуется целиком
        // (не додумываем, какое из двух слов верное). Префикс с ПРЕДЫДУЩЕЙ
        // строки (обе колонки) при этом всё ещё честно применяется независимо
        // от отбракованного суффикса.
        var groups = new List<List<PW>>
        {
            HeaderLastFirst(),
            new() { new PW("STRIMO", 22.5), new PW("Jonatha", 63.8) },
            new() { new PW("2010", 103.0) },
            new() { new PW("VSKY", 28.0), new PW("ZZZ", 26.0) }, // оба у last-колонки
        };
        var lines = new List<string> { "Last First Reaction Total", "STRIMO Jonatha", "2010", "VSKY ZZZ" };

        var result = IsrOrgCompetitionParser.ReconstructEnRelaySwimmerNames(groups, lines);

        // Last и First собрались только из префикса (STRIMO/Jonatha), суффикс-
        // группа отклонена целиком и осталась отдельной нетронутой строкой.
        Assert.Equal(new[] { "Last First Reaction Total", "STRIMO Jonatha 2010", "VSKY ZZZ" }, result);
    }

    [Fact]
    public void AmbiguousColumnDistance_LeavesLineUntouched()
    {
        // Обрывок ровно посередине между колонками (|dLast-dFirst| < 3) — не рискуем.
        var groups = new List<List<PW>>
        {
            HeaderLastFirst(),
            new() { new PW("Frag", 52.5) }, // равноудалён от lastColX=32.1 и firstColX=72.9
            new() { new PW("Zohar", 68.1), new PW("2009", 103.0) },
        };
        var lines = new List<string> { "Last First Reaction Total", "Frag", "Zohar 2009" };

        var result = IsrOrgCompetitionParser.ReconstructEnRelaySwimmerNames(groups, lines);

        // Строка с годом уже полная (Last отсутствует, но неоднозначный обрывок не трогаем) =>
        // last остаётся пустым => реконструкция для этой строки не применяется, всё как было.
        Assert.Equal(lines, result);
    }
}

// Оборачиваем end-to-end проверку через ParseLines, используя уже реконструированные
// строки — так тестируется и сама реконструкция, и её интеграция с существующей
// логикой парсинга состава эстафеты (RelaySwimmersName/RelaySwimmers).
public class IsrOrgCompetitionParserEnRelayLegsTests
{
    private static readonly string[] EnRelayPageWithLegs =
    {
        "Maccabiah 2025 -",
        "Swimming",
        "OPEN & JUNIOR",
        "05/07/2026 - 07/07/2026",
        "Results",
        "4X100m Freestyle Relay - U17 Girls",
        "05/07/2026 19:33",
        "1 4 Israel 04:05.04 Rank 1",
        "Last First Reaction Total",
        "LERNER Hilla 2009",           // как выглядит ПОСЛЕ ReconstructEnRelaySwimmerNames
        "Zhukovskyy Alina 2011",
        "Harfenist Zohar 2009",
        "MAHMOUD Tatyana 2009",
        "1 5 USA 04:13.76 Rank 2",
        "1 3 Maccabiah MIX 04:34.44 Rank 3",
    };

    [Fact]
    public void ReconstructedLegLines_PopulateRelaySwimmersName()
    {
        var comp = Assert.Single(IsrOrgCompetitionParser.ParseLines(new[] { EnRelayPageWithLegs }, "EN").ToList());

        var israel = comp.Results.Single(r => (int)r.Position! == 1);
        Assert.Equal("Israel", israel.RelayTeamName);
        Assert.NotNull(israel.RelaySwimmers);
        Assert.Equal(4, israel.RelaySwimmers!.Count);
        Assert.Equal(new[] { "LERNER", "Zhukovskyy", "Harfenist", "MAHMOUD" },
            israel.RelaySwimmers.Select(s => s.LastName));
        Assert.Equal(new[] { "Hilla", "Alina", "Zohar", "Tatyana" },
            israel.RelaySwimmers.Select(s => s.FirstName));
        Assert.Equal(new int?[] { 2009, 2011, 2009, 2009 },
            israel.RelaySwimmers.Select(s => s.BirthYear));
        Assert.Equal("Hilla LERNER, Alina Zhukovskyy, Zohar Harfenist, Tatyana MAHMOUD",
            israel.RelaySwimmersName);

        // Команды без реконструированных строк-пловцов (USA/Maccabiah MIX в этой фикстуре)
        // консервативно остаются с null-составом, а не мусором.
        var usa = comp.Results.Single(r => (int)r.Position! == 2);
        Assert.Null(usa.RelaySwimmersName);
        Assert.Null(usa.RelaySwimmers);
    }

    [Fact]
    public void OnlyThreeLegsPrintedInSource_TeamStaysNull_NamesNeverInvented()
    {
        // Реальный кейс comp 1484 4X50 "Maccabiah MIX" 02:07.12: источник
        // печатает ТОЛЬКО три ноги для этой команды (в PDF нет данных на
        // четвёртую) — состав должен остаться null, а не собраться из трёх
        // реально распознанных строк как будто это полная четвёрка.
        string[] page =
        {
            "Maccabiah 2025 -",
            "Swimming",
            "OPEN & JUNIOR",
            "05/07/2026 - 07/07/2026",
            "Results",
            "4X50m Freestyle Mix - MIX 18-99",
            "06/07/2026 17:40",
            "1 4 Maccabiah MIX 02:07.12 Rank 4",
            "Last First Reaction Total",
            "BITRAN Mirela 2009",
            "GANT Zoe 2009",
            "GOODRIDGE Mia 2009",
            "Powered By",
        };

        var comp = Assert.Single(IsrOrgCompetitionParser.ParseLines(new[] { page }, "EN").ToList());
        var team = Assert.Single(comp.Results);

        Assert.Equal("Maccabiah MIX", team.RelayTeamName);
        Assert.Null(team.RelaySwimmersName);
        Assert.Null(team.RelaySwimmers);
    }
}

// Разрыв команды эстафеты по границе страницы (см. IsrOrgCompetitionParserMaccabiahRealPdfTests
// и комментарии "разрыв страницы" в IsrOrgCompetitionParser.cs): первые 1-3 ноги — на одной
// странице PDF, остаток — в начале следующей, без повтора командной строки/шапки колонок.
// Реальный кейс — "Maccabiah MIX" 4X50, comp 1484, 01:57.31: HARAS/BENTES на первой странице,
// DABBAH ("DABBA"/"H")/ACUNA (имя "Constan"/"za") — на второй.
public class IsrOrgCompetitionParserRelaySplitAcrossPageBreakTests
{
    private static readonly string[] Page1 =
    {
        "Maccabiah 2025 -",
        "Swimming",
        "OPEN & JUNIOR",
        "05/07/2026 - 07/07/2026",
        "Results",
        "4X50m Freestyle Mix - MIX 18-99",
        "06/07/2026 17:40",
        "1 4 Maccabiah MIX 01:57.31 Rank 4",
        "Last First Reaction Total",
        "HARAS Alan 2008",
        "BENTES Luana 2007",   // как выглядит ПОСЛЕ ReconstructEnRelaySwimmerNames
    };

    private static readonly string[] Page2WithLegsOnly =
    {
        // Продолжение состава без повторной шапки таблицы и без командной строки —
        // ровно то, что видит ReconstructEnRelaySwimmerNames ПОСЛЕ реконструкции
        // (перенос колонок Last/First сделан вызывающим кодом в
        // ParseCompetitionsInternal, здесь тестируем уже готовые строки).
        "DABBAH Alan 2008",
        "ACUNA Constanza 2006",
        "Powered By",
    };

    [Fact]
    public void FourthLeg_SplitAcrossPageBreak_IsCollectedFromNextPage()
    {
        var comp = Assert.Single(
            IsrOrgCompetitionParser.ParseLines(new[] { Page1, Page2WithLegsOnly }, "EN").ToList());

        var team = Assert.Single(comp.Results);
        Assert.Equal("Maccabiah MIX", team.RelayTeamName);
        Assert.NotNull(team.RelaySwimmers);
        Assert.Equal(4, team.RelaySwimmers!.Count);
        Assert.Equal(new[] { "HARAS", "BENTES", "DABBAH", "ACUNA" },
            team.RelaySwimmers.Select(s => s.LastName));
        Assert.Equal(new[] { "Alan", "Luana", "Alan", "Constanza" },
            team.RelaySwimmers.Select(s => s.FirstName));
    }

    [Fact]
    public void FourthLeg_SplitAcrossPageBreak_ConsumedNextPageLinesAreNotReprocessed()
    {
        // "DABBAH Alan 2008" и "ACUNA Constanza 2006" не должны попасть в результат
        // ещё раз как самостоятельные строки — основной цикл разбора страницы 2
        // обязан пропустить уже "съеденные" строки состава.
        var comp = Assert.Single(
            IsrOrgCompetitionParser.ParseLines(new[] { Page1, Page2WithLegsOnly }, "EN").ToList());

        Assert.Single(comp.Results); // одна командная строка, не дублировалась и не расползлась
    }

    [Fact]
    public void MissingContinuation_NextPageIsUnrelatedEvent_StaysNull()
    {
        // Если после разрыва страницы следующая страница НЕ является продолжением
        // состава (сразу другое событие) — состав остаётся null, а не мусором
        // из первых слов чужого события (см. "не строка ноги — стоп").
        string[] unrelatedNextPage =
        {
            "400m Freestyle - U17 Girls",
            "06/07/2026 09:28",
            "Rank Heat Lane Last name First name Year Of Birth Club Result International Score",
            "1 2 4 JAVER Samara 2011 USA 04:37.03 604",
        };

        var comps = IsrOrgCompetitionParser.ParseLines(new[] { Page1, unrelatedNextPage }, "EN").ToList();
        var relayComp = comps.Single(c => c.Event.Contains("Freestyle Mix"));

        var team = Assert.Single(relayComp.Results);
        Assert.Null(team.RelaySwimmers);
        Assert.Null(team.RelaySwimmersName);
    }
}
