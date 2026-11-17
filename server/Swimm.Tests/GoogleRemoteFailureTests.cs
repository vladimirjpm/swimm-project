using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Swimm.API.Security;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сорвавшийся возврат от Google (GoogleRemoteFailure): раньше — исключение и страница 500 (две такие
/// в телеметрии прода 17.11.2026), теперь — редирект на страницу, откуда пошли входить, или на главную.
/// Настоящий обработчик Google на TestServer, в Google никто не ходит: все случаи отваливаются раньше
/// обмена кода на токен.
/// </summary>
public class GoogleRemoteFailureTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Services
            .AddAuthentication(o =>
            {
                o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                o.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
            })
            .AddCookie()
            .AddCookie(AuthSchemes.External)
            .AddGoogle(o =>
            {
                o.ClientId = "test-client";
                o.ClientSecret = "test-secret";
                o.CallbackPath = "/signin-google";
                o.SignInScheme = AuthSchemes.External;
                o.Events.OnRemoteFailure = GoogleRemoteFailure.HandleAsync;
            });

        _app = builder.Build();
        _app.UseAuthentication();
        _app.MapGet("/", () => "home");
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.BaseAddress = new Uri("https://swimhub.co.il");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    /// <summary>state, который поставил бы LoginGoogle: адрес возврата с returnUrl.</summary>
    private string StateFor(string returnUrl)
    {
        var options = _app.Services.GetRequiredService<IOptionsMonitor<GoogleOptions>>().Get(GoogleDefaults.AuthenticationScheme);
        var props = new AuthenticationProperties
        {
            RedirectUri = "/auth/google-callback?returnUrl=" + Uri.EscapeDataString(returnUrl),
        };
        return options.StateDataFormat.Protect(props);
    }

    private async Task<HttpResponseMessage> Get(string url)
    {
        // TestServer-клиент редиректы сам не проходит — видим 302 как есть.
        return await _client.GetAsync(url);
    }

    [Fact]
    public async Task GarbageState_RedirectsHome_Not500()
    {
        var r = await Get("/signin-google?state=garbage&code=x");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/", r.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task NoParams_RedirectsHome()
    {
        var r = await Get("/signin-google");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/", r.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task CancelAtGoogle_ReturnsToStartingPage()
    {
        // «Отмена» на экране Google: error=access_denied и наш state. Куки корреляции в тесте нет,
        // поэтому обработчик отваливается на ней (она проверяется раньше error) — исход тот же:
        // RemoteFailure со state, редирект на исходную страницу.
        var state = Uri.EscapeDataString(StateFor("/swimmers/63553"));
        var r = await Get($"/signin-google?error=access_denied&state={state}");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/swimmers/63553", r.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task StaleTab_CorrelationFailed_ReturnsToStartingPage()
    {
        // Настоящий state, но транзитной куки корреляции нет (старая вкладка, «назад»).
        var state = Uri.EscapeDataString(StateFor("https://swimhub.co.il/competitions?x=1"));
        var r = await Get($"/signin-google?code=abc&state={state}");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("https://swimhub.co.il/competitions?x=1", r.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ForeignReturnUrl_InState_GoesHome()
    {
        var state = Uri.EscapeDataString(StateFor("https://evil.example/phish"));
        var r = await Get($"/signin-google?error=access_denied&state={state}");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/", r.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("/auth/google-callback", null)]
    [InlineData("/auth/google-callback?returnUrl=", null)]
    [InlineData("/auth/google-callback?returnUrl=%2Fclubs%2F7%3Ftab%3Dteam", "/clubs/7?tab=team")]
    public void ReturnUrlFrom_ParsesCallbackAddress(string? redirectUri, string? expected) =>
        Assert.Equal(expected, GoogleRemoteFailure.ReturnUrlFrom(redirectUri));
}
