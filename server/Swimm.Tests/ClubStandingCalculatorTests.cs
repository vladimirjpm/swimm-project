using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты <see cref="ClubStandingCalculator"/> — общий алгоритм клубного зачёта
/// (витрина Top clubs + материализованный зачёт страницы клуба). Здесь закреплены
/// правила, расхождение в которых заметить почти невозможно: что считается «зачётным»
/// заплывом, кого считает SwimmerCount, и как расставляются места при равенстве очков.
/// </summary>
public class ClubStandingCalculatorTests
{
    private static ClubScoringRow Row(
        int clubId = 1, string? clubKey = null, int swimmerId = 1,
        int? place = 1, bool isRelay = false, int points = 10) =>
        new(clubId, clubKey ?? $"club-{clubId}", swimmerId, place, isRelay, points);

    [Fact]
    public void RanksByPoints_HighestFirst()
    {
        var rows = new[]
        {
            Row(clubId: 1, points: 10, place: 3),
            Row(clubId: 2, points: 25, place: 1),
            Row(clubId: 3, points: 18, place: 2),
        };

        var table = ClubStandingCalculator.Build(rows);

        Assert.Equal([2, 3, 1], table.Select(t => t.ClubId));
        Assert.Equal([1, 2, 3], table.Select(t => t.Rank));
    }

    [Fact]
    public void EqualPoints_ShareRank_AndNextIsSkipped()
    {
        var rows = new[]
        {
            Row(clubId: 1, clubKey: "a", points: 20),
            Row(clubId: 2, clubKey: "b", points: 20),
            Row(clubId: 3, clubKey: "c", points: 5),
        };

        var table = ClubStandingCalculator.Build(rows);

        // Спортивное ранжирование: 1, 1, 3 — а не 1, 2, 3.
        Assert.Equal([1, 1, 3], table.Select(t => t.Rank));
    }

    [Fact]
    public void Medals_CountedByPlace()
    {
        var rows = new[]
        {
            Row(place: 1), Row(place: 1), Row(place: 2), Row(place: 3), Row(place: 4), Row(place: null),
        };

        var club = Assert.Single(ClubStandingCalculator.Build(rows));

        Assert.Equal(2, club.Gold);
        Assert.Equal(1, club.Silver);
        Assert.Equal(1, club.Bronze);
    }

    [Fact]
    public void SwimmerCount_IsDistinctSwimmers_NotSwims()
    {
        // Однофамильцы схлопывались в одного, когда ключом была фамилия — считаем по SwimmerId.
        var rows = new[]
        {
            Row(swimmerId: 7), Row(swimmerId: 7), Row(swimmerId: 8),
        };

        var club = Assert.Single(ClubStandingCalculator.Build(rows));

        Assert.Equal(2, club.SwimmerCount);
        Assert.Equal(3, club.SwimCount);
    }

    [Fact]
    public void ScoringSwims_CountsOnlySwimsThatEarnedPoints()
    {
        // «Принёс очки» ≠ «доплыл»: заплыв вне шкалы правила даёт 0 и в счётчик не идёт.
        var rows = new[]
        {
            Row(points: 10), Row(points: 0), Row(points: 3),
        };

        var club = Assert.Single(ClubStandingCalculator.Build(rows));

        Assert.Equal(2, club.ScoringSwims);
        Assert.Equal(3, club.SwimCount);
        Assert.Equal(13, club.Points);
    }

    [Fact]
    public void RelayMultiplier_IsCallersJob_PointsTakenAsGiven()
    {
        // Эстафетный множитель применяет вызывающий (правило живёт в Infrastructure) —
        // калькулятор обязан взять очки как есть, не удваивая их второй раз.
        var rows = new[] { Row(isRelay: true, points: 40) };

        Assert.Equal(40, Assert.Single(ClubStandingCalculator.Build(rows)).Points);
    }

    [Fact]
    public void ByName_MergesSameNamedClubs_ById_KeepsThemApart()
    {
        // Два разных Club.Id с одинаковым именем — след дублей до merge.
        var rows = new[]
        {
            Row(clubId: 1, clubKey: "Dolphin", points: 10),
            Row(clubId: 2, clubKey: "Dolphin", points: 15),
        };

        var byName = ClubStandingCalculator.Build(rows);
        var byId = ClubStandingCalculator.Build(rows, ClubStandingKey.ById);

        Assert.Equal(25, Assert.Single(byName).Points);
        Assert.Equal(2, byId.Count);
    }

    [Fact]
    public void EmptyInput_GivesEmptyTable()
    {
        Assert.Empty(ClubStandingCalculator.Build([]));
    }
}
