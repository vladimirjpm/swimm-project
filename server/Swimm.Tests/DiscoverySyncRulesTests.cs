using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Перенос и отмена старта по синхронизации «Входящих» со списком isr.org.il (шаг В4′, решение
/// В4 — docs/plans/competitions-home-plan.md, часть В). Пометок на сайте нет — выводим из списка.
/// </summary>
public class DiscoverySyncRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    // ── Перенос ──────────────────────────────────────────────────────────────

    [Fact]
    public void Future_start_moved_is_postponed() =>
        Assert.Equal(DiscoveryDateChange.Postponed,
            DiscoverySyncRules.ClassifyDateChange(new(2026, 11, 1), new(2026, 11, 22), null, Today));

    [Fact]
    public void Start_today_moved_is_postponed() =>
        // Старт сегодня ещё не прошёл — сдвиг сегодняшнего старта тоже перенос.
        Assert.Equal(DiscoveryDateChange.Postponed,
            DiscoverySyncRules.ClassifyDateChange(Today, new(2026, 10, 26), null, Today));

    [Fact]
    public void Past_start_moved_is_a_typo_fix_not_postponed() =>
        Assert.Equal(DiscoveryDateChange.None,
            DiscoverySyncRules.ClassifyDateChange(new(2026, 10, 9), new(2026, 10, 10), null, Today));

    [Fact]
    public void Same_start_is_no_change() =>
        Assert.Equal(DiscoveryDateChange.None,
            DiscoverySyncRules.ClassifyDateChange(new(2026, 11, 1), new(2026, 11, 1), null, Today));

    [Fact]
    public void Moved_back_to_first_announced_date_restores()
    {
        // Объявили 1 ноября, перенесли на 22-е, вернули на 1-е — перенос отменён.
        Assert.Equal(DiscoveryDateChange.Restored,
            DiscoverySyncRules.ClassifyDateChange(new(2026, 11, 22), new(2026, 11, 1), new DateOnly(2026, 11, 1), Today));
    }

    [Fact]
    public void Second_move_is_still_postponed()
    {
        // Перенесли ещё раз — по-прежнему перенос; первую дату сервис не перезаписывает.
        Assert.Equal(DiscoveryDateChange.Postponed,
            DiscoverySyncRules.ClassifyDateChange(new(2026, 11, 22), new(2026, 12, 6), new DateOnly(2026, 11, 1), Today));
    }

    // ── Отмена ───────────────────────────────────────────────────────────────

    private static DiscoveryKnownRow Row(int id, string start, string status = DiscoveredCompetitionStatus.New) =>
        new(id, DateOnly.Parse(start), status);

    private static readonly DiscoveryKnownRow[] Season =
    [
        Row(1, "2026-10-09"),                      // прошёл — его ищут в «прошедших»
        Row(2, "2026-11-01"),
        Row(3, "2026-12-01"),
        Row(4, "2027-01-15"),
        Row(5, "2027-03-01"),
        Row(6, "2027-05-01"),
        Row(7, "2027-07-30"),
        Row(8, "2027-02-01", DiscoveredCompetitionStatus.Ignored),
        Row(9, "2027-09-15"),                      // следующий сезон — не наш
    ];

    [Fact]
    public void Future_row_missing_from_lists_is_marked()
    {
        var seen = new HashSet<int> { 1, 2, 3, 4, 5, 6 };
        var d = DiscoverySyncRules.FindMissing(Season, seen, upcomingOnSite: 5, Today);
        Assert.False(d.Suppressed);
        Assert.Equal([7], d.Missing);
    }

    [Fact]
    public void Past_ignored_and_next_season_rows_are_never_candidates()
    {
        // Видны только 2–7: прошедший 1, скрытый 8 и сезон-2027 9 пропали, но кандидатами не стали.
        var seen = new HashSet<int> { 2, 3, 4, 5, 6, 7 };
        var d = DiscoverySyncRules.FindMissing(Season, seen, upcomingOnSite: 6, Today);
        Assert.False(d.Suppressed);
        Assert.Empty(d.Missing);
    }

    [Fact]
    public void Empty_upcoming_list_suppresses()
    {
        var seen = new HashSet<int> { 1, 2, 3, 4, 5, 6 };
        var d = DiscoverySyncRules.FindMissing(Season, seen, upcomingOnSite: 0, Today);
        Assert.True(d.Suppressed);
        Assert.Empty(d.Missing);
        Assert.NotNull(d.Reason);
    }

    [Fact]
    public void Mass_disappearance_suppresses()
    {
        // 6 будущих в сезоне, пропало 4 > max(3, 6/2=3) — сбой сайта, а не четыре отмены.
        var seen = new HashSet<int> { 1, 2, 3 };
        var d = DiscoverySyncRules.FindMissing(Season, seen, upcomingOnSite: 2, Today);
        Assert.True(d.Suppressed);
        Assert.Empty(d.Missing);
    }

    [Fact]
    public void Up_to_floor_of_three_is_allowed_even_in_small_season()
    {
        // 6 будущих, пропало ровно 3 = порог — помечаем.
        var seen = new HashSet<int> { 1, 2, 3, 4 };
        var d = DiscoverySyncRules.FindMissing(Season, seen, upcomingOnSite: 3, Today);
        Assert.False(d.Suppressed);
        Assert.Equal([5, 6, 7], d.Missing.Order());
    }

    [Fact]
    public void Nothing_missing_is_quiet_even_with_empty_upcoming_list()
    {
        // Всё увидено — предохранителю нечего гасить, предупреждения в лог нет.
        var d = DiscoverySyncRules.FindMissing(Season, Season.Select(r => r.OrgCompId).ToHashSet(), 0, Today);
        Assert.False(d.Suppressed);
        Assert.Empty(d.Missing);
        Assert.Null(d.Reason);
    }
}
