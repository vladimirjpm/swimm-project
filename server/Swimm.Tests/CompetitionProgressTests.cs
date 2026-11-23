using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Application.Validation;
using Swimm.Domain;
using Swimm.Parsing;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>«Закончено ли соревнование» — перенос правил из server/db/day-json.mjs (план П5, §4.4); отметки судей — Р120.</summary>
public class CompetitionProgressTests
{
    private static ProgressRow Swam(string ev, int heat, bool relay = false) => new(ev, relay, heat, "1:02.34", false);
    /// <summary>Строка без времени и без отметки — результат не внесён.</summary>
    private static ProgressRow Blank(string ev, int heat, bool relay = false) => new(ev, relay, heat, "", true);
    /// <summary>Вместо времени напечатано <paramref name="note"/> (DNS, DQ / SW 6.5, NS…).</summary>
    private static ProgressRow Marked(string ev, int heat, string note, bool relay = false) => new(ev, relay, heat, "", true, note);

    [Fact]
    public void All_events_swum_is_finished()
    {
        var s = CompetitionProgress.Evaluate([Swam("50 free", 1), Swam("100 back", 2), Blank("100 back", 2)]);
        Assert.True(s.IsFinished);
        Assert.Empty(s.Pending);
    }

    [Fact]
    public void Event_without_any_time_is_pending_and_named()
    {
        var s = CompetitionProgress.Evaluate([Swam("50 free", 1), Blank("200 free", 1), Blank("200 free", 1)]);
        Assert.False(s.IsFinished);
        Assert.Contains("200 free", Assert.Single(s.Pending));
    }

    [Fact]
    public void Relay_with_only_first_heat_swum_is_not_finished()
    {
        var s = CompetitionProgress.Evaluate([Swam("4x50 free", 1, true), Blank("4x50 free", 2, true), Blank("4x50 free", 2, true)]);
        Assert.False(s.IsFinished);
        Assert.Contains("эстафеты", Assert.Single(s.Pending));
    }

    [Fact]
    public void Partly_DNS_but_last_heat_has_time_is_swum()
    {
        var s = CompetitionProgress.Evaluate([Blank("50 free", 1), Blank("50 free", 2), Swam("50 free", 2)]);
        Assert.True(s.IsFinished);
    }

    [Fact]
    public void Empty_is_not_finished() =>
        Assert.False(CompetitionProgress.Evaluate([]).IsFinished);

    [Fact]
    public void Zero_time_text_counts_as_no_time() =>
        Assert.False(new ProgressRow("e", false, 1, "00:00.00", false).HasTime);

    [Fact]
    public void Json_projection_matches_rows()
    {
        const string json = """
            [{"event":"A","heat":1,"time":"0:30.10","time_fail":false,"is_relay":false},
             {"event":"B","heat":1,"time":"","time_fail":true,"is_relay":null}]
            """;
        var s = CompetitionProgress.EvaluateJson(json);
        Assert.False(s.IsFinished);
        Assert.Contains("B", Assert.Single(s.Pending));
    }

    // ── Р120: отметки судей ────────────────────────────────────────────────────

    [Theory]
    [InlineData("DNS", true)]
    [InlineData(" dns ", true)]
    [InlineData("DQ", true)]
    [InlineData("DQ / SW 6.5", true)]
    [InlineData("DSQ", true)]
    [InlineData("DNF", true)]
    [InlineData("DNF / SW 10.2", true)]
    [InlineData("NS", false)]        // заглушка loglig «результат не внесён» — не доказывает заплыв
    [InlineData("NS / SW", false)]
    [InlineData("2/.00:28", false)]  // мусор колонки времени
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Official_mark_is_DNS_DQ_DNF_but_not_NS(string? note, bool expected) =>
        Assert.Equal(expected, StartStatus.IsOfficialMark(timeFail: true, note));

    [Fact]
    public void Official_mark_needs_time_fail() =>
        Assert.False(StartStatus.IsOfficialMark(timeFail: false, "DNS"));

    [Fact]
    public void Section_of_only_DNS_among_timed_sections_is_finished()
    {
        // 16926: «50 breaststroke - בנות 12» — две пловчихи, обе «DNS»; соседние секции со временами.
        var s = CompetitionProgress.Evaluate([
            Swam("50 breaststroke - בנים 12", 3), Swam("50 breaststroke - בנים 12", 4),
            Marked("50 breaststroke - בנות 12", 3, "DNS"), Marked("50 breaststroke - בנות 12", 3, "DNS"),
        ]);
        Assert.True(s.IsFinished);
        Assert.Empty(s.PendingLines());
    }

    [Fact]
    public void Section_of_only_DQ_and_DNF_is_finished() =>
        Assert.True(CompetitionProgress.Evaluate([Marked("100 fly", 1, "DQ / SW 8.2"), Marked("100 fly", 1, "DNF")]).IsFinished);

    [Fact]
    public void Last_heat_of_only_marks_falls_back_to_previous_heat()
    {
        // 16924: «100 breaststroke - בנים 11» — в последнем заплыве (10) один «DNS», в 9-м время.
        var s = CompetitionProgress.Evaluate([Swam("100 br", 9), Marked("100 br", 10, "DNS")]);
        Assert.True(s.IsFinished);
    }

