using Swimm.Domain.Entities;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Юнит-тесты чистого расчёта клубных очков (сезонный зачёт 8.5, <see cref="ClubPointsScoring"/>).
/// Ключевой кейс — тай-брейк правил на одну дату: scope-специфичное (masters/non-masters)
/// важнее общего "all". Это был реальный баг на смоуке 8.5 (masters давал очки по "all").
/// </summary>
public class ClubPointsScoringTests
{
    private static ClubPointsRule Rule(
        string scope, string effectiveFrom, int defaultPoints, int? maxPlace, params (int place, int pts)[] scale) =>
        new()
        {
            Scope = scope,
            EffectiveFrom = DateOnly.Parse(effectiveFrom),
            DefaultPoints = defaultPoints,
            MaxScoringPlace = maxPlace,
            Entries = scale.Select(s => new ClubPointsRuleEntry { Place = s.place, Points = s.pts }).ToList(),
        };

    private static readonly DateOnly SomeDate = new(2026, 3, 1);

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 20)]
    [InlineData(3, 10)]
    public void PointsFor_PlaceOnScale_ReturnsScalePoints(int place, int expected)
    {
        var rules = new[] { Rule("all", "2025-09-01", defaultPoints: 1, maxPlace: 8, (1, 30), (2, 20), (3, 10)) };
        Assert.Equal(expected, ClubPointsScoring.PointsFor(rules, place, timeFail: false, isMasters: false, SomeDate));
    }

    [Fact]
    public void PointsFor_PlaceBeyondMaxScoringPlace_ReturnsDefaultPoints()
    {
        var rules = new[] { Rule("all", "2025-09-01", defaultPoints: 1, maxPlace: 8, (1, 30)) };
        Assert.Equal(1, ClubPointsScoring.PointsFor(rules, position: 20, timeFail: false, isMasters: false, SomeDate));
    }

    [Fact]
    public void PointsFor_PlaceWithoutEntryAndNoMax_ReturnsDefaultPoints()
    {
        var rules = new[] { Rule("all", "2025-09-01", defaultPoints: 5, maxPlace: null, (1, 30)) };
        Assert.Equal(5, ClubPointsScoring.PointsFor(rules, position: 7, timeFail: false, isMasters: false, SomeDate));
    }

    [Fact]
    public void PointsFor_TimeFail_ReturnsZero()
    {
        var rules = new[] { Rule("all", "2025-09-01", 1, 8, (1, 30)) };
        Assert.Equal(0, ClubPointsScoring.PointsFor(rules, position: 1, timeFail: true, isMasters: false, SomeDate));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void PointsFor_NoOrInvalidPlace_ReturnsZero(int? position)
    {
        var rules = new[] { Rule("all", "2025-09-01", 1, 8, (1, 30)) };
        Assert.Equal(0, ClubPointsScoring.PointsFor(rules, position, timeFail: false, isMasters: false, SomeDate));
    }

    [Fact]
    public void PointsFor_NoApplicableRule_ReturnsZero()
    {
        // Правило вступает в силу позже даты заплыва.
        var rules = new[] { Rule("all", "2027-01-01", 1, 8, (1, 30)) };
        Assert.Equal(0, ClubPointsScoring.PointsFor(rules, position: 1, timeFail: false, isMasters: false, SomeDate));
    }

    [Fact]
    public void PointsFor_SameDate_ScopeSpecificBeatsAll_ForMasters()
    {
        // Регрессия бага 8.5: на одну дату есть и "all", и "masters". Masters-заплыв должен
        // считаться по masters-правилу (12 за первое), а не по "all" (30).
        var rules = new[]
        {
            Rule("all",     "2025-09-01", 1, 8, (1, 30), (2, 26)),
            Rule("masters", "2025-09-01", 1, 8, (1, 12), (2, 11)),
        };

        Assert.Equal(12, ClubPointsScoring.PointsFor(rules, position: 1, timeFail: false, isMasters: true, SomeDate));
        // Non-masters заплыв на те же правила — по "all" (masters-правило ему не подходит).
        Assert.Equal(30, ClubPointsScoring.PointsFor(rules, position: 1, timeFail: false, isMasters: false, SomeDate));
    }

    [Fact]
    public void PointsFor_NonMastersScope_UsedForNonMastersSwim()
    {
        var rules = new[]
        {
            Rule("all",         "2025-09-01", 1, 8, (1, 30)),
            Rule("non-masters", "2025-09-01", 1, 8, (1, 40)),
        };
        Assert.Equal(40, ClubPointsScoring.PointsFor(rules, position: 1, timeFail: false, isMasters: false, SomeDate));
        // Masters-заплыв: non-masters-правило не подходит, остаётся "all".
        Assert.Equal(30, ClubPointsScoring.PointsFor(rules, position: 1, timeFail: false, isMasters: true, SomeDate));
    }

    [Fact]
    public void PointsFor_MultipleVersions_LatestEffectiveWins()
    {
        var rules = new[]
        {
            Rule("all", "2024-09-01", 1, 8, (1, 20)),
            Rule("all", "2025-09-01", 1, 8, (1, 30)),
        };
        // Заплыв в марте 2026 — действует версия 2025-09-01.
        Assert.Equal(30, ClubPointsScoring.PointsFor(rules, position: 1, timeFail: false, isMasters: false, new DateOnly(2026, 3, 1)));
        // Заплыв в марте 2025 — ещё действует версия 2024-09-01.
        Assert.Equal(20, ClubPointsScoring.PointsFor(rules, position: 1, timeFail: false, isMasters: false, new DateOnly(2025, 3, 1)));
    }
}
