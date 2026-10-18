using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Бейдж «стартовый протокол затянут» в /Admin/Competitions (решение Влада 18.10.2026):
/// цвет по исходу последнего забора и «пора обновить» за 3 дня до старта.
/// </summary>
public class StartListBadgeRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc);

    private static StartListPullFact Pull(DateTime at, string status = "ok", int entries = 120) =>
        new(at, status, Events: 10, Entries: entries, Added: 0, Removed: 0, Moved: 0);

    [Fact]
    public void NeverPulled_NoBadge() =>
        Assert.Null(StartListBadgeRules.Evaluate([], Now.AddDays(2), Now));

    [Theory]
    [InlineData("ok", 120, StartListBadgeKind.Ok)]
    [InlineData("ok", 0, StartListBadgeKind.Empty)]      // программа есть, людей нет
    [InlineData("partial", 50, StartListBadgeKind.Partial)]
    [InlineData("empty", 0, StartListBadgeKind.Empty)]
    [InlineData("error", 0, StartListBadgeKind.Error)]
    [InlineData("weird", 10, StartListBadgeKind.Error)]  // незнакомое — лучше посмотреть
    public void Kind_FromLastPull(string status, int entries, StartListBadgeKind kind) =>
        Assert.Equal(kind, StartListBadgeRules.KindOf(Pull(Now, status, entries)));

    [Fact]
    public void Last_IsNewest_HistoryCapped()
    {
        var pulls = Enumerable.Range(0, 8).Select(i => Pull(Now.AddHours(-i), entries: 100 + i)).Reverse().ToList();

        var b = StartListBadgeRules.Evaluate(pulls, Now.AddDays(10), Now)!;

        Assert.Equal(Now, b.Last.PulledAt);
        Assert.Equal(StartListBadgeRules.HistorySize, b.History.Count);
        Assert.True(b.History.Zip(b.History.Skip(1)).All(p => p.First.PulledAt > p.Second.PulledAt));
    }

    [Theory]
    [InlineData(2, 30, true)]    // старт через 2 дня, забор 30 ч назад — пора
    [InlineData(2, 10, false)]   // свежий забор
    [InlineData(3, 30, true)]    // край окна
    [InlineData(4, 30, false)]   // ещё рано следить
    [InlineData(0, 30, true)]    // старт сегодня
    [InlineData(-1, 30, false)]  // старт прошёл — обновлять нечего
    public void Stale_ThreeDaysBeforeStart(int daysToStart, int hoursSincePull, bool stale) =>
        Assert.Equal(stale, StartListBadgeRules.IsStale(
            Now.AddHours(-hoursSincePull), Now.Date.AddDays(daysToStart), Now));

    [Fact]
    public void Stale_AppliesToEmptyToo()
    {
        // Посева не было позавчера — за день до старта он, скорее всего, уже есть.
        var b = StartListBadgeRules.Evaluate([Pull(Now.AddHours(-40), "empty", 0)], Now.Date.AddDays(1), Now)!;

        Assert.Equal(StartListBadgeKind.Empty, b.Kind);
        Assert.True(b.Stale);
    }
}
