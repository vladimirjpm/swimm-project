using System.Text.Json.Nodes;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Промежуточные личных заплывов из PDF «זמני ביניים» loglig (docs/relays.md,
/// «Промежуточные эстафет»). Фикстуры — зимний чемпионат мастерсов 2026 (loglig 13805):
/// 100 на спине ж (73036), 200 и 400 комплекс ж (73034, 73046).
/// </summary>
public class LogligIndividualSplitParserTests
{
    private static List<SplitSwim> Parse(string name)
    {
        using var fs = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", name));
        return LogligIndividualSplitParser.Parse(fs);
    }

    [Fact]
    public void Back100_LapsAreSegmentsNotCumulative()
    {
        var swims = Parse("loglig-split-indiv-13805-73036-women-100back.pdf");
        // Гостомельская 1:06.36: на 50 м 31.52, второй отрезок 34.84 (нарастающее 1:06.36 не берём).
        var own = Assert.Single(swims, s => s.Time == "01:06.36");
        Assert.Equal(1981, own.BirthYear);
        Assert.Equal(new[] { "31.52", "34.84" }, own.Laps);
    }

    [Fact]
    public void Im200_FourLaps()
    {
        var swims = Parse("loglig-split-indiv-13805-73034-women-200im.pdf");
        Assert.Equal(15, swims.Count);
        Assert.Equal(new[] { "32.45", "40.88", "48.96", "40.64" }, Assert.Single(swims, s => s.Time == "02:42.93").Laps);
    }

    [Fact]
    public void Im400_ColumnsLeftOfResultBound_AreRead()
    {
        // Колонки 400 и 350 стоят левее прежней границы «итог / отметки» — раньше терялись.
        var swims = Parse("loglig-split-indiv-13805-73046-women-400im.pdf");
        Assert.Equal(4, swims.Count);
        Assert.Equal(new[] { "38.38", "44.25", "47.09", "46.47", "53.39", "53.89", "42.42", "42.38" },
            Assert.Single(swims, s => s.Time == "06:08.27").Laps);
        // Кривая отметка «01:35.0» не мешает: отрезок 100 м берётся из строки отрезков.
        Assert.Equal("51.72", Assert.Single(swims, s => s.Time == "06:25.07").Laps[1]);
    }

    [Fact]
    public void Enricher_MatchesByTimeAndBirthYear_Only()
    {
        var json = new JsonArray(
            Row("01:06.36", 1981),
            Row("01:06.36", 1975),          // то же время, другой год — не она
            Row("01:10.91", 1990, relay: true)).ToJsonString();

        var ev = new IndividualSplitEvent("backstroke", "100", "female",
            Parse("loglig-split-indiv-13805-73036-women-100back.pdf"));
        var (outJson, _, enriched) = IndividualSplitEnricher.Apply(json, new[] { ev });

        Assert.Equal(1, enriched);
        var rows = JsonNode.Parse(outJson)!.AsArray();
        Assert.Equal("31.52;34.84", (string?)rows[0]!["time_split"]);
        Assert.Null(rows[1]!["time_split"]);
        Assert.Null(rows[2]!["time_split"]);
    }

    /// <summary>
    /// 800 м (6595, loglig 11586 / 58161, 07.10.2026): 16 отметок, шапка в ДВЕ строки
    /// («50…550» и «600…800»), времена второй полосы под теми же X, а отрезок 600 м не
    /// напечатан. Раньше бралась только первая строка шапки — ни одного пловца из 24.
    /// </summary>
    [Fact]
    public void Free800_HeaderInTwoRows_AllSixteenLaps()
    {
        var swims = Parse("loglig-split-indiv-11586-58161-men-800free.pdf");
        Assert.Equal(23, swims.Count);

        var own = Assert.Single(swims, s => s.Time == "09:25.48");
        Assert.Equal(1992, own.BirthYear);
        Assert.Equal(
            ["31.01", "34.45", "34.82", "35.87", "35.46", "35.37", "35.77", "35.77",
             "36.52", "36.01", "36.39", "36.41", "36.57", "36.54", "35.61", "32.91"],
            own.Laps);
        Assert.All(swims, s => Assert.Equal(16, s.Laps.Count));
    }

    /// <summary>
    /// Время с ОДНОЙ цифрой после точки («00:45.0») не читалось — сумма не сходилась, и пловец
    /// пропадал целиком (6595 / 58130: 27 из 29). Отрезки считаются разностью нарастающих.
    /// </summary>
    [Fact]
    public void OneDecimalTime_DoesNotDropTheSwimmer()
    {
        var swims = Parse("loglig-split-indiv-11586-58130-men-100fly.pdf");
        Assert.Equal(29, swims.Count);
        Assert.Equal(["35.89", "45.00"], Assert.Single(swims, s => s.Time == "01:20.89").Laps);
    }

    private static JsonObject Row(string time, int year, bool relay = false) => new()
    {
        ["is_relay"] = relay,
        ["event_style_name"] = "backstroke",
        ["event_style_len"] = "100",
        ["event_style_gender"] = "female",
        ["time"] = time,
        ["birth_year"] = year,
    };
}
