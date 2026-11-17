using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Swimm.API.Security;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сорвавшийся возврат от Google (GoogleRemoteFailure): раньше — исключение и страница 500 (две такие
/// в телеметрии прода 17.11.2026), теперь — редирект на страницу, откуда пошли входить, или на главную.
/// Причины человека («Отмена», старая вкладка, мусор) — без Warning; поломка входа (Google не обменял
/// код на токен) — Warning с исключением, иначе тихий редирект спрятал бы «вход сломан у всех».
/// Настоящий обработчик Google на TestServer; в сеть никто не ходит — обмен кода отвечает подделка.
/// </summary>
public class GoogleRemoteFailureTests : IAsyncLifetime
{
    private const string Nonce = "nonce123";

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly CapturingLoggerProvider _logs = new();

    /// <summary>Что отвечает «Google» на обмен кода на токен. По умолчанию — протухший секрет.</summary>
    private readonly FakeGoogleBackchannel _google = new(HttpStatusCode.BadRequest,
        """{"error":"invalid_client","error_description":"The OAuth client was not found."}""");

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Logging.AddProvider(_logs);
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
                o.BackchannelHttpHandler = _google;
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

    /// <summary>
    /// state, который поставил бы LoginGoogle: адрес возврата с returnUrl и (по желанию) ключ
    /// корреляции — тогда запрос с кукой <see cref="CorrelationCookie"/> проходит проверку корреляции,
    /// как настоящий свежий возврат от Google.
    /// </summary>
    private string StateFor(string returnUrl, bool withCorrelation = false)
    {
        var options = _app.Services.GetRequiredService<IOptionsMonitor<GoogleOptions>>().Get(GoogleDefaults.AuthenticationScheme);
        var props = new AuthenticationProperties
        {
            RedirectUri = "/auth/google-callback?returnUrl=" + Uri.EscapeDataString(returnUrl),
        };
        if (withCorrelation) props.Items[".xsrf"] = Nonce;
        return Uri.EscapeDataString(options.StateDataFormat.Protect(props));
    }

    private const string CorrelationCookie = ".AspNetCore.Correlation." + Nonce + "=N";

    // TestServer-клиент редиректы сам не проходит — видим 302 как есть.
    private Task<HttpResponseMessage> Get(string url, bool withCorrelationCookie = false)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (withCorrelationCookie) req.Headers.Add("Cookie", CorrelationCookie);
        return _client.SendAsync(req);
    }

    private List<CapturedLog> FailureWarnings() =>
        _logs.Entries.Where(e => e.Category == GoogleRemoteFailure.LogCategory && e.Level >= LogLevel.Warning).ToList();

    private static void AssertRedirect(HttpResponseMessage r, string expected)
    {
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(expected, r.Headers.Location?.OriginalString);
    }

    // ── причины человека: редирект, без Warning ─────────────────────────────

    [Fact]
    public async Task GarbageState_RedirectsHome_Not500_NoWarning()
    {
        AssertRedirect(await Get("/signin-google?state=garbage&code=x"), "/");
        Assert.Empty(FailureWarnings());
    }

    [Fact]
    public async Task NoParams_RedirectsHome_NoWarning()
    {
        AssertRedirect(await Get("/signin-google"), "/");
        Assert.Empty(FailureWarnings());
    }

    [Fact]
    public async Task CancelAtGoogle_ReturnsToStartingPage_NoWarning()
    {
        // Настоящая «Отмена»: свежий возврат с кукой корреляции и error=access_denied.
        var r = await Get($"/signin-google?error=access_denied&state={StateFor("/swimmers/63553", withCorrelation: true)}",
            withCorrelationCookie: true);
        AssertRedirect(r, "/swimmers/63553");
        Assert.Empty(FailureWarnings());
        Assert.Equal(0, _google.Calls);
    }

    [Fact]
    public async Task StaleTab_CorrelationFailed_ReturnsToStartingPage_NoWarning()
    {
        // Настоящий state, но транзитной куки корреляции нет (старая вкладка, «назад»).
        AssertRedirect(await Get($"/signin-google?code=abc&state={StateFor("https://swimhub.co.il/competitions?x=1")}"),
            "https://swimhub.co.il/competitions?x=1");
        Assert.Empty(FailureWarnings());
    }

    [Fact]
    public async Task ForeignReturnUrl_InState_GoesHome()
    {
        AssertRedirect(await Get($"/signin-google?error=access_denied&state={StateFor("https://evil.example/phish")}"), "/");
    }

    // ── поломка входа: тот же редирект, но Warning с исключением ─────────────

    [Fact]
    public async Task TokenExchangeFails_RedirectsBack_AndWarnsWithException()
    {
        var r = await Get($"/signin-google?code=abc&state={StateFor("/clubs/7", withCorrelation: true)}",
            withCorrelationCookie: true);

        AssertRedirect(r, "/clubs/7");
        Assert.Equal(1, _google.Calls);
        var warning = Assert.Single(FailureWarnings());
        Assert.NotNull(warning.Exception); // с исключением → в Azure это AppExceptions
        Assert.Contains("invalid_client", warning.Message);
    }

    [Fact]
    public async Task OtherGoogleError_Warns()
    {
        // Google вернул ошибку, но не «Отмену» — например, server_error.
        var r = await Get($"/signin-google?error=server_error&state={StateFor("/", withCorrelation: true)}",
            withCorrelationCookie: true);
        AssertRedirect(r, "/");
        Assert.Single(FailureWarnings());
    }

    // ── чистые функции ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("Correlation failed.", null, true)]
    [InlineData("The oauth state was missing or invalid.", null, true)]
    [InlineData("Access was denied by the resource owner or by the remote server.", "access_denied", true)]
    [InlineData("OAuth token endpoint failure: invalid_client", null, false)]
    [InlineData("Response status code does not indicate success: 500", null, false)]
    [InlineData("server_error", "server_error", false)]
    [InlineData(null, null, false)]
    public void IsUserCaused_Classifies(string? message, string? errorParam, bool expected) =>
        Assert.Equal(expected, GoogleRemoteFailure.IsUserCaused(message is null ? null : new Exception(message), errorParam));

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("/auth/google-callback", null)]
    [InlineData("/auth/google-callback?returnUrl=", null)]
    [InlineData("/auth/google-callback?returnUrl=%2Fclubs%2F7%3Ftab%3Dteam", "/clubs/7?tab=team")]
    public void ReturnUrlFrom_ParsesCallbackAddress(string? redirectUri, string? expected) =>
        Assert.Equal(expected, GoogleRemoteFailure.ReturnUrlFrom(redirectUri));

    // ── подделки ────────────────────────────────────────────────────────────

    private sealed class FakeGoogleBackchannel(HttpStatusCode status, string json) : HttpMessageHandler
    {
        private int _calls;
        public int Calls => _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed record CapturedLog(string Category, LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<CapturedLog> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, Entries);
        public void Dispose() { }

        private sealed class Logger(string category, ConcurrentQueue<CapturedLog> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                sink.Enqueue(new CapturedLog(category, logLevel, formatter(state, exception), exception));
        }
    }
}
