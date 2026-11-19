using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// results-auto (П5, docs/plans/prod-auto-pull-plan.md §4.4): кого берём за тик и гейты — чистые
/// функции <see cref="ResultsAutoRules"/>, без сети и базы.
/// </summary>
public class ResultsAutoRulesTests
{
    // 20.11.2026 — Израиль UTC+2 (зимнее время). 10:00 UTC = 12:00 по Израилю.
    private static readonly DateTime Day = new(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);

    private static ResultsAutoCandidate C(
        int id = 1, string name = "ליגה מס 3", DateTime? start = null, DateTime? end = null,
        string status = "new", string discipline = "swimming", int? logligId = 16000,
        bool inDb = false, bool manualEmpty = false, DateTime? missing = null,
        DateTime? checkedAt = null, string? verdict = null) =>
        new(id, name, status, discipline, logligId, start ?? end ?? Day, end ?? start ?? Day,
            inDb, manualEmpty, missing, checkedAt, verdict);

    private static VenuePoolLookup Found(string pool = "25m") =>
        new(VenuePoolLookupStatus.Found, "בריכת אפק", "אפק", pool,
            new VenuePoolEntry(1, "בריכת אפק", pool, false, null, "Vlad", Day));

    private static VenuePoolLookup NotInDirectory() =>
        new(VenuePoolLookupStatus.NotInDirectory, "בריכת מרום נווה", "מרום נווה", null, null);

    private static CompetitionProgressSummary Finished() =>
        new([new EventProgress("50 חופשי", false, 10, 10, true)]);

    private static ResultsAutoGateInput Clean(
        BulkPullVerdict verdict = BulkPullVerdict.Clean, IReadOnlyList<string>? reasons = null,
        bool champ = false, int days = 1, CompetitionProgressSummary? progress = null,
        string? regulationPool = null, VenuePoolLookup? venue = null) =>
        new(verdict, reasons ?? [], champ, days, progress ?? Finished(), regulationPool, venue ?? Found());

    // ── режим ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "off")]
    [InlineData("", "off")]
    [InlineData("REPORT", "report")]
    [InlineData(" import ", "import")]
    [InlineData("on", "off")]
    public void Mode_parse_unknown_is_off(string? value, string expected) =>
        Assert.Equal(expected, ResultsAutoModes.Parse(value));

    // ── окно ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Window_starts_at_noon_of_end_day_israel_time()
    {
        var c = C(end: Day);
        Assert.False(ResultsAutoRules.InWindow(c, Day.AddHours(9).AddMinutes(59))); // 11:59 по Израилю
        Assert.True(ResultsAutoRules.InWindow(c, Day.AddHours(10)));                // 12:00
    }

    [Fact]
    public void Window_uses_israel_date_not_utc()
    {
        // 22:30 UTC 19.11 — по Израилю уже 00:30 20.11, но до полудня: ещё рано.
        Assert.False(ResultsAutoRules.InWindow(C(end: Day), Day.AddMinutes(-90)));
        // Старт 19.11, сейчас 00:30 20.11 по Израилю — день окончания прошёл.
        Assert.True(ResultsAutoRules.InWindow(C(end: Day.AddDays(-1)), Day.AddMinutes(-90)));
    }

    [Fact]
    public void Window_is_seven_days()
    {
        var now = Day.AddHours(10);
        Assert.True(ResultsAutoRules.InWindow(C(end: Day.AddDays(-7)), now));
        Assert.False(ResultsAutoRules.InWindow(C(end: Day.AddDays(-8)), now));
        Assert.False(ResultsAutoRules.InWindow(C(end: Day.AddDays(1)), now)); // будущий
    }

    [Fact]
    public void Window_excludes_rows_nobody_should_pull()
    {
        var now = Day.AddHours(12);
        Assert.False(ResultsAutoRules.InWindow(C(status: "imported"), now));
        Assert.False(ResultsAutoRules.InWindow(C(status: "ignored"), now));
        Assert.False(ResultsAutoRules.InWindow(C(discipline: "artistic"), now));
        Assert.False(ResultsAutoRules.InWindow(C(logligId: null), now));
        Assert.False(ResultsAutoRules.InWindow(C(inDb: true), now));
        Assert.False(ResultsAutoRules.InWindow(C(manualEmpty: true), now));
        Assert.False(ResultsAutoRules.InWindow(C(missing: Day), now));
        Assert.True(ResultsAutoRules.InWindow(C(), now));
    }

