using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Правило «текущий клуб» (К2 docs/plans/swimmer-club-change-plan.md, Р94): клуб последнего
/// соревнования, без псевдоклубов, склеенные — приёмником, ничья — не менять, закреплённый — не трогать.
/// </summary>
public class SwimmerCurrentClubRuleTests
{
    private const int Dolphin = 438, Raanana = 449, Israel = 900, OldDolphin = 77, Other = 500;

    private static readonly Dictionary<int, ClubFacts> Clubs = new()
    {
        [Dolphin] = new(false, null),
        [Raanana] = new(false, null),
        [Other] = new(false, null),
        [Israel] = new(true, null),            // псевдоклуб-сборная
        [OldDolphin] = new(false, Dolphin),    // склеен в Дельфин
    };

    private static SwimmerClubRow Row(int club, int competition, string date, int? eventId = null) =>
        new(club, competition, eventId, DateTime.Parse(date));

    [Fact]
    public void LastCompetitionWins_ExampleOf6073()
    {
        var rows = new[]
        {
            Row(Dolphin, 1700, "2026-03-10"), Row(Dolphin, 1700, "2026-03-10"),
            Row(Dolphin, 1800, "2026-07-20"),
            Row(Raanana, 1874, "2026-11-05", eventId: 90), Row(Raanana, 1875, "2026-11-06", eventId: 90),
        };

        Assert.Equal(Raanana, SwimmerCurrentClubRule.Decide(Dolphin, false, rows, Clubs));
    }

    [Fact]
    public void AlreadyCurrent_NoChange()
    {
        var rows = new[] { Row(Dolphin, 1, "2025-01-01"), Row(Raanana, 2, "2026-01-01") };
        Assert.Null(SwimmerCurrentClubRule.Decide(Raanana, false, rows, Clubs));
    }

    [Fact]
    public void Locked_NeverChanged()
    {
        var rows = new[] { Row(Raanana, 2, "2026-01-01") };
        Assert.Null(SwimmerCurrentClubRule.Decide(Dolphin, true, rows, Clubs));
    }

    [Fact]
    public void PseudoClubRows_Ignored()
    {
        // Последний старт — за сборную: клуб остаётся клубом последнего НАСТОЯЩЕГО старта.
        var rows = new[] { Row(Raanana, 1, "2026-01-01"), Row(Israel, 2, "2026-08-01") };
        Assert.Equal(Raanana, SwimmerCurrentClubRule.Decide(Dolphin, false, rows, Clubs));
    }

    [Fact]
    public void OnlyPseudoRows_NoChange()
    {
        var rows = new[] { Row(Israel, 2, "2026-08-01") };
        Assert.Null(SwimmerCurrentClubRule.Decide(null, false, rows, Clubs));
        Assert.Null(SwimmerCurrentClubRule.Decide(Dolphin, false, rows, Clubs));
    }

    [Fact]
    public void MergedClub_CountsAsItsTarget()
    {
        var rows = new[] { Row(Raanana, 1, "2025-01-01"), Row(OldDolphin, 2, "2026-01-01") };
        Assert.Equal(Dolphin, SwimmerCurrentClubRule.Decide(Raanana, false, rows, Clubs));
        // Карточка на склеенном клубе, а строки уже на приёмнике — тоже правится на приёмника.
        Assert.Equal(Dolphin, SwimmerCurrentClubRule.Decide(OldDolphin, false, [Row(Dolphin, 3, "2026-02-01")], Clubs));
    }

    [Fact]
    public void MergedRowsAddUp_WithTargetRows_InTheSameCompetition()
    {
        // 1 строка Дельфина + 1 склеенного = 2 против 1 у Раананы.
        var rows = new[] { Row(Dolphin, 5, "2026-01-01"), Row(OldDolphin, 5, "2026-01-01"), Row(Raanana, 5, "2026-01-01") };
        Assert.Equal(Dolphin, SwimmerCurrentClubRule.Decide(Raanana, false, rows, Clubs));
    }

    [Fact]
    public void TwoClubsInLastCompetition_MoreRowsWins()
    {
        var rows = new[]
        {
            Row(Dolphin, 1, "2025-01-01"),
            Row(Raanana, 2, "2026-01-01"), Row(Raanana, 2, "2026-01-01"), Row(Other, 2, "2026-01-01"),
        };
        Assert.Equal(Raanana, SwimmerCurrentClubRule.Decide(Dolphin, false, rows, Clubs));
    }

