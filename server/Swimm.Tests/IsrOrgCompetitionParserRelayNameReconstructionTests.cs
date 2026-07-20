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
}