    // ── повторная проверка и выбор ─────────────────────────────────────────────

    [Theory]
    [InlineData(ResultsAutoVerdicts.NotFinished, 2)]
    [InlineData(ResultsAutoVerdicts.Failed, 3)]
    [InlineData(ResultsAutoVerdicts.Empty, 6)]
    [InlineData(ResultsAutoVerdicts.Blocked, 24)]
    [InlineData(ResultsAutoVerdicts.WouldTake, 24)]
    public void Recheck_step_by_verdict(string verdict, int hours)
    {
        var now = Day.AddHours(20);
        Assert.False(ResultsAutoRules.IsDue(C(checkedAt: now.AddHours(-hours).AddMinutes(1), verdict: verdict), now));
        Assert.True(ResultsAutoRules.IsDue(C(checkedAt: now.AddHours(-hours), verdict: verdict), now));
    }

    [Fact]
    public void Pick_never_checked_first_then_freshest_start()
    {
        var now = Day.AddHours(12);
        var pick = ResultsAutoRules.Pick(
        [
            C(1, end: Day.AddDays(-1), checkedAt: now.AddDays(-2), verdict: ResultsAutoVerdicts.Blocked),
            C(2, end: Day.AddDays(-3)),
            C(3, end: Day.AddDays(-1)),
            C(4, end: Day, checkedAt: now.AddMinutes(-30), verdict: ResultsAutoVerdicts.NotFinished), // рано
            C(5, end: Day.AddDays(-20)), // вне окна
        ], now);

        Assert.Equal(3, pick.Next?.Id);
        Assert.Equal(4, pick.InWindow);
        Assert.Equal(3, pick.Due);
    }

    [Fact]
    public void Pick_then_longest_unchecked()
    {
        var now = Day.AddHours(12);
        var pick = ResultsAutoRules.Pick(
        [
            C(1, checkedAt: now.AddDays(-1), verdict: ResultsAutoVerdicts.Blocked),
            C(2, checkedAt: now.AddDays(-3), verdict: ResultsAutoVerdicts.Blocked),
        ], now);
        Assert.Equal(2, pick.Next?.Id);
    }

    [Fact]
    public void Pick_nothing_due()
    {
        var now = Day.AddHours(12);
        var pick = ResultsAutoRules.Pick([C(checkedAt: now.AddHours(-1), verdict: ResultsAutoVerdicts.WouldTake)], now);
        Assert.Null(pick.Next);
        Assert.Equal(1, pick.InWindow);
        Assert.Equal(0, pick.Due);
    }

    // ── гейты без сети ─────────────────────────────────────────────────────────

    [Fact]
    public void PreGate_championship_by_name()
    {
        var d = ResultsAutoRules.PreGate(C(name: "אליפות ישראל חורף 2026"));
        Assert.Equal(ResultsAutoVerdicts.Blocked, d?.Verdict);
        Assert.Contains("чемпионат", d!.ReasonText);
    }

    [Fact]
    public void PreGate_multiday_by_dates()
    {
        var d = ResultsAutoRules.PreGate(C(start: Day.AddDays(-1), end: Day));
        Assert.Equal(ResultsAutoVerdicts.Blocked, d?.Verdict);
        Assert.Contains("многодневка", d!.ReasonText);
        Assert.Contains("19.11–20.11", d.ReasonText);
    }

    [Fact]
    public void PreGate_plain_league_passes() => Assert.Null(ResultsAutoRules.PreGate(C()));

    // ── гейты после разбора ────────────────────────────────────────────────────

    [Fact]
    public void All_gates_pass_would_take_with_venue_pool()
    {
        var d = ResultsAutoRules.Decide(Clean());
        Assert.Equal(ResultsAutoVerdicts.WouldTake, d.Verdict);
        Assert.Empty(d.Reasons);
        Assert.Null(d.ReasonText);
        Assert.Equal("25m", d.PoolType);
        Assert.Equal(PoolTypeSources.Venue, d.PoolTypeSource);
    }