    [Fact]
    public void GroupedRows_CountWeighs_AsManyRows()
    {
        // Запрос отдаёт строки сгруппированными: (клуб, соревнование) → число строк.
        var rows = new[]
        {
            new SwimmerClubRow(Raanana, 2, null, DateTime.Parse("2026-01-01"), Count: 3),
            new SwimmerClubRow(Other, 2, null, DateTime.Parse("2026-01-01"), Count: 2),
        };
        Assert.Equal(Raanana, SwimmerCurrentClubRule.Decide(Dolphin, false, rows, Clubs));
        Assert.Equal(3, SwimmerCurrentClubRule.Periods(rows, Clubs).Single(p => p.ClubId == Raanana).Rows);
    }

    [Fact]
    public void TwoClubsInLastCompetition_Tie_NoChange()
    {
        var rows = new[] { Row(Raanana, 2, "2026-01-01"), Row(Other, 2, "2026-01-01") };
        Assert.Null(SwimmerCurrentClubRule.Decide(Dolphin, false, rows, Clubs));
        Assert.Null(SwimmerCurrentClubRule.Decide(Raanana, false, rows, Clubs));
    }

    [Fact]
    public void MultiDayEvent_IsOneCompetition_CountedAcrossDays()
    {
        // День 1 — три заплыва за Раанану, день 2 — один за другой клуб: соревнование одно,
        // строк Раананы больше. Если бы считали по дню, победил бы клуб последнего дня.
        var rows = new[]
        {
            Row(Raanana, 10, "2026-11-05", eventId: 7), Row(Raanana, 10, "2026-11-05", eventId: 7),
            Row(Raanana, 10, "2026-11-05", eventId: 7), Row(Other, 11, "2026-11-06", eventId: 7),
        };
        Assert.Equal(Raanana, SwimmerCurrentClubRule.Decide(Dolphin, false, rows, Clubs));
    }

    [Fact]
    public void TwoCompetitionsSameDate_CountedTogether()
    {
        var rows = new[]
        {
            Row(Dolphin, 1, "2026-01-01"), Row(Dolphin, 1, "2026-01-01"),
            Row(Raanana, 2, "2026-01-01"),
        };
        Assert.Equal(Dolphin, SwimmerCurrentClubRule.Decide(Raanana, false, rows, Clubs));
    }

    [Fact]
    public void EmptyCard_GetsClub()
    {
        Assert.Equal(Raanana, SwimmerCurrentClubRule.Decide(null, false, [Row(Raanana, 1, "2026-01-01")], Clubs));
    }

    [Fact]
    public void NoRows_NoChange() =>
        Assert.Null(SwimmerCurrentClubRule.Decide(Dolphin, false, [], Clubs));

    [Fact]
    public void UnknownClub_Ignored() =>
        Assert.Null(SwimmerCurrentClubRule.Decide(Dolphin, false, [Row(12345, 1, "2026-01-01")], Clubs));

    [Fact]
    public void MergeLoop_DoesNotHang()
    {
        var loop = new Dictionary<int, ClubFacts> { [1] = new(false, 2), [2] = new(false, 1) };
        Assert.Null(SwimmerCurrentClubRule.Resolve(1, loop));
    }

    [Fact]
    public void RepeatRun_IsStable()
    {
        var rows = new[] { Row(Dolphin, 1, "2025-01-01"), Row(Raanana, 2, "2026-01-01") };
        var first = SwimmerCurrentClubRule.Decide(Dolphin, false, rows, Clubs);
        Assert.Null(SwimmerCurrentClubRule.Decide(first, false, rows, Clubs));
    }

    [Fact]
    public void Periods_Overlap_WhenTwoClubsInterleave()
    {
        var interleaved = SwimmerCurrentClubRule.Periods(new[]
        {
            Row(Dolphin, 1, "2025-01-01"), Row(Raanana, 2, "2025-03-01"), Row(Dolphin, 3, "2025-05-01"),
        }, Clubs);
        Assert.True(SwimmerCurrentClubRule.HasOverlap(interleaved));

        var clean = SwimmerCurrentClubRule.Periods(new[]
        {
            Row(Dolphin, 1, "2025-01-01"), Row(Dolphin, 3, "2025-05-01"), Row(Raanana, 4, "2026-01-01"),
        }, Clubs);
        Assert.False(SwimmerCurrentClubRule.HasOverlap(clean));
        Assert.Equal([Dolphin, Raanana], clean.Select(p => p.ClubId));
    }
}
