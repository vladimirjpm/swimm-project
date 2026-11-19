using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// П3 (docs/plans/prod-auto-pull-plan.md §4.3, Р-4): кого обход стартовых протоколов смотрит в
/// тик. Время фиксированное — ноябрь, Израиль UTC+2.
/// </summary>
public class StartListSweepRulesTests
{
    // 20.11.2026 10:00 UTC = 12:00 по Израилю.
    private static readonly DateTime Now = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Today = new(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);

    private static StartListPullFact Pull(TimeSpan ago, string status = StartListPullStatus.Ok, int entries = 120) =>
        new(Now - ago, status, 30, entries, 0, 0, 0);

    private static StartListSweepCandidate Comp(int daysFromToday, bool logligId = true,
        DateTime? firstHeat = null, params StartListPullFact[] pulls) =>
        new(1, Today.AddDays(daysFromToday), logligId, pulls, firstHeat);

    private static StartListSweepVerdict Decide(StartListSweepCandidate c, DateTime? now = null) =>
        StartListSweepRules.Decide(c, now ?? Now, daysAhead: 14);

    private static readonly StartListPullFact EmptyPull = new(default, StartListPullStatus.Empty, 30, 0, 0, 0, 0);

    // ── Ни разу не тянули — пора всем в окне ─────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(14)]
    public void Never_pulled_is_due_anywhere_in_window(int days) =>
        Assert.Equal(StartListSweepVerdict.Due, Decide(Comp(days)));

    [Theory]
    [InlineData(-1)]
    [InlineData(15)]
    public void Past_or_beyond_window_is_out(int days) =>
        Assert.Equal(StartListSweepVerdict.OutOfWindow, Decide(Comp(days)));

    // ── 3–14 дней: раз в сутки ───────────────────────────────────────────

    [Fact]
    public void Far_start_waits_a_day()
    {
        Assert.Equal(StartListSweepVerdict.TooEarly, Decide(Comp(3, pulls: Pull(TimeSpan.FromHours(6)))));
        Assert.Equal(StartListSweepVerdict.TooEarly, Decide(Comp(3, pulls: Pull(TimeSpan.FromHours(22)))));
        // Допуск час: забор шёл в середине вчерашнего тика — сегодняшний тик его не пропускает.
        Assert.Equal(StartListSweepVerdict.Due, Decide(Comp(3, pulls: Pull(TimeSpan.FromHours(23)))));
    }

    [Fact]
    public void Far_start_without_loglig_id_waits_for_details()
    {
        Assert.Equal(StartListSweepVerdict.WaitingLogligId, Decide(Comp(3, logligId: false)));
        // А ближайший без loglig-id — тянем: забор сам дочитает comp.asp.
        Assert.Equal(StartListSweepVerdict.Due, Decide(Comp(2, logligId: false)));
    }

    [Fact]
    public void Failed_pull_of_far_start_is_retried_after_three_hours_not_a_day()
    {
        Assert.Equal(StartListSweepVerdict.TooEarly,
            Decide(Comp(5, pulls: Pull(TimeSpan.FromHours(1), StartListPullStatus.Error, 0))));
        Assert.Equal(StartListSweepVerdict.Due,
            Decide(Comp(5, pulls: Pull(TimeSpan.FromHours(2), StartListPullStatus.Error, 0))));
    }

    // ── Граница 2 дня: посева нет — 3 ч, посев есть — 6 ч ───────────────

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Near_start_without_seeding_every_three_hours(int days)
    {
        var empty = Pull(TimeSpan.FromHours(1), StartListPullStatus.Empty, 0);
        Assert.Equal(StartListSweepVerdict.TooEarly, Decide(Comp(days, pulls: empty)));
        var older = Pull(TimeSpan.FromHours(2.5), StartListPullStatus.Empty, 0);
        Assert.Equal(StartListSweepVerdict.Due, Decide(Comp(days, pulls: older)));
        Assert.Equal(StartListSweepRules.NearNoSeedInterval, StartListSweepRules.Interval(Comp(days, pulls: older), Now, 14));
    }

    [Fact]
    public void Day_three_is_still_daily_even_without_seeding()
    {
        var empty = Pull(TimeSpan.FromHours(4), StartListPullStatus.Empty, 0);
        Assert.Equal(StartListSweepVerdict.TooEarly, Decide(Comp(3, pulls: empty)));
        Assert.Equal(StartListSweepRules.FarInterval, StartListSweepRules.Interval(Comp(3, pulls: empty), Now, 14));
    }

    [Fact]
    public void Near_start_with_seeding_every_six_hours()
    {
        // Посев забран раньше, последний забор — свежий «ok».
        var seeded = Comp(2, pulls: [Pull(TimeSpan.FromHours(10)), Pull(TimeSpan.FromHours(4))]);
        Assert.Equal(StartListSweepRules.NearSeededInterval, StartListSweepRules.Interval(seeded, Now, 14));
        Assert.Equal(StartListSweepVerdict.TooEarly, Decide(seeded));

        var later = Comp(2, pulls: [Pull(TimeSpan.FromHours(10)), Pull(TimeSpan.FromHours(5))]);
        Assert.Equal(StartListSweepVerdict.Due, Decide(later));
    }

