using System.Text.Json.Nodes;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// PDF промежуточных одной эстафетной дисциплины loglig (docs/relays.md, «Промежуточные
/// эстафет»). Фикстуры — зимний чемпионат мастерсов 2026 (loglig 13805): женская 4×50
/// комплекс (73056) и микст 4×50 комплекс (73037). Оттуда же кейс, с которого всё началось:
/// рекорд 45-49 50 на спине 30.25 — это первый этап женской эстафеты Гостомельской.
/// </summary>
public class LogligRelaySplitParserTests
{
    private static List<SplitRelayTeam> Parse(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", name);
        using var fs = File.OpenRead(path);
        return LogligRelaySplitParser.Parse(fs);
    }

    private static List<SplitRelayTeam> Women => Parse("loglig-split-relay-13805-73056-women-medley.pdf");
    private static List<SplitRelayTeam> Mixed => Parse("loglig-split-relay-13805-73037-mixed-medley.pdf");

    [Fact]
    public void WomenMedley_LeadOffBackstrokeIs30_25()
    {
        var team = Assert.Single(Women, t => t.Time == "02:34.67");
        Assert.Equal((2, 8), (team.Heat, team.Lane));
        Assert.Equal("מכבי עולם המים", team.Club);

        Assert.Equal(4, team.Legs.Count);
        var lead = team.Legs[0];
        Assert.Equal((1, "גוסטמלסקי", "אניה", 1981, "00:30.25"),
            (lead.Order, lead.LastName, lead.FirstName, lead.BirthYear, lead.SplitTime));
        // Фамилия из двух слов — граница колонок, а не первое слово.
        Assert.Equal(("ברילר גולן", "דגנית"), (team.Legs[2].LastName, team.Legs[2].FirstName));
    }

    [Fact]
    public void WrappedNames_AreJoined()
    {
        var teams = Mixed;
        // Фамилия перенесена на строки ВЫШЕ и НИЖЕ ноги: «מרמור» / «סירוטה».
        var own = Assert.Single(teams, t => t.Time == "01:58.43");
        Assert.Equal("מרמור סירוטה", own.Legs[2].LastName);
        Assert.Equal(("גוסטמלסקי", "00:27.11"), (own.Legs[3].LastName, own.Legs[3].SplitTime));

        // Обрывок из одной буквы приклеивается к слову: «טיטינשנייד» + «ר».
        var shoham = Assert.Single(teams, t => t.Time == "02:17.37");
        Assert.Equal("טיטינשניידר", shoham.Legs[0].LastName);
        // Латиница читается слева направо, перенос «SHOHA» + «M».
        Assert.Equal(("BEN SHOHAM", "Shirli"), (shoham.Legs[3].LastName, shoham.Legs[3].FirstName));
    }

    [Fact]
    public void TeamSpanningPageBreak_KeepsItsLegs()
    {
        // Строка команды — последняя на странице 1, ноги — на странице 2.
        var team = Assert.Single(Mixed, t => t.Time == "01:58.43");
        Assert.Equal((1, 2), (team.Heat, team.Lane));
        Assert.Equal(new[] { "00:26.18", "00:29.23", "00:35.91", "00:27.11" }, team.Legs.Select(l => l.SplitTime));
    }

    [Fact]
    public void DisqualifiedOrSplitlessTeams_HaveNoLegs()
    {
        var dq = Assert.Single(Women, t => t.Time is null);
        Assert.Empty(dq.Legs);
        // Команда с итогом, но без промежуточных в источнике — без догадок.
        Assert.Empty(Assert.Single(Mixed, t => t.Time == "02:05.73").Legs);
    }

    [Fact]
    public void Enricher_MatchesByHeatLaneTime_AndChecksBirthYears()
    {
        var json = new JsonArray(
            Row("02:34.67", 2, 8, (1981, "קיגוסטמלס", "אניה"), (1990, "נוסם", "שירי"), (1981, "ברילר", "דגנית"), (1974, "קמר", "פלר")),
            // Та же дорожка и заплыв, другое время — чужая команда, не трогаем.
            Row("02:30.00", 2, 8, (1981, "x", "y"), (1990, "x", "y"), (1981, "x", "y"), (1974, "x", "y")),
            // Своя команда, но годы ног не сошлись — не трогаем.
            Row("02:24.29", 2, 4, (1990, "a", "b"), (1976, "a", "b"), (1998, "a", "b"), (1999, "a", "b"))
        ).ToJsonString();

        var (outJson, report) = RelaySplitEnricher.Apply(json,
            new[] { new RelaySplitEvent("individual_medley", "4X50", Women) });

        Assert.Equal(1, report.Enriched);
        Assert.Equal(1, report.BirthYearMismatch);

        var rows = JsonNode.Parse(outJson)!.AsArray();
        var legs = rows[0]!["relay_swimmers"]!.AsArray();
        Assert.Equal("גוסטמלסקי", (string?)legs[0]!["last_name"]);
        Assert.Equal("00:30.25", (string?)legs[0]!["split_time"]);
        // Владелец строки — первая нога, уже с целой фамилией.
        Assert.Equal(("גוסטמלסקי", "אניה", 1981),
            ((string?)rows[0]!["last_name"], (string?)rows[0]!["first_name"], (int?)rows[0]!["birth_year"]));
        Assert.Null((string?)rows[1]!["relay_swimmers"]![0]!["split_time"]);
        Assert.Equal("a", (string?)rows[2]!["relay_swimmers"]![0]!["last_name"]);
    }

    private static JsonObject Row(string time, int heat, int lane, params (int Year, string Last, string First)[] legs) => new()
    {
        ["is_relay"] = true,
        ["event_style_name"] = "individual_medley",
        ["event_style_len"] = "4X50",
        ["heat"] = heat,
        ["lane"] = lane,
        ["time"] = time,
        ["relay_swimmers"] = new JsonArray(legs.Select((l, i) => (JsonNode)new JsonObject
        {
            ["order"] = i + 1,
            ["last_name"] = l.Last,
            ["first_name"] = l.First,
            ["birth_year"] = l.Year,
            ["club"] = null,
            ["split_time"] = null,
        }).ToArray()),
    };
}
