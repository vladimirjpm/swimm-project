using Swimm.Domain;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты <see cref="SeasonMath"/> — календарь сезона (1 сен – 31 авг, метка по году начала).
/// Главное, что здесь закреплено: краевые даты 31 авг / 1 сен и то, что нумерация федерации
/// (cYear на isr.org.il) отличается от публичной ровно на 1 — их легко молча перепутать.
/// </summary>
public class SeasonMathTests
{
    [Theory]
    [InlineData(2025, 8, 31, 2024)] // последний день сезона 2024/25
    [InlineData(2025, 9, 1, 2025)]  // первый день сезона 2025/26
    [InlineData(2025, 12, 31, 2025)]
    [InlineData(2026, 1, 1, 2025)]
    [InlineData(2026, 2, 15, 2025)] // зимний чемпионат 2026 → сезон 2025/26
    [InlineData(2026, 7, 20, 2025)] // летний чемпионат 2026 → тот же сезон
    public void StartYearOf_SeasonBoundaries(int y, int m, int d, int expected)
    {
        Assert.Equal(expected, SeasonMath.StartYearOf(new DateTime(y, m, d)));
    }

    [Fact]
    public void FederationYear_IsStartYearPlusOne()
    {
        // окт-2024 … авг-2025 = cYear 2025 (проверено на списке isr.org.il).
        Assert.Equal(2025, SeasonMath.FederationYearOf(new DateTime(2024, 10, 5)));
        Assert.Equal(2025, SeasonMath.FederationYearOf(new DateTime(2025, 8, 31)));
        Assert.Equal(2026, SeasonMath.FederationYearOf(new DateTime(2025, 9, 1)));
    }

    [Theory]
    [InlineData(2025, "2025/26")]
    [InlineData(2009, "2009/10")]
    [InlineData(1999, "1999/00")]
    public void Label_UsesTwoDigitEndYear(int startYear, string expected)
    {
        Assert.Equal(expected, SeasonMath.Label(startYear));
    }

    [Fact]
    public void Range_IsHalfOpenAndUnspecifiedKind()
    {
        var (start, end) = SeasonMath.RangeOf(2025);

        Assert.Equal(new DateTime(2025, 9, 1), start);
        Assert.Equal(new DateTime(2026, 9, 1), end);
        // Kind обязан быть Unspecified: CompetitionDate — timestamp without time zone.
        Assert.Equal(DateTimeKind.Unspecified, start.Kind);
        Assert.Equal(DateTimeKind.Unspecified, end.Kind);
    }

    [Fact]
    public void IsInSeason_EdgeDays()
    {
        Assert.True(SeasonMath.IsInSeason(new DateTime(2025, 9, 1), 2025));
        Assert.True(SeasonMath.IsInSeason(new DateTime(2026, 8, 31), 2025));
        Assert.False(SeasonMath.IsInSeason(new DateTime(2026, 9, 1), 2025));
        Assert.False(SeasonMath.IsInSeason(new DateTime(2025, 8, 31), 2025));
    }
}
