using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Swimm.API.Hosting.Warmup;
using Swimm.API.Telemetry;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Прогрев после старта (StartupWarmupService, решение Влада 19.11.2026): на НАСТОЯЩЕМ Kestrel (свободный
/// порт) служба сама открывает свои адреса — с каноническим Host, https по X-Forwarded-Proto и меткой
/// прогрева, с loopback. Так проверяется и то, что на проде запрос не упрётся в редирект на https.
/// </summary>
public class StartupWarmupTests
{
    private sealed record Seen(string Path, string Host, bool IsHttps, bool WarmupHeader, bool FromLoopback);

    [Fact]
    public async Task Warmup_HitsEachPath_AsHttpsCanonicalHostFromLoopback()
    {
        var seen = new ConcurrentQueue<Seen>();
        var done = new TaskCompletionSource();
        string[] paths = ["/api/a", "/api/b?x=last"];

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [WarmupPaths.PathsKey + ":0"] = paths[0],
            [WarmupPaths.PathsKey + ":1"] = paths[1],
            ["Seo:BaseUrl"] = "https://swimhub.co.il",
        });
        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });
        builder.Services.AddHttpClient(StartupWarmupService.HttpClientName);
        builder.Services.AddHostedService<StartupWarmupService>();

        await using var app = builder.Build();
        var rawRemote = new ConcurrentDictionary<string, bool>();
        app.Use(async (ctx, next) =>
        {
            // До UseForwardedHeaders — как видит запрос инструментарий телеметрии.
            rawRemote[ctx.Request.Path + ctx.Request.QueryString] =
                ctx.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip);
            await next();
        });
        app.UseForwardedHeaders();
        app.Run(async ctx =>
        {
            var key = ctx.Request.Path + ctx.Request.QueryString;
            seen.Enqueue(new Seen(key, ctx.Request.Host.Host, ctx.Request.IsHttps,
                ctx.Request.Headers.ContainsKey(RequestSource.WarmupHeader), rawRemote.GetValueOrDefault(key)));
            if (seen.Count == paths.Length) done.TrySetResult();
            await ctx.Response.WriteAsync("ok");
        });

        await app.StartAsync();
        await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        await app.StopAsync();

        Assert.Equal(paths, seen.Select(s => s.Path).ToArray());
        Assert.All(seen, s =>
        {
            Assert.Equal("swimhub.co.il", s.Host);
            Assert.True(s.IsHttps);
            Assert.True(s.WarmupHeader);
            Assert.True(s.FromLoopback);
        });
    }

    [Theory]
    [InlineData("http://+:8080", "http://127.0.0.1:8080/")]       // App Service Linux
    [InlineData("http://*:5000", "http://127.0.0.1:5000/")]
    [InlineData("http://0.0.0.0:80", "http://127.0.0.1/")]
    [InlineData("http://[::]:8080", "http://127.0.0.1:8080/")]
    [InlineData("http://localhost:5078", "http://localhost:5078/")]
    [InlineData("http://127.0.0.1:61234", "http://127.0.0.1:61234/")]
    public void LoopbackBase_FromKestrelAddress(string address, string expected) =>
        Assert.Equal(expected, StartupWarmupService.LoopbackBase([address])?.ToString());

    [Fact]
    public void LoopbackBase_PrefersHttp_AndNullWithoutIt()
    {
        Assert.Equal("http://127.0.0.1:8080/",
            StartupWarmupService.LoopbackBase(["https://+:8443", "http://+:8080"])?.ToString());
        Assert.Null(StartupWarmupService.LoopbackBase(["https://+:8443"]));
        Assert.Null(StartupWarmupService.LoopbackBase(null));
    }

    [Fact]
    public void DefaultPaths_HaveNoUserParameters()
    {
        // Греем только то, у чего один ключ кэша на всех: ни /api/me, ни личных параметров.
        Assert.All(WarmupPaths.Default, p =>
        {
            Assert.StartsWith("/api/", p);
            Assert.DoesNotContain("/api/me", p);
        });
    }
}
