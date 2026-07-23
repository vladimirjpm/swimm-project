using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>Парс/формат времени заплыва (SwimTime) — согласован с импортом.</summary>
public class SwimTimeTests
{
    [Theory]
    [InlineData("58.21", 58210)]
    [InlineData("1:02.34", 62340)]
    [InlineData("58,21", 58210)]      // запятая = точка
    [InlineData("2:05.6", 125600)]    // 1 знак дроби → сотые *10
    [InlineData("0.5", 500)]
    public void ParseToMs_ParsesKnownFormats(string text, int expectedMs)
        => Assert.Equal(expectedMs, SwimTime.ParseToMs(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1:2:3")]
    public void ParseToMs_ReturnsNull_OnBadInput(string text)
        => Assert.Null(SwimTime.ParseToMs(text));

    [Theory]
    [InlineData(58210, "58.21")]
    [InlineData(62340, "1:02.34")]
    [InlineData(500, "0.50")]
    [InlineData(125600, "2:05.60")]
    public void FormatMs_FormatsWithHundredths(int ms, string expected)
        => Assert.Equal(expected, SwimTime.FormatMs(ms));

    [Fact]
    public void FormatMs_Null_ReturnsEmpty() => Assert.Equal("", SwimTime.FormatMs(null));

    [Theory]
    [InlineData("58.21")]
    [InlineData("1:02.34")]
    [InlineData("12:59.99")]
    public void RoundTrip_TextToMsToText_IsStable(string text)
        => Assert.Equal(text, SwimTime.FormatMs(SwimTime.ParseToMs(text)));
}
