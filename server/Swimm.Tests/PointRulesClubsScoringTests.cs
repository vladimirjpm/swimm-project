using Swimm.Domain.Entities;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Юнит-тесты чистого расчёта клубных очков (сезонный зачёт 8.5, <see cref="PointRulesClubsScoring"/>).
/// Ключевой кейс — тай-брейк правил на одну дату: scope-специфичное (masters/non-masters)
/// важнее общего "all". Это был реальный баг на смоуке 8.5 (masters давал очки по "all").
/// </summary>
public class PointRulesClubsScoringTests
{
    private static PointRuleClubs Rule(
        string scope, string effectiveFrom, int defaultPoints, int? maxPlace, params (int place, int pts)[] scale) =>
        new()
        {
            Scope = scope,
            EffectiveFrom = DateOnly.Parse(effectiveFrom),
            DefaultPoints = defaultPoints,
            MaxScoringPlace = maxPlace,
            Entries = scale.Select(s => new PointRuleClubsEntry { Place = s.place, Points = s.pts }).ToList(),
        };

    private static readonly DateOnly SomeDate = new(2026, 3, 1);

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 20)]
    [InlineData(3, 10)]
    public void PointsFor_PlaceOnScale_ReturnsScalePoints(int place, int expected)
    {
        var rules = new[] { Rule("all", "2025-09-01", defaultPoints: 1, maxPlace: 8, (1, 30), (2, 20), (3, 10)) };
        Assert.Equal(expected, PointRulesClubsScoring.PointsFor(rules, place, timeFail: false, isMasters: false, SomeDate));
    }

    [Fact]
    public void PointsFor_PlaceBeyondMaxScoringPlace_ReturnsDefaultPoints()
    {
        var rules = new[] { Rule("all", "2025-09-01", defaultPoints: 1, maxPlace: 8, (1, 30)) };
        Assert.Equal(1, PointRulesClubsScoring.PointsFor(rules, position: 20, timeFail: false, isMasters: false, SomeDate));
    }

    [Fact]
    public void PointsFor_PlaceWithoutEntryAndNoMax_ReturnsDefaultPoints()
    {
        var rules = new[] { Rule("all", "2025-09-01", defaultPoints: 5, maxPlace: null, (1, 30)) };
        Assert.Equal(5, PointRulesClubsScoring.PointsFor(rules, position: 7, timeFail: false, isMasters: false, SomeDate));
    }

    [Fact]
    public void PointsFor_TimeFail_ReturnsZero()
    {
        var rules = new[] { Rule("all", "2025-09-01", 1, 8, (1, 30)) };
        Assert.Equal(0, PointRulesClubsScoring.PointsFor(rules, position: 1, timeFail: true, isMasters: false, SomeDate));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void PointsFor_NoOrInvalidPlace_ReturnsZero(int? position)
    {
        var rules = new[] { Rule("all", "2025-09-01", 1, 8, (1, 30)) };
        Assert.Equal(0, PointRulesClubsScoring.PointsFor(rules, position, timeFail: false, isMasters: false, SomeDate));
    }

    [Fact]
    public void PointsFor_NoApplicableRule_ReturnsZero()
    {
        // Правило вступает в силу позже даты заплыва.
        var rules = new[] { Rule("all", "2027-01-01", 1, 8, (1, 30)) };
        Assert.Equal(0, PointRulesClubsScoring.PointsFor(rules, position: 1, timeFail: false, isMasters: false, SomeDate));
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

        Assert.Equal(12, PointRulesClubsScoring.PointsFor(rules, position: 1, timeFail: false, isMasters: true, SomeDate));
        // Non-masters заплыв на те же правила — по "all" (masters-правило ему не подходит).
        Assert.Equal(30, PointRulesClubsScoring.PointsFor(rules, position: 1, timeFail: false, isMasters: false, SomeDate));
    }

    [Fact]
    public void PointsFor_NonMastersScope_UsedForNonMastersSwim()
    {
        var rules = new[]
        {
            Rule("all",         "2025-09-01", 1, 8, (1, 30)),
            Rule("non-masters", "2025-09-01", 1, 8, (1, 40)),
        };
        Assert.Equal(40, PointRulesClubsScoring.PointsFor(rules, position: 1, timeFail: false, isMasters: false, SomeDate));
        // Masters-заплыв: non-masters-правило не подходит, остаётся "all".
        Assert.Equal(30, PointRulesClubsScoring.PointsFor(rules, position: 1, timeFail: false, isMasters: true, SomeDate));
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
        Assert.Equal(30, PointRulesClubsScoring.PointsFor(rules, position: 1, timeFail: false, isMasters: false, new DateOnly(2026, 3, 1)));
        // Заплыв в марте 2025 — ещё действует версия 2024-09-01.
        Assert.Equal(20, PointRulesClubsScoring.PointsFor(rules, position: 1, timeFail: false, isMasters: false, new DateOnly(2025, 3, 1)));
    }

    // --- Новые сущности: правила очков пловца (Э0 — только конфигурация/сохранение, без расчёта) ---

    private static PointRuleSwimmers SwimmerRule(int defaultPoints, params (int place, int pts)[] scale) => new()
    {
        Version = "2026.01",
        EffectiveFrom = new DateOnly(2026, 1, 1),
        Scope = "all",
        PointsSource = "placement",
        DefaultPoints = defaultPoints,
        Entries = scale.Select(s => new PointRuleSwimmersEntry { Place = s.place, Points = s.pts }).ToList(),
    };

    [Fact]
    public void SwimmerRule_Entries_HoldScalePoints()
    {
        var rule = SwimmerRule(defaultPoints: 1, (1, 9), (2, 7), (3, 6));

        Assert.Equal(9, rule.Entries.First(e => e.Place == 1).Points);
        Assert.Equal(7, rule.Entries.First(e => e.Place == 2).Points);
        Assert.Equal(6, rule.Entries.First(e => e.Place == 3).Points);
    }

    [Fact]
    public void SwimmerRule_PlaceBeyondScale_HasNoEntry_DefaultPointsUsedByCaller()
    {
        // Расчётного сервиса для пловцов ещё нет (Э1+) — здесь проверяем только, что сущность
        // корректно моделирует «место вне шкалы»: entry отсутствует, значит вызывающий код
        // должен использовать DefaultPoints, как и для PointRuleClubs.
        var rule = SwimmerRule(defaultPoints: 1, (1, 9));

        var entry = rule.Entries.FirstOrDefault(e => e.Place == 20);
        Assert.Null(entry);
        Assert.Equal(1, rule.DefaultPoints);
    }
}