    [Fact]
    public void Last_heat_of_only_marks_with_unswum_previous_heat_is_pending()
    {
        // Отметки в последнем заплыве не делают проплытым предыдущий, где результатов нет.
        var s = CompetitionProgress.Evaluate([Blank("100 br", 9), Marked("100 br", 10, "DNS")]);
        Assert.False(s.IsFinished);
    }

    [Fact]
    public void Section_of_only_NS_is_still_pending()
    {
        // Неполный PDF loglig печатает непроплытые события строками «NS» (comp 1768, 10.10.2026).
        var s = CompetitionProgress.Evaluate([Swam("50 free", 1), Marked("200 free", 1, "NS"), Marked("200 free", 1, "NS")]);
        Assert.False(s.IsFinished);
        Assert.Contains("200 free: ещё не плыли (0 из 2 со временем)", s.Pending);
    }

    [Fact]
    public void Mixed_marks_and_blanks_without_time_is_pending()
    {
        // Смешанный случай: DNS могли проставить до заплыва, а пустые строки — «результат не внесён».
        var s = CompetitionProgress.Evaluate([
            Swam("50 free", 1),
            Marked("200 free", 1, "DNS"), Blank("200 free", 1), Marked("200 free", 1, "NS"),
        ]);
        Assert.False(s.IsFinished);
        Assert.Contains("200 free", Assert.Single(s.Pending));
    }

    [Fact]
    public void Marks_never_make_a_finished_event_pending()
    {
        var s = CompetitionProgress.Evaluate([Swam("50 free", 2), Marked("50 free", 2, "DNS"), Marked("50 free", 3, "DQ")]);
        Assert.True(s.IsFinished);
    }

    [Fact]
    public void Relay_last_heat_all_DNS_is_finished() =>
        Assert.True(CompetitionProgress.Evaluate([
            Swam("4x50 free", 1, true), Marked("4x50 free", 2, "DNS", true), Marked("4x50 free", 2, "DQ", true),
        ]).IsFinished);

    [Fact]
    public void Json_projection_reads_fail_note_and_string_time_fail()
    {
        const string json = """
            [{"event":"A","heat":1,"time":"0:30.10","time_fail":false},
             {"event":"B","heat":1,"time":"","time_fail":"true","time_fail_note":"DNS"},
             {"event":"C","heat":1,"time":"","time_fail":true,"time_fail_note":"NS"}]
            """;
        var s = CompetitionProgress.EvaluateJson(json);
        Assert.Contains("C", Assert.Single(s.Pending));
    }

    [Theory]
    [InlineData("loglig-pdf-16926-progress.json")] // «50 breaststroke/freestyle - בנות 12»: только DNS
    [InlineData("loglig-pdf-16924-progress.json")] // «100 breaststroke - בנים 11»: последний заплыв — один DNS
    public void Real_protocols_with_DNS_only_sections_are_finished(string file)
    {
        // Разбор свежих PDF loglig 23.11.2026 (--repull --dry-run), только поля «проплыто ли».
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Progress", file));
        var s = CompetitionProgress.EvaluateJson(json);
        Assert.True(s.IsFinished, string.Join("; ", s.PendingLines()));
        Assert.False(NoStartProtocol.AllNoStart(json));
    }

    // ── Р116 не задет: протокол из одних неявок — «∅ нет протокола» ─────────────

    [Fact]
    public void All_DNS_protocol_is_still_empty_for_results_auto()
    {
        const string json = """
            [{"event":"A","heat":1,"time":"","time_fail":true,"time_fail_note":"DNS"},
             {"event":"B","heat":1,"time":"","time_fail":true,"time_fail_note":"DNS"}]
            """;
        Assert.True(NoStartProtocol.AllNoStart(json));

        // Превью ставит «пусто» до оценки прогресса; results-auto идёт по классификатору первым.
        var (verdict, reasons) = BulkPullClassifier.Classify(
            DiscoveryPreviewResult.Failed(NoStartProtocol.Message(2)), regulation: null, isChampionshipByName: false);
        var d = ResultsAutoRules.Decide(new ResultsAutoGateInput(
            verdict, reasons.ToList(), false, 1, CompetitionProgress.EvaluateJson(json), null,
            new VenuePoolLookup(VenuePoolLookupStatus.NoVenue, null, null, null, null)));
        Assert.Equal(ResultsAutoVerdicts.Empty, d.Verdict);
    }

    [Fact]
    public async Task RealPdf_14676_all_NS_stays_empty_and_unfinished()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing",
            "LeagueAdults2Summer2025_14676_all_ns_HE.pdf");
        await using var fs = File.OpenRead(path);
        var parsed = await new PdfResultSourceProvider([new IsrOrgParser()]).ParseAsync(new ResultSourceRequest(
            fs, "isrorg-14676-loglig-12637-he.pdf", "IsrOrg", Language: "he"));

        Assert.True(NoStartProtocol.AllNoStart(parsed.ResultsJson));
        Assert.False(CompetitionProgress.EvaluateJson(parsed.ResultsJson).IsFinished);
    }
}
