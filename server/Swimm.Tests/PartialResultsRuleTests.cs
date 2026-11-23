using System.Text.Json;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// «Результаты неполные» (Р114, docs/data-integrity.md): правило отметки, строка «Требует
/// внимания», подсказка многодневки и JSON-контракт полей витрины. Эталон — 16903 (20.11.2026):
/// из 16 событий не выложены две эстафеты.
/// </summary>
public class PartialResultsRuleTests
{
    private static readonly DateTime T1 = new(2026, 11, 21, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = T1.AddDays(1);

    private const string RelayGirls = "4X50m Freestyle Relay - GIRLS 8-15";
    private const string RelayBoys = "4X50m Freestyle Relay - BOYS 8-15";

    /// <summary>14 проплытых событий и две эстафеты строками без времени — как у 16903.</summary>
    private static CompetitionProgressSummary Partial16903(int missingRelays = 2)
    {
        var rows = new List<ProgressRow>();
        for (var i = 1; i <= 14; i++)
            rows.Add(new ProgressRow($"Event {i}", false, 1, "1:02.34", false));
        if (missingRelays >= 1) rows.AddRange(Enumerable.Range(0, 9).Select(_ => new ProgressRow(RelayGirls, true, 1, null, true)));
        else rows.Add(new ProgressRow(RelayGirls, true, 1, "2:10.00", false));
        if (missingRelays >= 2) rows.AddRange(Enumerable.Range(0, 7).Select(_ => new ProgressRow(RelayBoys, true, 1, null, true)));
        else rows.Add(new ProgressRow(RelayBoys, true, 1, "2:05.00", false));
        return CompetitionProgress.Evaluate(rows);
    }

    [Fact]
    public void Incomplete_protocol_sets_the_mark_with_results_auto_text()
    {
        var s = PartialResultsRule.Apply(PartialResultsState.Complete, Partial16903(), T1);

        Assert.True(s.IsShown);
        Assert.Equal(T1, s.MarkedAt);
        Assert.Null(s.AcceptedAt);
        Assert.StartsWith("ждут 2 из 16 событий; ", s.Note);
        Assert.Contains($"{RelayGirls}: ещё не плыли (0 из 9 со временем)", s.Note);
        Assert.Equal($"Waiting for 2 of 16 events: {RelayGirls}; {RelayBoys}", s.NoteEn);
    }

    [Fact]
    public void Note_is_the_same_text_results_auto_gives()
    {
        var progress = Partial16903();
        var decision = ResultsAutoRules.Decide(new ResultsAutoGateInput(
            BulkPullVerdict.Clean, [], false, 1, progress, "25m", null!));
        var s = PartialResultsRule.Apply(PartialResultsState.Complete, progress, T1);

        Assert.Equal(ResultsAutoVerdicts.NotFinished, decision.Verdict);
        Assert.Equal("соревнование не закончено: " + s.Note, ResultsAutoRules.JoinReasons(decision.Reasons));
    }

    [Fact]
    public void Complete_protocol_clears_the_mark()
    {
        var partial = PartialResultsRule.Apply(PartialResultsState.Complete, Partial16903(), T1);
        var s = PartialResultsRule.Apply(partial, Partial16903(missingRelays: 0), T2);

        Assert.Equal(PartialResultsState.Complete, s);
        Assert.False(s.IsShown);
    }

    [Fact]
    public void Missing_days_acceptance_survives_reimport_of_a_complete_day()
    {
        // «Затянуто меньше дней, чем по сайту — принято» хранится как AcceptedAt без отметки
        // (решение Влада 23.11.2026): полный переимпорт дня его не снимает.
        var accepted = PartialResultsState.Complete with { AcceptedAt = T1 };
        var s = PartialResultsRule.Apply(accepted, Partial16903(missingRelays: 0), T2);

        Assert.Equal(T1, s.AcceptedAt);
        Assert.Null(s.Note);
        Assert.False(s.IsShown);
    }

    [Fact]
    public void Accepted_survives_reimport_of_the_same_partial_protocol()
    {
        var partial = PartialResultsRule.Apply(PartialResultsState.Complete, Partial16903(), T1);
        var accepted = PartialResultsRule.Accept(partial, T1.AddHours(1));
        var s = PartialResultsRule.Apply(accepted, Partial16903(), T2);

        Assert.Equal(accepted, s);
        Assert.False(s.IsShown);
        Assert.NotNull(s.Note); // отметка помнит, чего не хватает, — просто не показывается
    }

    [Fact]
    public void Accepted_and_then_complete_protocol_clears_everything()
    {
        var accepted = PartialResultsRule.Accept(
            PartialResultsRule.Apply(PartialResultsState.Complete, Partial16903(), T1), T1.AddHours(1));
        var s = PartialResultsRule.Apply(accepted, Partial16903(missingRelays: 0), T2);

        Assert.Equal(PartialResultsState.Complete, s);
    }

    [Fact]
    public void Accepted_but_another_partial_protocol_brings_the_mark_back()
    {
        var accepted = PartialResultsRule.Accept(
            PartialResultsRule.Apply(PartialResultsState.Complete, Partial16903(), T1), T1.AddHours(1));
        var s = PartialResultsRule.Apply(accepted, Partial16903(missingRelays: 1), T2);

        Assert.True(s.IsShown);
        Assert.Null(s.AcceptedAt);
        Assert.Equal(T2, s.MarkedAt);
        Assert.StartsWith("ждут 1 из 16 событий", s.Note);
    }

    [Fact]
    public void Same_partial_protocol_keeps_the_first_mark_time()
    {
        var first = PartialResultsRule.Apply(PartialResultsState.Complete, Partial16903(), T1);
        Assert.Equal(first, PartialResultsRule.Apply(first, Partial16903(), T2));
    }

    [Fact]
    public void Accept_without_mark_changes_nothing_and_unaccept_shows_again()
    {
        Assert.Equal(PartialResultsState.Complete, PartialResultsRule.Accept(PartialResultsState.Complete, T1));
        var accepted = PartialResultsRule.Accept(
            PartialResultsRule.Apply(PartialResultsState.Complete, Partial16903(), T1), T2);
        Assert.True(PartialResultsRule.Unaccept(accepted).IsShown);
    }

    [Fact]
    public void Many_pending_events_are_listed_up_to_three()
    {
        var rows = Enumerable.Range(1, 6).Select(i => new ProgressRow($"E{i}", false, 1, null, true)).ToList();
        rows.Add(new ProgressRow("Done", false, 1, "30.00", false));
        var s = PartialResultsRule.Apply(PartialResultsState.Complete, CompetitionProgress.Evaluate(rows), T1);

        Assert.EndsWith("; и ещё 3", s.Note);
        Assert.Equal("Waiting for 6 of 7 events: E1; E2; E3; and 3 more", s.NoteEn);
    }

    [Fact]
    public void Combine_days_labels_each_day_of_a_multiday_event()
    {
        Assert.Null(PartialResultsRule.CombineDays([(1, null), (2, null)]));
        Assert.Equal("Waiting for 1 of 8 events: X", PartialResultsRule.CombineDays([(1, null), (2, "Waiting for 1 of 8 events: X")]));
        Assert.Equal("Day 1: A · Day 3: B", PartialResultsRule.CombineDays([(3, "B"), (2, null), (1, "A")]));
    }

    // ── «Требует внимания» на /Admin/Jobs ─────────────────────────────────────

    private static PartialResultsDay Day(int id, int? orgCompId, string note = "ждут 2 из 16 событий; …") =>
        new(id, "ליגת חורף", "20/11/2026", null, orgCompId, note, T1);

    [Fact]
    public void Attention_row_for_partial_competition_links_to_edit()
    {
        var rows = PartialResultsWaiting.Merge([], [Day(1888, 16903)]);
        var item = Assert.Single(JobsMonitorBuilder.Attention([], rows, T2));

        Assert.Equal(JobAttentionKinds.Waiting, item.Kind);
        Assert.Equal("/Admin/Competitions/Edit?id=1888#partial-results", item.Href);
        Assert.Contains("«ליגת חורף» #16903 (20.11.2026) затянуто с неполными результатами: ждут 2 из 16 событий", item.Text);
        Assert.Contains("Принять как есть", item.Text);
    }

    [Fact]
    public void Stuck_inbox_row_and_pulled_partial_competition_give_one_row()
    {
        var ended = T1.AddDays(-2);
        var stuck = new ResultsAutoWaitingRow(7, 16903, "ליגת חורף", ended, ended, "ждут 2 из 16 событий", T1, ResultsAutoVerdicts.NotFinished);
        var other = new ResultsAutoWaitingRow(8, 16911, "אחר", ended, ended, "бассейн не задан", T1);

        var rows = PartialResultsWaiting.Merge([stuck, other], [Day(1888, 16903)]);

        Assert.Equal(2, rows.Count); // счётчик «ждёт тебя» = rows.Count
        Assert.Equal([16911, 16903], rows.Select(r => r.OrgCompId));
        Assert.Equal(PartialResultsWaiting.Verdict, rows[1].Verdict);
        Assert.Equal(1888, rows[1].CompetitionId);
    }

    [Fact]
    public void Day_of_event_without_own_compid_keeps_inbox_rows()
    {
        var ended = T1.AddDays(-2);
        var stuck = new ResultsAutoWaitingRow(7, 16903, "x", ended, ended, null, T1, ResultsAutoVerdicts.NotFinished);
        var rows = PartialResultsWaiting.Merge([stuck], [Day(1888, null) with { DayNumber = 2 }]);

        Assert.Equal(2, rows.Count);
        Assert.Equal("ליגת חורף (день 2)", rows[1].Name);
        Assert.DoesNotContain("#", PartialResultsWaiting.Text(rows[1]));
    }

    // ── JSON-контракт витрины ─────────────────────────────────────────────────

    [Fact]
    public void Public_dtos_carry_partial_results_in_snake_case()
    {
        static JsonElement Json(object o) => JsonDocument.Parse(JsonSerializer.Serialize(o)).RootElement;

        Assert.Equal("W", Json(new CompetitionSourceDto { PartialResults = "W" }).GetProperty("partial_results").GetString());
        Assert.Equal("W", Json(new OverviewSummaryDto { PartialResults = "W" }).GetProperty("partial_results").GetString());
        Assert.Equal("W", Json(new OverviewDayDto { PartialResults = "W" }).GetProperty("partial_results").GetString());
        Assert.Equal("W", Json(new CalendarMeetDto { PartialResults = "W" }).GetProperty("partial_results").GetString());
        Assert.Equal(JsonValueKind.Null, Json(new CompetitionSourceDto()).GetProperty("partial_results").ValueKind);
    }
}