    [Fact]
    public void Regulation_pool_beats_directory()
    {
        var d = ResultsAutoRules.Decide(Clean(regulationPool: "50m", venue: NotInDirectory()));
        Assert.Equal(ResultsAutoVerdicts.WouldTake, d.Verdict);
        Assert.Equal("50m", d.PoolType);
        Assert.Equal(PoolTypeSources.Regulation, d.PoolTypeSource);
    }

    [Fact]
    public void Venue_not_in_directory_blocks_with_describe()
    {
        var d = ResultsAutoRules.Decide(Clean(venue: NotInDirectory()));
        Assert.Equal(ResultsAutoVerdicts.Blocked, d.Verdict);
        Assert.Contains("нет в справочнике бассейнов", d.ReasonText);
        Assert.Null(d.PoolType);
    }

    [Fact]
    public void Ambiguous_venue_blocks()
    {
        var ambiguous = new VenuePoolLookup(VenuePoolLookupStatus.Ambiguous, "מכון וינגייט", "וינגייט", null,
            new VenuePoolEntry(2, "מכון וינגייט", null, true, null, "Vlad", Day));
        var d = ResultsAutoRules.Decide(Clean(venue: ambiguous));
        Assert.Equal(ResultsAutoVerdicts.Blocked, d.Verdict);
        Assert.Contains("двусмысленное", d.ReasonText);
    }

    [Fact]
    public void No_venue_blocks()
    {
        var d = ResultsAutoRules.Decide(Clean(venue: new VenuePoolLookup(VenuePoolLookupStatus.NoVenue, null, null, null, null)));
        Assert.Equal(ResultsAutoVerdicts.Blocked, d.Verdict);
        Assert.Contains("место проведения не указано", d.ReasonText);
    }

    [Fact]
    public void Not_finished_wins_over_other_reasons()
    {
        var progress = new CompetitionProgressSummary(
        [
            new EventProgress("50 חופשי", false, 10, 10, true),
            new EventProgress("4x50 מעורב", true, 6, 0, false),
        ]);
        var d = ResultsAutoRules.Decide(Clean(progress: progress, venue: NotInDirectory(), champ: true));
        Assert.Equal(ResultsAutoVerdicts.NotFinished, d.Verdict);
        Assert.Contains("ждут 1 из 2", d.ReasonText);
        Assert.Contains("4x50 מעורב", d.ReasonText);
    }

    [Fact]
    public void Unreadable_parse_is_failure()
    {
        var input = Clean() with { Progress = null };
        Assert.Equal(ResultsAutoVerdicts.Failed, ResultsAutoRules.Decide(input).Verdict);
    }

    [Fact]
    public void Classifier_failed_and_empty_pass_through()
    {
        Assert.Equal(ResultsAutoVerdicts.Failed,
            ResultsAutoRules.Decide(Clean(BulkPullVerdict.Failed, ["HTTP 503"])).Verdict);
        var empty = ResultsAutoRules.Decide(Clean(BulkPullVerdict.Empty, ["No competitions found in PDF"]));
        Assert.Equal(ResultsAutoVerdicts.Empty, empty.Verdict);
        Assert.Equal("No competitions found in PDF", empty.ReasonText);
    }

    [Fact]
    public void Failure_reason_keeps_first_line_only()
    {
        // Так парсер отдаёт пустой PDF loglig (сухой прогон П5, compID 16859): сообщение + отладочный лог.
        var message = "No competitions found in PDF (language=he).\n\n--- DEBUG LOG ---\n[1] Starting parse, language=he";
        var d = ResultsAutoRules.Decide(Clean(BulkPullVerdict.Empty, [message]));
        Assert.Equal("No competitions found in PDF (language=he).", d.ReasonText);
    }

    [Fact]
    public void Needs_review_reasons_are_the_classifier_reasons()
    {
        var d = ResultsAutoRules.Decide(Clean(BulkPullVerdict.NeedsReview, ["бьёт рекордов: 2 — нужна ручная проверка"]));
        Assert.Equal(ResultsAutoVerdicts.Blocked, d.Verdict);
        Assert.Equal("бьёт рекордов: 2 — нужна ручная проверка", d.ReasonText);
    }

