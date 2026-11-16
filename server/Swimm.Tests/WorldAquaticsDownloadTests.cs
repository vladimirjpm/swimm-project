using System.Net;
using Swimm.Parsing.RecordSources;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Скачивание файла World Aquatics с повторами — общее для worldrecords и прогона по странам.
/// Повод (16.11.2026): шлюз источника режет отчёты 504-м, и один такой ответ ронял весь
/// источник worldrecords.
/// </summary>
public class WorldAquaticsDownloadTests
{
    private static readonly Uri Url = new("https://api.worldaquatics.com/fina/records/report?pool=SCM&recordCode=WR");

    private sealed class ScriptedHandler(params Func<HttpResponseMessage>[] steps) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var step = steps[Math.Min(Calls, steps.Length - 1)];
            Calls++;
            return Task.FromResult(step());
        }
    }

    private static HttpResponseMessage Status(HttpStatusCode code) => new(code);
    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };

    private static Task<MemoryStream> Download(HttpMessageHandler handler) =>
        WorldAquaticsDownload.DownloadAsync(new HttpClient(handler), Url, "WR SCM", TimeSpan.Zero, CancellationToken.None);

    [Fact]
    public async Task Gateway_timeout_is_retried_until_success()
    {
        var handler = new ScriptedHandler(() => Status(HttpStatusCode.GatewayTimeout), Ok);

        using var ms = await Download(handler);

        Assert.Equal(2, handler.Calls);
        Assert.Equal(3, ms.Length);
    }

    [Fact]
    public async Task Gives_up_after_max_attempts_and_names_the_file()
    {
        var handler = new ScriptedHandler(() => Status(HttpStatusCode.GatewayTimeout));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Download(handler));

        Assert.Equal(WorldAquaticsDownload.MaxAttempts, handler.Calls);
        Assert.Contains("WR SCM", ex.Message);
        Assert.Contains("504", ex.Message);
    }

    [Fact]
    public async Task Client_error_is_not_retried()
    {
        var handler = new ScriptedHandler(() => Status(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Download(handler));

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Network_failure_like_dns_is_retried()
    {
        // «No such host is known» приходит как HttpRequestException — так упал wa-masters 16.11.
        var handler = new ScriptedHandler(
            () => throw new HttpRequestException("No such host is known. (www.worldaquatics.com:443)"),
            Ok);

        using var ms = await Download(handler);

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_retried()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = new ScriptedHandler(Ok);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            WorldAquaticsDownload.DownloadAsync(new HttpClient(handler), Url, "WR SCM", TimeSpan.Zero, cts.Token));
    }
}
