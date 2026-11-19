using System.Net;
using Swimm.API.Telemetry;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Кто прислал запрос (RequestSource) и версия сборки (TelemetryVersion) — решение Влада 19.11.2026:
/// отделить наши проверки и роботов от посетителей в статистике /Admin/AppInsights.
/// </summary>
[Collection(TelemetryCollection.Name)]
public class RequestSourceTests
{
    private const string Chrome =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36";

    [Theory]
    [InlineData(Chrome, "visitor")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148 Safari/604.1", "visitor")]
    [InlineData("", "visitor")]
    [InlineData(null, "visitor")]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", "bot")]
    [InlineData("Mozilla/5.0 (compatible; bingbot/2.0; +http://www.bing.com/bingbot.htm)", "bot")]
    [InlineData("Mozilla/5.0 (compatible; YandexBot/3.0)", "bot")]
    [InlineData("facebookexternalhit/1.1", "bot")]
    [InlineData("Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko; compatible; GPTBot/1.2)", "bot")]
    [InlineData("curl/8.4.0", "bot")]
    [InlineData("python-requests/2.31", "bot")]
    [InlineData("Mozilla/5.0+(compatible; UptimeRobot/2.0; http://www.uptimerobot.com/)", "uptime")]
    public void ClassifiesByUserAgent(string? userAgent, string expected) =>
        Assert.Equal(expected, RequestSource.Classify(userAgent, hasWarmupHeader: false, IPAddress.Parse("203.0.113.5")));

    [Fact]
    public void WarmupHeader_FromLoopback_IsWarmup() =>
        Assert.Equal("warmup", RequestSource.Classify(Chrome, true, IPAddress.Loopback));

    [Fact]
    public void WarmupHeader_FromIPv6Loopback_IsWarmup() =>
        Assert.Equal("warmup", RequestSource.Classify(null, true, IPAddress.IPv6Loopback));

    [Theory]
    [InlineData("203.0.113.5")]   // снаружи — заголовок ничего не даёт
    [InlineData("169.254.130.1")] // фронтенд App Service — тоже не loopback
    public void WarmupHeader_NotFromLoopback_IsIgnored(string ip) =>
        Assert.Equal("visitor", RequestSource.Classify(Chrome, true, IPAddress.Parse(ip)));

    [Fact]
    public void WarmupHeader_NoRemoteIp_IsIgnored() =>
        Assert.Equal("visitor", RequestSource.Classify(Chrome, true, null));

    [Theory]
    [InlineData("1.0.0+e88c997e07026e23fed75c764893a24cbb5f04d1", "e88c997e")]
    [InlineData("0d0f5787c5fea17d5bd7f6a5e8c081e6ed95ff30+0d0f5787c5fea17d5bd7f6a5e8c081e6ed95ff30", "0d0f5787")]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("abc", "abc")]
    [InlineData(null, "unknown")]
    [InlineData("", "unknown")]
    public void VersionShortened(string? informational, string expected) =>
        Assert.Equal(expected, TelemetryVersion.Short(informational));

    [Fact]
    public async Task Pipeline_TagsSourceAndCold()
    {
        await using var pipeline = await TelemetryTestPipeline.StartAsync();

        await pipeline.GetAsync("/competitions/1581", new Dictionary<string, string>
        {
            ["User-Agent"] = "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)",
        });
        await pipeline.GetAsync("/swimmers/5", new Dictionary<string, string> { ["User-Agent"] = Chrome });
        // TestServer не даёт адреса соединения — заголовок прогрева без loopback не срабатывает.
        await pipeline.GetAsync("/clubs/7", new Dictionary<string, string> { [RequestSource.WarmupHeader] = "1" });

        var bySource = pipeline.Spans.ToDictionary(s => s.Tag("url.path")!, s => s.Tag(RequestSource.Tag));
        Assert.Equal("bot", bySource["/competitions/1581"]);
        Assert.Equal("visitor", bySource["/swimmers/5"]);
        Assert.Equal("visitor", bySource["/clubs/7"]);
        // Тест идёт в первые 10 минут жизни процесса — запросы «холодные».
        Assert.All(pipeline.Spans, s => Assert.Equal("True", s.Tag(ColdStart.Tag)));
    }
}
