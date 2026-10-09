using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Эталоны разбора: каждая PDF-фикстура парсеров → сохранённый JSON того, что парсер из неё
/// достаёт, и тест сверяет разбор с ним побайтно (с точностью до конца строки). Страховка перед
/// делением <c>IsrOrgCompetitionParser.ParseLines</c> (docs/plans/refactor-handoff.md, шаги 4 и 7):
/// любой сдвиг в выдаче — красный тест с первой отличающейся строкой.
///
/// Эталон сменился НАМЕРЕННО (починили парсер) — пересоздать и посмотреть дифф в git:
/// <c>SWIMM_UPDATE_GOLDEN=1 dotnet test --filter ParserGoldenTests</c>.
/// Новая PDF в Fixtures/Parsing — строка в <see cref="ProtocolFiles"/> / <see cref="SplitFiles"/> или в
/// <see cref="NotCovered"/>, иначе падает <see cref="EveryParsingFixture_HasGolden"/>.
/// </summary>
public class ParserGoldenTests
{
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing");

    // Протоколы соревнований → IsrOrgCompetitionParser.ParseCompetitions; язык — как у импорта.
    private static readonly (string Pdf, string Language)[] ProtocolFiles =
    {
        ("ArenaCenterBoys2026_57_wrapped_names_EN.pdf", "EN"),
        ("ArenaCenterBoys2026_57_wrapped_names_HE.pdf", "HE"),
        ("Maccabiah-2026_IL_EN.pdf", "EN"),
        ("Maccabiah-2026_IL_HE.pdf", "HE"),
        ("MastersWinter2025_6595_relay_then_individual_HE.pdf", "HE"),
        ("OpenLeague2Haifa2025_1658_doubled_rows_HE.pdf", "HE"),
        ("RodovskyNimri2025_1720_ns_startlist_HE.pdf", "HE"),
        ("isr-3km-2026.pdf", "HE"),
        ("isr-maccabi-spring-2026-senior-he.pdf", "HE"),
        ("isr-maccabi-spring-2026-youth-he.pdf", "HE"),
        ("isr-open-water-2026.pdf", "HE"),
    };

    public static TheoryData<string, string> Protocols()
    {
        var data = new TheoryData<string, string>();
        foreach (var (pdf, language) in ProtocolFiles) data.Add(pdf, language);
        return data;
    }

    // PDF промежуточных loglig → LogligRelaySplitParser / LogligIndividualSplitParser (по имени).
    private static readonly string[] SplitFiles =
    {
        "loglig-split-indiv-11586-58130-men-100fly.pdf",
        "loglig-split-indiv-11586-58161-men-800free.pdf",
        "loglig-split-indiv-13805-73034-women-200im.pdf",
        "loglig-split-indiv-13805-73036-women-100back.pdf",
        "loglig-split-indiv-13805-73046-women-400im.pdf",
        "loglig-split-relay-11586-58158-women-free.pdf",
        "loglig-split-relay-13805-73027-men-freestyle.pdf",
        "loglig-split-relay-13805-73037-mixed-medley.pdf",
        "loglig-split-relay-13805-73056-women-medley.pdf",
        "loglig-split-relay-14032-75021-openwater-mixed.pdf",
        "loglig-split-relay-15155-82534-men-free.pdf",
        "loglig-split-relay-15155-82535-men-medley.pdf",
    };

    public static TheoryData<string> Splits() => new(SplitFiles);

    // Справочники рекордов — другой парсер (IsrOrgAgeRecordsParser), к делению ParseLines отношения не имеют.
    private static readonly string[] NotCovered =
    {
        "isr-age-records-25m.pdf", "isr-age-records-50m.pdf", "isr-age-records-50m-2026-09.pdf",
    };

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        // Иврит и кавычки — как есть: эталон читают глазами в диффе.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Theory]
    [MemberData(nameof(Protocols))]
    public void Protocol_ParsesAsGolden(string pdf, string language)
    {
        using var fs = File.OpenRead(Path.Combine(FixtureDir, pdf));
        var parsed = IsrOrgCompetitionParser.ParseCompetitions(fs, language).ToList();
        AssertGolden(pdf, JsonSerializer.Serialize(parsed, Json));
    }

    [Theory]
    [MemberData(nameof(Splits))]
    public void Split_ParsesAsGolden(string pdf)
    {
        using var fs = File.OpenRead(Path.Combine(FixtureDir, pdf));
        var json = pdf.Contains("-relay-")
            ? JsonSerializer.Serialize(LogligRelaySplitParser.Parse(fs), Json)
            : JsonSerializer.Serialize(LogligIndividualSplitParser.Parse(fs), Json);
        AssertGolden(pdf, json);
    }

    [Fact]
    public void EveryParsingFixture_HasGolden()
    {
        var covered = ProtocolFiles.Select(p => p.Pdf).Concat(SplitFiles).Concat(NotCovered);
        var missing = Directory.GetFiles(FixtureDir, "*.pdf").Select(Path.GetFileName).Except(covered).ToList();
        Assert.True(missing.Count == 0,
            "PDF-фикстура без эталона разбора: " + string.Join(", ", missing) +
            ". Добавь её в Protocols/Splits (или NotCovered) и пересоздай эталоны (SWIMM_UPDATE_GOLDEN=1).");
    }

    private static void AssertGolden(string pdf, string actual, [CallerFilePath] string thisFile = "")
    {
        actual = actual.Replace("\r\n", "\n") + "\n";
        var name = Path.ChangeExtension(pdf, ".json");

        if (Environment.GetEnvironmentVariable("SWIMM_UPDATE_GOLDEN") == "1")
        {
            var srcDir = Path.Combine(Path.GetDirectoryName(thisFile)!, "Fixtures", "Parsing", "golden");
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, name), actual);
            return;
        }

        var goldenPath = Path.Combine(FixtureDir, "golden", name);
        Assert.True(File.Exists(goldenPath), $"Нет эталона {name} — пересоздай: SWIMM_UPDATE_GOLDEN=1.");
        var expected = File.ReadAllText(goldenPath).Replace("\r\n", "\n");
        if (expected == actual) return;

        var actualPath = Path.Combine(AppContext.BaseDirectory, name.Replace(".json", ".actual.json"));
        File.WriteAllText(actualPath, actual);
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        var i = 0;
        while (i < e.Length && i < a.Length && e[i] == a[i]) i++;
        Assert.Fail($"Разбор {pdf} разошёлся с эталоном в строке {i + 1}:\n" +
                    $"  эталон: {(i < e.Length ? e[i].Trim() : "<конец>")}\n" +
                    $"  сейчас: {(i < a.Length ? a[i].Trim() : "<конец>")}\n" +
                    $"Весь разбор: {actualPath}");
    }
}
