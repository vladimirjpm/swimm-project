using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Parsing.Discovery;
using Swimm.Parsing.Parsers.IsrOrg;
using Swimm.Parsing.Parsers.Loglig;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Пустой PDF промежуточных при живых результатах (мастерс-зима 2025, loglig 11586, мужская
/// 4X50 комплекс 58140, 07.10.2026). Одна такая дисциплина роняла сборку мастерских эстафет
/// целиком: импорт откатывался на общий PDF — места сквозные, полос нет, клубные очки эстафет
/// 266 вместо официальных 2356. Теперь команды берутся со страницы результатов дисциплины, а
/// состав с годами — из протокола. Фикстура — живая страница 58140.
/// </summary>
public class LogligRelayResultsFallbackTests
{
    private const int DisciplineId = 58140;

    private static LogligEventResultsDto MenMedley()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "loglig",
            "event-58140-masters-relay-men-medley.html"));
        var ev = LogligEventResultsParser.Parse(html);
        return new LogligEventResultsDto(
            ev.CompetitionName, ev.Date, ev.StyleName, ev.Distance, ev.Gender, ev.AgeBand, ev.IsRelay,
            ev.Rows.Select(r => new LogligResultRowDto(
                r.Position, r.Round, r.Category, r.FullName, r.BirthYear, r.Club, r.Heat, r.Lane,
                r.Time, r.FailNote, r.InternationalPoints, r.PersonalPoints, r.ClubPoints)).ToList());
    }

    [Fact]
    public void TeamsFromResults_KeepLogligBands_NamesAndStatuses()
    {
        var teams = LogligRelaySplitProvider.TeamsFromResults(MenMedley().Rows);

        Assert.Equal(25, teams.Count);
        Assert.All(teams, t => Assert.Empty(t.Legs));
        Assert.All(teams, t => Assert.Equal(4, t.LegNames!.Count));
        Assert.All(teams, t => Assert.NotNull(t.Band));

        // Решение Влада 07.10.2026 (а): полоса как у loglig — даже личная пятилетка у трёх команд.
        var omer = Assert.Single(teams, t => t.Time == "01:49.75");
        Assert.Equal(("הפועל עומר", "30-34", 4, 1), (omer.Club, omer.Band, omer.Heat, omer.Lane));
        Assert.Equal("85-89", Assert.Single(teams, t => t.Time == "01:51.82").Band);
        Assert.Equal("120-159", Assert.Single(teams, t => t.Time == "02:00.66").Band);

        var ns = teams.Where(t => t.Time == null).ToList();
        Assert.Equal(2, ns.Count);
        Assert.All(ns, t => Assert.Equal("NS", t.Status));
    }

    [Fact]
    public void Builder_BorrowsLegsFromProtocol_PlacesInsideLogligBands()
    {
        var events = new[] { Event(Pick("01:49.75", "01:52.64", "01:51.82", null)) };
        var protocol = new JsonArray(
            Relay("הפועל עומר", "01:49.75", (1995, "סגל", "מתן"), (1998, "מטלון", "עומר"),
                (1998, "שמשון", "עמית"), (1997, "בץרבינוביץ", "טל")),
            Relay("מכבי עולם המים", "01:52.64", (1991, "קופלב", "יהונתן"), (1993, "בכמט", "דניאל"),
                (2000, "ישראלי", "עידו"), (1998, "פוגלמן", "עמרי")),
            // У ויקטור בלכסן в протоколе нет года — нога не попала в состав (хвост 6595).
            Relay("מכבי חיפה", "01:51.82", (1995, "סולניק", "גיא"), (1999, "שמואלי", "בן"), (1992, "כהן", "שגיא")),
            Relay("מועדון ספורט דניה", "", (1980, "קורובוב", "אריאל"), (1975, "לוין", "יורי"),
                (1978, "לוין", "דוד"), (1985, "נחשול", "שלומי")));

        var built = RelayMastersBuilder.Build(protocol.ToJsonString(), events)!.Value;
        Assert.Equal(4, built.Relays);

        var rows = JsonNode.Parse(built.Json)!.AsArray().OfType<JsonObject>().ToList();
        var omer = rows.Single(r => (string?)r["time"] == "01:49.75");
        Assert.Equal(("male", "30-34", "30", 1, "סגל", 1995),
            ((string?)omer["event_style_gender"], (string?)omer["age_group"], (string?)omer["event_style_age"],
             (int?)omer["position"], (string?)omer["last_name"], (int?)omer["birth_year"]));
        Assert.Equal(2, (int?)rows.Single(r => (string?)r["time"] == "01:52.64")["position"]);

        // Своя полоса «85-89» — первое место, как у loglig; состав из трёх ног протокола.
        var haifa = rows.Single(r => (string?)r["time"] == "01:51.82");
        Assert.Equal(("85-89", 1, 3), ((string?)haifa["age_group"], (int?)haifa["position"],
            haifa["relay_swimmers"]!.AsArray().Count));

        // NS — по именам ног, без места.
        var ns = rows.Single(r => (string?)r["club"] == "מועדון ספורט דניה");
        Assert.Equal(("NS", (int?)null, 4), ((string?)ns["time_fail_note"], (int?)ns["position"],
            ns["relay_swimmers"]!.AsArray().Count));
    }

    [Fact]
    public void Builder_DropsNoShowTeam_WhenTwoProtocolRowsMatchItsNames()
    {
        var events = new[] { Event(Pick("01:49.75", null)) };
        (int, string, string)[] legs = [(1980, "קורובוב", "אריאל"), (1975, "לוין", "יורי"), (1978, "לוין", "דוד")];
        var protocol = new JsonArray(
            Relay("הפועל עומר", "01:49.75", (1995, "סגל", "מתן"), (1998, "מטלון", "עומר"),
                (1998, "שמשון", "עמית"), (1997, "בץרבינוביץ", "טל")),
            Relay("מועדון ספורט דניה", "", legs), Relay("מועדון ספורט דניה", "", legs));

        var built = RelayMastersBuilder.Build(protocol.ToJsonString(), events)!.Value;
        // Какая строка чья — неизвестно: не строим, как NS без состава. Остальное собирается.
        Assert.Equal(1, built.Relays);
        Assert.DoesNotContain(JsonNode.Parse(built.Json)!.AsArray(),
            r => (string?)r!["club"] == "מועדון ספורט דניה");
    }

    [Fact]
    public async Task Provider_EmptySplitPdf_BuildsFromResultsPage()
    {
        var protocol = new JsonArray(
            Relay("הפועל עומר", "01:49.75", (1995, "סגל", "מתן"), (1998, "מטלון", "עומר"),
                (1998, "שמשון", "עמית"), (1997, "בץרבינוביץ", "טל")));
        var source = MenMedley() with { Rows = MenMedley().Rows.Where(r => r.Time == "01:49.75").ToList() };
        var provider = new LogligRelaySplitProvider(new FakeLoglig(source), NullLogger<LogligRelaySplitProvider>.Instance);

        var outcome = await provider.EnrichAsync(11586, protocol.ToJsonString());

        Assert.Equal(1, outcome.Enriched);
        Assert.Contains("со страницы результатов: 58140", outcome.Message);
        var row = JsonNode.Parse(outcome.ResultsJson)!.AsArray().Single()!;
        Assert.Equal(("male", "30-34"), ((string?)row["event_style_gender"], (string?)row["age_group"]));
    }

    /// <summary>Не мастерс (полос нет) — доклейке состава команды без ног не отдаём: JSON как был.</summary>
    [Fact]
    public async Task Provider_EmptySplitPdf_NotMasters_LeavesJsonUntouched()
    {
        var protocol = new JsonArray(Relay("הפועל עומר", "01:49.75", (1995, "סגל", "מתן"))).ToJsonString();
        var full = MenMedley();
        var source = full with
        {
            Rows = full.Rows.Where(r => r.Time == "01:49.75").Select(r => r with { Category = "בנים 13-14" }).ToList(),
        };
        var provider = new LogligRelaySplitProvider(new FakeLoglig(source), NullLogger<LogligRelaySplitProvider>.Instance);

        var outcome = await provider.EnrichAsync(11586, protocol);

        Assert.Equal(protocol, outcome.ResultsJson);
        Assert.Equal(0, outcome.Enriched);
    }

    private static List<SplitRelayTeam> Pick(params string?[] times) =>
        LogligRelaySplitProvider.TeamsFromResults(MenMedley().Rows)
            .Where(t => times.Contains(t.Time) && (t.Time != null || t.Club == "מועדון ספורט דניה"))
            .ToList();

    private static RelayBuildEvent Event(List<SplitRelayTeam> teams) =>
        new("individual_medley", "4X50", "male", "4X50 מעורב שליחים", teams);

    private static JsonObject Relay(string club, string time, params (int Year, string Last, string First)[] legs) => new()
    {
        ["is_relay"] = true,
        ["event_style_name"] = "individual_medley",
        ["event_style_len"] = "4X50",
        ["club"] = club,
        ["time"] = time,
        ["time_fail"] = time.Length == 0,
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

    private sealed class FakeLoglig(LogligEventResultsDto results) : ICompetitionDiscoveryProvider
    {
        public Task<IReadOnlyList<LogligDisciplineGridRowDto>> FetchDisciplineGridAsync(
            int logligId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LogligDisciplineGridRowDto>>(
            [
                new(DisciplineId, 12, "4X50 מעורב שליחים", "מאסטרס ג 21-99", "medley", "4X50",
                    "none", "21-99", true, null, new DateTime(2025, 1, 3, 12, 2, 0), 0, 0),
            ]);

        // Как у loglig на 58140: ответ 200 с пустым телом → провайдер бросает «не PDF».
        public Task<byte[]> FetchDisciplineSplitPdfAsync(int logligId, int disciplineId, CancellationToken ct = default) =>
            throw new InvalidOperationException($"loglig вернул не PDF промежуточных для дисциплины {disciplineId}");

        public Task<LogligEventResultsDto> FetchEventResultsAsync(int eventId, CancellationToken ct = default) =>
            eventId == DisciplineId ? Task.FromResult(results) : throw new NotSupportedException();

        public Task<IReadOnlyList<DiscoveredListItem>> FetchListAsync(bool finished, int? year = null, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<DiscoveredDetails> FetchDetailsAsync(int orgCompId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<byte[]> FetchResultsPdfAsync(int logligId, string culture = "he-IL", CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<int>> FetchEventIdsAsync(int logligId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<LogligStartListDto> FetchStartListAsync(int disciplineId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