    [Fact]
    public void Seeded_means_ok_or_partial_with_entries()
    {
        Assert.False(StartListSweepRules.IsSeeded([]));
        Assert.False(StartListSweepRules.IsSeeded([EmptyPull]));
        Assert.False(StartListSweepRules.IsSeeded([EmptyPull with { Status = StartListPullStatus.Ok }])); // ok без заявок
        Assert.False(StartListSweepRules.IsSeeded([EmptyPull with { Status = StartListPullStatus.Error, Entries = 5 }]));
        Assert.True(StartListSweepRules.IsSeeded([EmptyPull with { Status = StartListPullStatus.Partial, Entries = 5 }]));
        // Посев был, потом сбой — всё равно «посев забран»: шаг 6 ч, но сбой повторяется через 3 ч.
        var seededThenError = Comp(1, pulls: [Pull(TimeSpan.FromHours(20)), Pull(TimeSpan.FromHours(2.5), StartListPullStatus.Error, 0)]);
        Assert.Equal(StartListSweepVerdict.Due, Decide(seededThenError));
    }

    // ── День старта: 3 ч до первого заплыва, потом не трогать ───────────

    [Fact]
    public void Start_day_before_first_heat_every_three_hours()
    {
        var firstHeat = Now.AddHours(2);
        Assert.Equal(StartListSweepVerdict.TooEarly, Decide(Comp(0, firstHeat: firstHeat, pulls: Pull(TimeSpan.FromHours(1)))));
        Assert.Equal(StartListSweepVerdict.Due, Decide(Comp(0, firstHeat: firstHeat, pulls: Pull(TimeSpan.FromHours(2.5)))));
        // Время первого заплыва неизвестно — тоже каждые 3 ч.
        Assert.Equal(StartListSweepVerdict.Due, Decide(Comp(0, pulls: Pull(TimeSpan.FromHours(2.5)))));
    }

    [Fact]
    public void Start_day_after_first_heat_is_left_alone()
    {
        var started = Comp(0, firstHeat: Now.AddMinutes(-1), pulls: Pull(TimeSpan.FromHours(8)));
        Assert.Equal(StartListSweepVerdict.Started, Decide(started));
        Assert.Null(StartListSweepRules.Interval(started, Now, 14));
        // Ровно в момент первого заплыва — уже начался.
        Assert.Equal(StartListSweepVerdict.Started, Decide(Comp(0, firstHeat: Now)));
    }

    [Fact]
    public void First_heat_on_another_date_does_not_count()
    {
        // Р86: время заплыва на чужую дату — как «не назначено».
        var wrongDate = Comp(0, firstHeat: Now.AddDays(-5));
        Assert.Equal(StartListSweepVerdict.Due, Decide(wrongDate));
    }

    [Fact]
    public void Start_day_is_israel_date_not_utc_date()
    {
        // 20.11 23:30 UTC = 21.11 01:30 по Израилю: старт 21.11 — уже «сегодня».
        var lateNight = new DateTime(2026, 11, 20, 23, 30, 0, DateTimeKind.Utc);
        Assert.Equal(0, StartListSweepRules.DaysToStart(Today.AddDays(1), lateNight));
        Assert.Equal(-1, StartListSweepRules.DaysToStart(Today, lateNight));
        Assert.Equal(DateTimeKind.Utc, StartListSweepRules.IsraelToday(lateNight).Kind);
    }

    [Fact]
    public void Window_shorter_than_default_is_respected()
    {
        Assert.Equal(StartListSweepVerdict.OutOfWindow, StartListSweepRules.Decide(Comp(8), Now, daysAhead: 7));
        Assert.Equal(StartListSweepVerdict.Due, StartListSweepRules.Decide(Comp(7), Now, daysAhead: 7));
    }

    // ── Суточная догрузка loglig-id ──────────────────────────────────────

    [Fact]
    public void Details_run_once_per_israel_day()
    {
        Assert.True(StartListSweepRules.DetailsDue(null, Now));
        Assert.False(StartListSweepRules.DetailsDue(Now.AddHours(-3), Now));             // тот же день
        Assert.False(StartListSweepRules.DetailsDue(Now.AddHours(-11), Now));            // 01:00 сегодня по Израилю, хотя UTC — вчера
        Assert.True(StartListSweepRules.DetailsDue(Now.AddHours(-13), Now));             // 23:00 вчера по Израилю
        // 21:30 UTC = 23:30 по Израилю, 22:30 UTC = 00:30 следующего дня — новый день.
        Assert.True(StartListSweepRules.DetailsDue(
            new DateTime(2026, 11, 20, 21, 30, 0, DateTimeKind.Utc), new DateTime(2026, 11, 20, 22, 30, 0, DateTimeKind.Utc)));
    }
}
