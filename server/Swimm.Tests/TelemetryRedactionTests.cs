using System.Diagnostics;
using Swimm.API.Telemetry;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Главный риск телеметрии (docs/plans/application-insights-plan.md, А2-бис): одноразовые токены
/// сброса пароля и подтверждения почты приходят в строке запроса. Ни один не должен уехать в Azure.
/// ⚠ <c>UseAzureMonitor()</c> сам выключает встроенную вычистку инструментария — поэтому здесь
/// проверяется и случай «встроенная выключена», где держит только наша.
/// </summary>
[Collection(TelemetryCollection.Name)]
public class TelemetryRedactionTests
{
    // ── Правило (чистые функции) ─────────────────────────────────────────────

    [Theory]
    [InlineData("/auth/reset-password")]
    [InlineData("/auth/verify-email")]
    [InlineData("/AUTH/Reset-Password")]
    [InlineData("/signin-google")]
    [InlineData("auth/x")]
    public void SensitivePath_Detected(string path) => Assert.True(QueryStringRedactor.IsSensitivePath(path));

    [Theory]
    [InlineData("/authors")]
    [InlineData("/api/auth-help")]
    [InlineData("/competitions/1581")]
    [InlineData("")]
    [InlineData(null)]
    public void OrdinaryPath_NotSensitive(string? path) => Assert.False(QueryStringRedactor.IsSensitivePath(path));

    [Fact]
    public void Query_OnSensitivePath_DroppedEntirely() =>
        Assert.Null(QueryStringRedactor.RedactQuery("?token=SEKRET", dropAll: true));

    [Theory]
    [InlineData("?q=Ivan%20Petrov", "?q=Redacted")]
    [InlineData("q=Ivan", "q=Redacted")]
    [InlineData("?competitionId=1581&q=x", "?competitionId=1581&q=Redacted")]
    [InlineData("?id=123456789", "?id=Redacted")]            // 9 цифр — теудат-зеут, не оставляем
    [InlineData("?id=12345678", "?id=12345678")]
    [InlineData("?SEKRET", "?Redacted")]                     // значение без ключа
    [InlineData("?empty=&flag", "?empty=&Redacted")]
    [InlineData("?", "?")]
    public void Query_ValuesRedacted_ShortNumbersKept(string input, string expected) =>
        Assert.Equal(expected, QueryStringRedactor.RedactQuery(input, dropAll: false));

    [Fact]
    public void Query_LongKey_Redacted() =>
        Assert.Equal("?Redacted", QueryStringRedactor.RedactQuery("?" + new string('k', 40) + "=1", dropAll: false));

    [Theory]
    [InlineData("https://swimhub.co.il/auth/reset-password?token=SEKRET", "https://swimhub.co.il/auth/reset-password")]
    [InlineData("/auth/verify-email?token=SEKRET", "/auth/verify-email")]
    [InlineData("https://swimhub.co.il/signin-google?code=SEKRET&state=S", "https://swimhub.co.il/signin-google")]
    [InlineData("https://swimhub.co.il/api/x?q=Ivan#frag", "https://swimhub.co.il/api/x?q=Redacted")]
    [InlineData("https://isr.org.il/competitions.asp?cYear=2026", "https://isr.org.il/competitions.asp?cYear=2026")]
    [InlineData("https://swimhub.co.il/competitions", "https://swimhub.co.il/competitions")]
    public void Url_Redacted(string input, string expected) =>
        Assert.Equal(expected, QueryStringRedactor.RedactUrl(input, dropQuery: false));

    // ── Процессор на Activity ────────────────────────────────────────────────

    [Fact]
    public void Processor_StripsTokenFromEveryUrlTag()
    {
        using var activity = new Activity("test").Start();
        activity.SetTag("url.path", "/auth/reset-password");
        activity.SetTag("url.query", "?token=SEKRET");
        activity.SetTag("url.full", "https://swimhub.co.il/auth/reset-password?token=SEKRET");
        activity.SetTag("http.url", "https://swimhub.co.il/auth/reset-password?token=SEKRET");
        activity.SetTag("http.target", "/auth/reset-password?token=SEKRET");

        TelemetryRedactionProcessor.Redact(activity);

        Assert.Null(activity.GetTagItem("url.query"));
        foreach (var tag in activity.TagObjects)
            Assert.DoesNotContain("SEKRET", tag.Value?.ToString() ?? "");
    }

    // ── Живой конвейер ASP.NET Core ──────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pipeline_AuthToken_NeverExported(bool builtInRedactionOff)
    {
        await using var pipeline = await TelemetryTestPipeline.StartAsync(builtInRedactionOff);

        await pipeline.GetAsync("/auth/reset-password?token=SEKRET");
        await pipeline.GetAsync("/auth/verify-email?token=SEKRET");
        await pipeline.GetAsync("/signin-google?code=SEKRET&state=SEKRET");

        var auth = pipeline.Spans.Where(s => (s.Tag("url.path") ?? "").Contains("auth", StringComparison.OrdinalIgnoreCase)
                                          || (s.Tag("url.path") ?? "").Contains("signin", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(3, auth.Count);                     // спаны есть — проверка не пустая
        foreach (var span in pipeline.Spans)
        {
            Assert.DoesNotContain("SEKRET", span.AllText);
            Assert.Null(span.Tag("url.query"));          // у путей входа строки запроса нет вовсе
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pipeline_SearchQuery_ValueRedacted(bool builtInRedactionOff)
    {
        await using var pipeline = await TelemetryTestPipeline.StartAsync(builtInRedactionOff);

        await pipeline.GetAsync("/api/swimmers/search?q=Ivan");

        var span = Assert.Single(pipeline.Spans);
        Assert.DoesNotContain("Ivan", span.AllText);
        Assert.Contains("q=Redacted", span.Tag("url.query"));
    }
}
