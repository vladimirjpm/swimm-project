using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Рекорд, проплытый первым этапом эстафеты (И-28): 30.25 Гостомельской — 45-49 50 на спине
/// 25 м, рекорд датирован 9/1/2026, а женская 4×50 комплекс плылась 10.01.2026.
/// </summary>
public class RelayLeadOffMatcherTests
{
    private static readonly RelayLeadOffLeg Medley =
        new("00:30.25", new DateTime(2026, 1, 10), "4X50", "individual_medley", "25m");

    [Fact]
    public void MedleyLeadOff_IsBackstroke_DateOffByOneDay()
        => Assert.True(RelayLeadOffMatcher.Matches("30.25", "09/01/2026", "50m", "backstroke", "25m", Medley));

    [Theory]
    [InlineData("30.26", "09/01/2026", "50m", "backstroke", "25m")]   // другое время
    [InlineData("30.25", "07/01/2026", "50m", "backstroke", "25m")]   // дата дальше дня
    [InlineData("30.25", "09/01/2026", "100m", "backstroke", "25m")]  // другая дистанция этапа
    [InlineData("30.25", "09/01/2026", "50m", "freestyle", "25m")]    // первый этап комплекса — спина
    [InlineData("30.25", "09/01/2026", "50m", "backstroke", "50m")]   // другой бассейн
    [InlineData("30.25", null, "50m", "backstroke", "25m")]           // даты у рекорда нет
    public void Mismatch_IsNotLeadOff(string time, string? date, string dist, string style, string pool)
        => Assert.False(RelayLeadOffMatcher.Matches(time, date, dist, style, pool, Medley));

    [Fact]
    public void FreestyleRelay_LeadOffIsFreestyle()
    {
        var leg = new RelayLeadOffLeg("00:26.18", new DateTime(2026, 1, 9), "4X50", "freestyle", "25m");
        Assert.True(RelayLeadOffMatcher.Matches("26.18", "09/01/2026", "50m", "freestyle", "25m", leg));
    }
}