    [Fact]
    public void No_regulation_is_not_green()
    {
        var d = ResultsAutoRules.Decide(Clean(BulkPullVerdict.NoRegulation, ["регламент (תקנון) не найден — медали и зачёт не проставлены"]));
        Assert.Equal(ResultsAutoVerdicts.Blocked, d.Verdict);
        Assert.StartsWith("без регламента", d.ReasonText);
    }

    [Fact]
    public void Championship_by_regulation_blocks()
    {
        var d = ResultsAutoRules.Decide(Clean(champ: true));
        Assert.Equal(ResultsAutoVerdicts.Blocked, d.Verdict);
        Assert.Contains("чемпионат", d.ReasonText);
    }

    [Fact]
    public void Multi_day_file_blocks_once()
    {
        var d = ResultsAutoRules.Decide(Clean(BulkPullVerdict.NeedsReview, ["дней в файле: 2 — нужно решение по событию"], days: 2));
        Assert.Single(d.Reasons);
        var d2 = ResultsAutoRules.Decide(Clean(days: 2));
        Assert.Contains("дней в файле: 2", d2.ReasonText);
    }

    [Fact]
    public void Several_reasons_listed_together()
    {
        var d = ResultsAutoRules.Decide(Clean(champ: true, venue: NotInDirectory()));
        Assert.Equal(2, d.Reasons.Count);
        Assert.Contains("; ", d.ReasonText);
    }

    [Fact]
    public void Reason_text_is_clipped_to_column()
    {
        var text = ResultsAutoRules.JoinReasons([new string('x', 2000)]);
        Assert.Equal(ResultsAutoRules.MaxReasonLength, text!.Length);
        Assert.EndsWith("…", text);
    }

    // ── итог прогона ───────────────────────────────────────────────────────────

    private static ResultsAutoRowOutcome Row(string verdict, IReadOnlyList<string>? reasons = null, string? pool = null) =>
        new(7, 16999, "ליגה מס 3", Day, "בריכת אפק", verdict, reasons ?? [], pool, pool is null ? null : "venue",
            "Clean", 300, 1, 20, 0, 1500);

    [Fact]
    public void Summary_would_take()
    {
        var s = JobRunSummaries.ResultsAuto(new ResultsAutoTickReport("report", 3, 1, Row(ResultsAutoVerdicts.WouldTake, pool: "25m")));
        Assert.Equal("report: в окне 3, пора 1 — «ליגה מס 3» #16999: взяла бы сама, 25m (venue)", s);
    }

    [Fact]
    public void Summary_blocked_and_import_note()
    {
        var s = JobRunSummaries.ResultsAuto(new ResultsAutoTickReport("import", 2, 1,
            Row(ResultsAutoVerdicts.Blocked, ["места «X» нет в справочнике бассейнов"]), ImportNotImplemented: true));
        Assert.StartsWith("import (импорт ещё не реализован — П6", s);
        Assert.Contains("ждёт тебя — места «X» нет в справочнике бассейнов", s);
    }

    [Fact]
    public void Summary_nothing_to_do()
    {
        Assert.Equal("report: в окне 0, пора 0 — прошедших стартов без результатов нет",
            JobRunSummaries.ResultsAuto(new ResultsAutoTickReport("report", 0, 0, null)));
        Assert.Equal("report: в окне 2, пора 0 — все проверены недавно",
            JobRunSummaries.ResultsAuto(new ResultsAutoTickReport("report", 2, 0, null)));
        Assert.Equal("режим off — ничего не делала",
            JobRunSummaries.ResultsAuto(new ResultsAutoTickReport("off", 0, 0, null)));
    }

    [Fact]
    public void Details_json_only_when_a_row_was_checked()
    {
        Assert.Null(JobRunDetails.ResultsAuto(new ResultsAutoTickReport("report", 0, 0, null)));
        var json = JobRunDetails.ResultsAuto(new ResultsAutoTickReport("report", 1, 1, Row(ResultsAutoVerdicts.WouldTake, pool: "25m")));
        Assert.Contains("\"verdict\":\"would-take\"", json);
        Assert.Contains("\"orgCompId\":16999", json);
    }

    [Fact]
    public void Verdict_list_matches_check_constraint_order() =>
        Assert.Equal(["would-take", "blocked", "not-finished", "failed", "empty"], ResultsAutoVerdicts.All);
}
