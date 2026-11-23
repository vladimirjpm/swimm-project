using System;
using System.Collections.Generic;
using System.Linq;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Пять рабочих состояний строки /Admin/Competitions (хэндофф «вариант E», раздел 2; правило
/// Влада 23.11.2026): впереди → идёт (с первого дня) → ждёт затягивания / ждём протокол; закрыто.
/// </summary>
public class CompetitionRowStateRulesTests
{
    private static readonly DateOnly Fri = new(2026, 11, 20);

    private static CompetitionRowStatus OnSite(DateOnly today, DateOnly first, DateOnly? last = null,
        string? verdict = null, bool empty = false, bool notPublished = false) =>
        CompetitionRowStateRules.Evaluate(new CompetitionRowStateInput(
            CompetitionStage.OnSite, first, last ?? first, verdict, empty, notPublished), today);

    // ── Границы дат однодневного старта ────────────────────────────────────────

    [Fact]
    public void OneDay_DayBefore_IsAhead() =>
        Assert.Equal(CompetitionRowState.Ahead, OnSite(Fri.AddDays(-1), Fri).State);

    [Fact]
    public void OneDay_OnTheDay_IsRunning_Day1Of1()
    {
        var s = OnSite(Fri, Fri);
        Assert.Equal(CompetitionRowState.Running, s.State);
        Assert.Equal(1, s.RunningDay);
        Assert.Equal(1, s.DayCount);
        Assert.Equal(CompetitionStateBucket.Ahead, s.Bucket); // «идёт» — в «впереди» полос
    }

    [Fact]
    public void OneDay_NextDay_IsReadyToPull_WhenNoVerdict()
    {
        var s = OnSite(Fri.AddDays(1), Fri);
        Assert.Equal(CompetitionRowState.ReadyToPull, s.State);
        Assert.Equal(CompetitionStateBucket.Debt, s.Bucket);
        Assert.Null(s.Reason);
        Assert.False(s.NeedsYou);
    }

    [Fact]
    public void StartListBeforeTheStart_DoesNotMakeItRunning()
    {
        // Стартовый протокол на вход правила вообще не подаётся: до первого дня — «впереди»,
        // даже если результаты-автопроверка уже что-то сказала (вердикт до старта не бывает, но
        // и он состояние не двигает раньше даты).
        Assert.Equal(CompetitionRowState.Ahead,
            OnSite(Fri.AddDays(-3), Fri, verdict: ResultsAutoVerdicts.NotFinished).State);
    }

    // ── Многодневка ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(-1, CompetitionRowState.Ahead, null)]
    [InlineData(0, CompetitionRowState.Running, 1)]
    [InlineData(1, CompetitionRowState.Running, 2)]
    [InlineData(2, CompetitionRowState.Running, 3)]  // последний день включительно
    [InlineData(3, CompetitionRowState.ReadyToPull, null)]
    public void MultiDay_Boundaries(int offset, CompetitionRowState expected, int? day)
    {
        var s = OnSite(Fri.AddDays(offset), Fri, Fri.AddDays(2));
        Assert.Equal(expected, s.State);
        Assert.Equal(day, s.RunningDay);
        Assert.Equal(3, s.DayCount);
        Assert.Equal(Fri.AddDays(2), s.LastDay);
    }

    [Fact]
    public void RunningIsByDates_NotByNotFinishedVerdict()
    {
        // Вердикт «ещё идёт» после последнего дня — это «ждём протокол (неполный)», а не «идёт».
        var s = OnSite(Fri.AddDays(1), Fri, verdict: ResultsAutoVerdicts.NotFinished);
        Assert.Equal(CompetitionRowState.WaitingProtocol, s.State);
        Assert.Equal(WaitingProtocolReason.Incomplete, s.Reason);
    }

    // ── Вердикты results-auto после последнего дня ─────────────────────────────

    [Theory]
    [InlineData(ResultsAutoVerdicts.WouldTake, CompetitionRowState.ReadyToPull, null, false, false)]
    [InlineData(ResultsAutoVerdicts.Blocked, CompetitionRowState.ReadyToPull, null, true, false)]
    [InlineData(ResultsAutoVerdicts.NotFinished, CompetitionRowState.WaitingProtocol, WaitingProtocolReason.Incomplete, false, false)]
    [InlineData(ResultsAutoVerdicts.Empty, CompetitionRowState.WaitingProtocol, WaitingProtocolReason.NoProtocol, false, false)]
    [InlineData(ResultsAutoVerdicts.Failed, CompetitionRowState.WaitingProtocol, WaitingProtocolReason.ParseFailed, false, true)]
    [InlineData(null, CompetitionRowState.ReadyToPull, null, false, false)]
    [InlineData("незнакомый", CompetitionRowState.ReadyToPull, null, false, false)]
    public void Verdicts(string? verdict, CompetitionRowState state, WaitingProtocolReason? reason, bool needsYou, bool parseFailed)
    {
        var s = OnSite(Fri.AddDays(2), Fri, verdict: verdict);
        Assert.Equal(state, s.State);
        Assert.Equal(reason, s.Reason);
        Assert.Equal(needsYou, s.NeedsYou);
        Assert.Equal(parseFailed, s.ParseFailed);
        Assert.Equal(CompetitionStateBucket.Debt, s.Bucket);
    }

    [Fact]
    public void NoVerdict_ResultsNotPublished_IsWaitingProtocol()
    {
        var s = OnSite(Fri.AddDays(1), Fri, notPublished: true);
        Assert.Equal(CompetitionRowState.WaitingProtocol, s.State);
        Assert.Equal(WaitingProtocolReason.NoProtocol, s.Reason);
    }

    [Fact]
    public void Verdict_WinsOverStaleFetchError()
    {
        // Задача разобрала протокол после ошибки забора — её вердикт свежее.
        Assert.Equal(CompetitionRowState.ReadyToPull,
            OnSite(Fri.AddDays(1), Fri, verdict: ResultsAutoVerdicts.WouldTake, notPublished: true).State);
    }

    // ── Закрыто ────────────────────────────────────────────────────────────────

    [Fact]
    public void NoProtocolMark_IsClosed_Grey_EvenBeforeTheStart()
    {
        foreach (var today in new[] { Fri.AddDays(-1), Fri, Fri.AddDays(1) })
        {
            var s = OnSite(today, Fri, empty: true);
            Assert.Equal(CompetitionRowState.Closed, s.State);
            Assert.True(s.NothingToPull);
        }
    }

    [Fact]
    public void MarkedImportedByHand_IsClosed_Grey_AfterTheDate()
    {
        var s = CompetitionRowStateRules.Evaluate(new CompetitionRowStateInput(
            CompetitionStage.OnSite, Fri, Fri, MarkedImported: true), Fri.AddDays(3));
        Assert.Equal(CompetitionRowState.Closed, s.State);
        Assert.True(s.NothingToPull);
        Assert.Equal(1, CompetitionStateCounts.Zero.Add(s).ClosedNothingToPull);
    }

    [Fact]
    public void Ignored_IsClosed_Grey()
    {
        var s = CompetitionRowStateRules.Evaluate(
            new CompetitionRowStateInput(CompetitionStage.Ignored, Fri.AddDays(5), Fri.AddDays(5)), Fri);
        Assert.Equal(CompetitionRowState.Closed, s.State);
        Assert.True(s.NothingToPull);
        Assert.Equal(CompetitionStateBucket.Closed, s.Bucket);
    }

    [Theory]
    [InlineData(CompetitionStage.Imported)]
    [InlineData(CompetitionStage.DbOnly)]
    public void InDb_IsClosed_Green_RegardlessOfVerdictAndDates(CompetitionStage stage)
    {
        var s = CompetitionRowStateRules.Evaluate(new CompetitionRowStateInput(
            stage, Fri.AddDays(1), Fri.AddDays(1), ResultsAutoVerdicts.Blocked), Fri);
        Assert.Equal(CompetitionRowState.Closed, s.State);
        Assert.False(s.NothingToPull);
    }

    [Fact]
    public void ImportedWithPartialResults_WaitsForFullProtocol_AfterLastDay()
    {
        var input = new CompetitionRowStateInput(CompetitionStage.Imported, Fri, Fri.AddDays(1), PartialResults: true);
        var after = CompetitionRowStateRules.Evaluate(input, Fri.AddDays(2));
        Assert.Equal(CompetitionRowState.WaitingProtocol, after.State);
        Assert.Equal(WaitingProtocolReason.Incomplete, after.Reason);
        // Затянули первый день, пока старт идёт (pull-to-prod по дням) — это «идёт», не долг.
        Assert.Equal(CompetitionRowState.Running, CompetitionRowStateRules.Evaluate(input, Fri.AddDays(1)).State);
    }

    // ── Решение Влада 23.11.2026: затянутая по дням многодневка без «неполные» ──────────

    [Theory]
    [InlineData(CompetitionStage.Imported)]
    [InlineData(CompetitionStage.DbOnly)]
    public void PulledByDays_WithoutPartialMark_IsRunning_UntilLastDay_ThenClosed(CompetitionStage stage)
    {
        // Старт пт–вс, первый день затянули в субботу; по сайту последний день — воскресенье.
        var input = new CompetitionRowStateInput(stage, Fri, Fri.AddDays(2));
        var sat = CompetitionRowStateRules.Evaluate(input, Fri.AddDays(1));
        Assert.Equal(CompetitionRowState.Running, sat.State);
        Assert.Equal(2, sat.RunningDay);
        Assert.Equal(3, sat.DayCount);
        Assert.Equal(CompetitionStateBucket.Ahead, sat.Bucket); // в полосах — «впереди», как любой «идёт»
        Assert.Equal(CompetitionRowState.Running, CompetitionRowStateRules.Evaluate(input, Fri.AddDays(2)).State);

        var mon = CompetitionRowStateRules.Evaluate(input, Fri.AddDays(3));
        Assert.Equal(CompetitionRowState.Closed, mon.State);
        Assert.False(mon.NothingToPull);
    }

    [Fact]
    public void ImportedOneDay_OnTheDay_StaysClosed()
    {
        // Правило «идёт» у затянутого — только для многодневки: однодневный, затянутый в день
        // старта, закрыт сразу.
        var s = CompetitionRowStateRules.Evaluate(new CompetitionRowStateInput(CompetitionStage.Imported, Fri, Fri), Fri);
        Assert.Equal(CompetitionRowState.Closed, s.State);
    }

    // ── Решение Влада 23.11.2026: недотянутая многодневка ─────────────────────────────

    private static CompetitionRowStatus Pulled(DateOnly today, int dbDays, int? siteDays, bool accepted = false,
        CompetitionStage stage = CompetitionStage.Imported) =>
        CompetitionRowStateRules.Evaluate(new CompetitionRowStateInput(
            stage, Fri, Fri.AddDays((siteDays ?? 1) - 1),
            DbDayCount: dbDays, SiteDayCount: siteDays, MissingDaysAccepted: accepted), today);

    [Fact]
    public void PulledTwoOfThree_AfterLastDay_IsWaitingProtocol_Incomplete()
    {
        var s = Pulled(Fri.AddDays(3), dbDays: 2, siteDays: 3);
        Assert.Equal(CompetitionRowState.WaitingProtocol, s.State);
        Assert.Equal(WaitingProtocolReason.Incomplete, s.Reason);
        Assert.Equal((2, 3), (s.PulledDays!.Value, s.SiteDays!.Value));
        Assert.Equal(CompetitionStateBucket.Debt, s.Bucket);
        // Пока последний день по сайту не прошёл — «идёт», как у любой затянутой по дням.
        Assert.Equal(CompetitionRowState.Running, Pulled(Fri.AddDays(2), dbDays: 2, siteDays: 3).State);
    }

    [Fact]
    public void PulledTwoOfThree_AcceptedAsIs_IsClosed()
    {
        var s = Pulled(Fri.AddDays(3), dbDays: 2, siteDays: 3, accepted: true);
        Assert.Equal(CompetitionRowState.Closed, s.State);
        Assert.Null(s.PulledDays);
    }

    [Theory]
    [InlineData(1, 1)] // однодневное
    [InlineData(3, 3)] // затянуто полностью
    [InlineData(4, 3)] // в базе дней больше, чем по сайту — не долг
    public void FullyPulled_IsClosed(int dbDays, int siteDays) =>
        Assert.Equal(CompetitionRowState.Closed, Pulled(Fri.AddDays(5), dbDays, siteDays).State);

    [Fact]
    public void DbOnly_WithoutSite_IsClosed() =>
        Assert.Equal(CompetitionRowState.Closed, Pulled(Fri.AddDays(5), dbDays: 1, siteDays: null, stage: CompetitionStage.DbOnly).State);

    // ── Решение Влада 23.11.2026: «пропал N дн. — скрыть?» ─────────────────────────

    private static CompetitionRowStatus Missing(DateOnly today, DateOnly start, DateOnly missingSince,
        CompetitionStage stage = CompetitionStage.OnSite) =>
        CompetitionRowStateRules.Evaluate(new CompetitionRowStateInput(
            stage, start, start, MissingSinceDay: missingSince), today);

    [Fact]
    public void Missing_AfterStart_IsStillDebt_AndSuggestsHide_FromThreshold()
    {
        var gone = Fri.AddDays(-10);
        var before = Missing(gone.AddDays(CompetitionRowStateRules.MissingSuggestHideDays - 1), Fri.AddDays(-12), gone);
        Assert.Equal(CompetitionStateBucket.Debt, before.Bucket);
        Assert.Equal(CompetitionRowStateRules.MissingSuggestHideDays - 1, before.MissingDays);
        Assert.False(before.SuggestHide);

        var at = Missing(gone.AddDays(CompetitionRowStateRules.MissingSuggestHideDays), Fri.AddDays(-12), gone);
        Assert.Equal(CompetitionRowState.ReadyToPull, at.State); // сама не закрывается
        Assert.True(at.SuggestHide);
    }

    [Fact]
    public void Missing_CountsFromMissingSince_NotFromStartDate()
    {
        // Старт прошёл месяц назад, а пропал вчера — предлагать скрыть рано.
        var s = Missing(Fri, Fri.AddDays(-30), Fri.AddDays(-1));
        Assert.Equal(1, s.MissingDays);
        Assert.False(s.SuggestHide);
    }

    [Fact]
    public void Missing_BeforeStart_NeverSuggestsHide()
    {
        var s = Missing(Fri, Fri.AddDays(5), Fri.AddDays(-20));
        Assert.Equal(CompetitionRowState.Ahead, s.State);
        Assert.Equal(20, s.MissingDays);
        Assert.False(s.SuggestHide);
    }

    [Fact]
    public void Missing_Closed_NeverSuggestsHide() =>
        Assert.False(Missing(Fri, Fri.AddDays(-30), Fri.AddDays(-20), CompetitionStage.Ignored).SuggestHide);

    [Fact]
    public void UnknownDate_IsReadyToPull_SoItShowsUpInDebt() =>
        Assert.Equal(CompetitionRowState.ReadyToPull, CompetitionRowStateRules.Evaluate(
            new CompetitionRowStateInput(CompetitionStage.OnSite, null, null), Fri).State);

    // ── Часы Израиля ───────────────────────────────────────────────────────────

    [Fact]
    public void IsraelToday_ShiftsTheDate_LateUtcEvening()
    {
        // 20.11 22:30 UTC — в Израиле (UTC+2 зимой) уже 21.11.
        Assert.Equal(new DateOnly(2026, 11, 21),
            CompetitionRowStateRules.IsraelToday(new DateTimeOffset(2026, 11, 20, 22, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 11, 20),
            CompetitionRowStateRules.IsraelToday(new DateTimeOffset(2026, 11, 20, 21, 30, 0, TimeSpan.Zero)));
    }

    // ── Счётчики и группировка ────────────────────────────────────────────────

    [Fact]
    public void Counts_BucketsAddUpToTotal()
    {
        var c = CompetitionStateCounts.Zero
            .Add(CompetitionRowState.Ahead).Add(CompetitionRowState.Running)
            .Add(CompetitionRowState.ReadyToPull).Add(CompetitionRowState.ReadyToPull)
            .Add(CompetitionRowState.WaitingProtocol).Add(CompetitionRowState.Closed);
        Assert.Equal(6, c.Total);
        Assert.Equal(3, c.Debt);
        Assert.Equal(2, c.AheadTotal);
        Assert.Equal(c.Total, c.Closed + c.Debt + c.AheadTotal);
        Assert.Equal(c.Debt, c.Of(CompetitionStateBucket.Debt));
        Assert.Equal(c.Total * 2, (c + c).Total);
    }

    [Fact]
    public void ReadyToPull_GroupedByLastDay_FreshFirst_OthersSkipped()
    {
        var today = Fri.AddDays(5);
        var rows = new List<(string Name, CompetitionRowStatus Status)>
        {
            ("a", OnSite(today, Fri)),                      // 20.11
            ("b", OnSite(today, Fri.AddDays(-2), Fri)),     // многодневка до 20.11 → тот же блок
            ("c", OnSite(today, Fri.AddDays(1))),           // 21.11
            ("d", OnSite(today, Fri, verdict: ResultsAutoVerdicts.Empty)), // ждём протокол — не в блоках
            ("e", OnSite(today, today.AddDays(1))),         // впереди — не в блоках
        };

        var days = CompetitionRowStateRules.GroupReadyToPullByDay(rows, r => r.Status);

        Assert.Equal(new[] { Fri.AddDays(1), Fri }, days.Select(d => d.Day));
        Assert.Equal(new[] { "c" }, days[0].Rows.Select(r => r.Name));
        Assert.Equal(new[] { "a", "b" }, days[1].Rows.Select(r => r.Name));
    }

    [Theory]
    [InlineData("waiting", CompetitionStateBucket.Debt)]
    [InlineData("AHEAD", CompetitionStateBucket.Ahead)]
    [InlineData(" closed ", CompetitionStateBucket.Closed)]
    [InlineData("", null)]
    [InlineData("OnSite", null)]
    public void StateFilter_Parse(string value, CompetitionStateBucket? expected) =>
        Assert.Equal(expected, CompetitionStateFilters.Parse(value));

    [Fact]
    public void SeasonMonthOrder_SeptemberToAugust() =>
        Assert.Equal(new[] { 9, 10, 11, 12, 1, 2, 3, 4, 5, 6, 7, 8 }, CompetitionRowStateRules.SeasonMonthOrder);
}
