using Swimm.Application.Dtos;
using Xunit;

namespace Swimm.Tests;

/// <summary>Признак «нет loglig-iframe» — «Протокола нет и не будет» только для этой причины.</summary>
public class NoLogligIframeFlagTests
{
    private static UnifiedSiteInfo Site(int? logligId, string? error) =>
        new() { LogligId = logligId, LastError = error };

    [Fact]
    public void True_WhenNoIdAndIframeError() =>
        Assert.True(Site(null, DiscoveryErrors.NoLogligIframe).NoLogligIframe);

    [Theory]
    [InlineData("сеть недоступна")]
    [InlineData("The request was canceled due to the configured HttpClient.Timeout")]
    [InlineData(null)]
    public void False_ForOtherErrors(string? error) =>
        Assert.False(Site(null, error).NoLogligIframe);

    [Fact]
    public void False_WhenLogligIdAppeared() =>
        Assert.False(Site(123, DiscoveryErrors.NoLogligIframe).NoLogligIframe);
}

/// <summary>Две ступени: меньше 7 дней после последнего дня — «пока нет», 7 и больше — «не будет».</summary>
public class NoLogligIframeRulesTests
{
    private static readonly DateTime Start = new(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Threshold_IsSevenDays() => Assert.Equal(7, NoLogligIframeRules.FinalAfterDays);

    [Theory]
    [InlineData(0, false)]
    [InlineData(6, false)]   // день 6 — ещё «пока нет»
    [InlineData(7, true)]    // ровно 7 — граница уже «не будет»
    [InlineData(30, true)]
    public void SingleDay_Boundary(int daysAfter, bool final) =>
        Assert.Equal(final, NoLogligIframeRules.IsFinal(Start, Start, Start.AddDays(daysAfter).AddHours(15)));

    [Fact]
    public void MultiDay_CountsFromLastDay()
    {
        var end = Start.AddDays(2);
        Assert.False(NoLogligIframeRules.IsFinal(Start, end, end.AddDays(6)));
        Assert.True(NoLogligIframeRules.IsFinal(Start, end, end.AddDays(7)));
    }

    [Fact]
    public void FutureStart_IsNotFinal() =>
        Assert.False(NoLogligIframeRules.IsFinal(Start, Start, Start.AddDays(-3)));
}
