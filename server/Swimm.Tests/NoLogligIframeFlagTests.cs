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
