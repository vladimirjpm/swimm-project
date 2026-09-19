using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Где проплыт официальный рекорд (docs/plans/record-card-plan.md §2): в справочнике поля нет,
/// ищем среди заплывов пловца — время до сотой, дисциплина, бассейн, дата ±1 день.
/// </summary>
public class RecordMeetMatcherTests
{
    private static readonly RecordMeet Winter = new(64, null, "אליפות ישראל ARENA מאסטרס חורף 2026", true, 100);
    private static readonly RecordMeet Other = new(70, 5, "גביע", false, 200);

    private static RecordMeetSwim Swim(int ms, DateTime date, RecordMeet meet,
        string dist = "50", string style = "freestyle", string pool = "25m") =>
        new(ms, date, dist, style, pool, meet);

    [Fact]
    public void IndividualSwim_SameTimeDisciplineAndDay_Found()
    {
        var meet = RecordMeetMatcher.Find("27.23", "10/01/2026", "50m", "freestyle", "25m",
            [Swim(27230, new DateTime(2026, 1, 10), Winter)], []);
        Assert.Equal(Winter, meet);
    }

    [Theory]
    [InlineData("27.24", "10/01/2026", "50m", "freestyle", "25m")]    // другое время
    [InlineData("27.23", "12/01/2026", "50m", "freestyle", "25m")]    // дата дальше дня
    [InlineData("27.23", "10/01/2026", "100m", "freestyle", "25m")]   // другая дистанция
    [InlineData("27.23", "10/01/2026", "50m", "backstroke", "25m")]   // другой стиль
    [InlineData("27.23", "10/01/2026", "50m", "freestyle", "50m")]    // другой бассейн
    [InlineData("27.23", null, "50m", "freestyle", "25m")]            // даты у рекорда нет
    public void Mismatch_NoMeet(string time, string? date, string dist, string style, string pool)
        => Assert.Null(RecordMeetMatcher.Find(time, date, dist, style, pool,
            [Swim(27230, new DateTime(2026, 1, 10), Winter)], []));

    [Fact]
    public void SeveralCandidates_ClosestDateWins()
    {
        var meet = RecordMeetMatcher.Find("27.23", "10/01/2026", "50m", "freestyle", "25m",
            [Swim(27230, new DateTime(2026, 1, 11), Other), Swim(27230, new DateTime(2026, 1, 10), Winter)], []);
        Assert.Equal(Winter, meet);
    }

    [Fact]
    public void RelayLeadOff_UsesRelayMeet()
    {
        // 30.25 Гостомельской: 50 на спине — первый этап женской 4×50 комплекс (И-28).
        var leg = new RelayLeadOffLeg("00:30.25", new DateTime(2026, 1, 10), "4X50", "individual_medley", "25m", Winter);
        Assert.Equal(Winter, RecordMeetMatcher.Find("30.25", "09/01/2026", "50m", "backstroke", "25m", [], [leg]));
    }

    [Fact]
    public void IndividualSwim_BeatsRelayLeg()
    {
        var leg = new RelayLeadOffLeg("00:26.18", new DateTime(2026, 1, 9), "4X50", "freestyle", "25m", Other);
        var meet = RecordMeetMatcher.Find("26.18", "09/01/2026", "50m", "freestyle", "25m",
            [Swim(26180, new DateTime(2026, 1, 9), Winter)], [leg]);
        Assert.Equal(Winter, meet);
    }

    [Fact]
    public void RelayLegWithoutMeet_Ignored()
    {
        var leg = new RelayLeadOffLeg("00:30.25", new DateTime(2026, 1, 10), "4X50", "individual_medley", "25m");
        Assert.Null(RecordMeetMatcher.Find("30.25", "09/01/2026", "50m", "backstroke", "25m", [], [leg]));
    }
}
