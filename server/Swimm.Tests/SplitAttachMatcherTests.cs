using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Доклейка промежуточных к строкам БАЗЫ, без переимпорта
/// (docs/plans/splits-attach-without-repull-plan.md §3; решение Влада 20.09.2026).
/// Переимпорт ради промежуточных задваивает соревнование, когда ключ upsert разъехался
/// (1581: 3561 строка, И-28) — поэтому пишем только SplitTime и TimeSplit.
/// </summary>
public class SplitAttachMatcherTests
{
    private static RelayRow Relay(long id = 10, int? heat = 3, int? lane = 4, string time = "01:52.30",
        params (int Order, int Year, string? Split)[] legs) =>
        new(id, 100, "freestyle", "4X50", heat, lane, time,
            legs.Select(l => new RelayLegRow(l.Order * 10, l.Order, l.Year, l.Split)).ToList());

    private static SplitSourceTeam Team(int heat = 3, int lane = 4, string time = "01:52.30",
        params (int Order, int Year, string? Split)[] legs) =>
        new("freestyle", "4X50", heat, lane, time,
            legs.Select(l => new SplitSourceLeg(l.Order, l.Year, l.Split)).ToList());

    private static readonly (int, int, string?)[] SourceLegs =
        [(1, 2010, "00:28.10"), (2, 2011, "00:28.40"), (3, 2010, "00:27.90"), (4, 2011, "00:27.90")];

    private static readonly (int, int, string?)[] DbLegs =
        [(1, 2010, null), (2, 2011, null), (3, 2010, null), (4, 2011, null)];

    [Fact]
    public void Relay_Matched_WritesSplitPerLegOrder()
    {
        var plan = SplitAttachMatcher.Build([Relay(legs: DbLegs)], [], [Team(legs: SourceLegs)], []);

        Assert.Equal(1, plan.Report.TeamsAttached);
        Assert.Equal(4, plan.Legs.Count);
        // Нога 1 в базе — MemberId 10 (см. Relay): время этапа обязано лечь именно на неё.
        Assert.Equal("00:28.10", plan.Legs.Single(w => w.MemberId == 10).SplitTime);
        Assert.Equal("00:27.90", plan.Legs.Single(w => w.MemberId == 40).SplitTime);
    }

    [Fact]
    public void Relay_TimeFormatDiffers_StillMatches()
    {
        // У базы формат протокола, у loglig свой: «1:52.30» и «01:52.30» — одно время.
        var plan = SplitAttachMatcher.Build(
            [Relay(time: "1:52.30", legs: DbLegs)], [], [Team(time: "01:52.30", legs: SourceLegs)], []);

        Assert.Equal(1, plan.Report.TeamsAttached);
    }

    [Fact]
    public void Relay_BirthYearsDiffer_NotWritten()
    {
        (int, int, string?)[] otherTeam = [(1, 2009, null), (2, 2009, null), (3, 2009, null), (4, 2009, null)];
        var plan = SplitAttachMatcher.Build([Relay(legs: otherTeam)], [], [Team(legs: SourceLegs)], []);

        Assert.Equal(1, plan.Report.TeamsBirthYearMismatch);
        Assert.Empty(plan.Legs);
    }

    [Fact]
    public void Relay_TwoCandidates_Ambiguous()
    {
        var plan = SplitAttachMatcher.Build(
            [Relay(id: 10, legs: DbLegs), Relay(id: 11, legs: DbLegs)], [], [Team(legs: SourceLegs)], []);

        Assert.Equal(1, plan.Report.TeamsAmbiguous);
        Assert.Empty(plan.Legs);
    }

    [Fact]
    public void Relay_NoRowInDb_Unmatched()
    {
        var plan = SplitAttachMatcher.Build(
            [Relay(lane: 7, legs: DbLegs)], [], [Team(lane: 4, legs: SourceLegs)], []);

        Assert.Equal(1, plan.Report.TeamsUnmatched);
    }

    [Fact]
    public void Relay_NoLegsInDb_OwnCounter()
    {
        // Состав заводит RelayMemberBackfillService — доклейка это не её работа.
        var plan = SplitAttachMatcher.Build([Relay()], [], [Team(legs: SourceLegs)], []);

        Assert.Equal(1, plan.Report.TeamsNoLegsInDb);
        Assert.Equal(0, plan.Report.TeamsUnmatched);
        Assert.Empty(plan.Legs);
    }

    [Fact]
    public void Relay_NoLegsInSource_OwnCounter()
    {
        var plan = SplitAttachMatcher.Build([Relay(legs: DbLegs)], [], [Team()], []);

        Assert.Equal(1, plan.Report.TeamsNoLegsInSource);
        Assert.Empty(plan.Legs);
    }

    [Fact]
    public void Swim_Matched_JoinsLapsWithSemicolon()
    {
        var row = new SwimRow(5, "backstroke", "100", "female", "01:06.36", 1981, null);
        var swim = new SplitSourceSwim("backstroke", "100", "female", "01:06.36", 1981, ["31.52", "34.84"]);

        var plan = SplitAttachMatcher.Build([], [row], [], [swim]);

        Assert.Equal(1, plan.Report.SwimsAttached);
        Assert.Equal("31.52;34.84", plan.Swims.Single().TimeSplit);
        Assert.Equal(5, plan.Swims.Single().ResultId);
    }

    [Fact]
    public void Swim_SameTimeDifferentBirthYear_Unmatched()
    {
        var row = new SwimRow(5, "backstroke", "100", "female", "01:06.36", 1975, null);
        var swim = new SplitSourceSwim("backstroke", "100", "female", "01:06.36", 1981, ["31.52", "34.84"]);

        var plan = SplitAttachMatcher.Build([], [row], [], [swim]);

        Assert.Equal(1, plan.Report.SwimsUnmatched);
        Assert.Empty(plan.Swims);
    }

    [Fact]
    public void Swim_TwoRowsSameYearAndTime_Ambiguous()
    {
        var a = new SwimRow(5, "backstroke", "100", "female", "01:06.36", 1981, null);
        var b = new SwimRow(6, "backstroke", "100", "female", "01:06.36", 1981, null);
        var swim = new SplitSourceSwim("backstroke", "100", "female", "01:06.36", 1981, ["31.52", "34.84"]);

        var plan = SplitAttachMatcher.Build([], [a, b], [], [swim]);

        Assert.Equal(1, plan.Report.SwimsAmbiguous);
        Assert.Empty(plan.Swims);
    }

    [Fact]
    public void SecondRun_WritesNothing()
    {
        // Идемпотентность: значения уже стоят — план записи пуст, но счётчики «доклеено» те же.
        (int, int, string?)[] filled =
            [(1, 2010, "00:28.10"), (2, 2011, "00:28.40"), (3, 2010, "00:27.90"), (4, 2011, "00:27.90")];
        var row = new SwimRow(5, "backstroke", "100", "female", "01:06.36", 1981, "31.52;34.84");
        var swim = new SplitSourceSwim("backstroke", "100", "female", "01:06.36", 1981, ["31.52", "34.84"]);

        var plan = SplitAttachMatcher.Build([Relay(legs: filled)], [row], [Team(legs: SourceLegs)], [swim]);

        Assert.Empty(plan.Legs);
        Assert.Empty(plan.Swims);
        Assert.Equal(1, plan.Report.TeamsAttached);
        Assert.Equal(1, plan.Report.SwimsAttached);
        Assert.Equal(0, plan.Report.LegWrites);
        Assert.Equal(0, plan.Report.SwimWrites);
    }
}
