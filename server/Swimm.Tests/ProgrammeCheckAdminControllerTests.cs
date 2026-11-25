using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Swimm.API.Controllers;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Р123: эндпоинты кнопок карточки — <c>POST api/admin/programme-check/{id}/check|split</c>.
/// Antiforgery и роль проверяются настоящим конвейером MVC на TestServer; отказ и успех — прямым
/// вызовом контроллера. Аудит пишет сервис (<c>ProgrammeCheckServiceTests</c>).
/// </summary>
public class ProgrammeCheckAdminControllerTests
{
    private sealed class RoleAuth(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e)
        : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            var id = new ClaimsIdentity([new Claim(ClaimTypes.Name, "utest"), new Claim(ClaimTypes.Role, role)], "test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(id), "test")));
        }
    }

    private static async Task<WebApplication> StartAsync(IProgrammeCheckService service)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(ProgrammeCheckAdminController).Assembly);
        builder.Services.AddAntiforgery(o => o.HeaderName = "X-XSRF-TOKEN");
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, RoleAuth>("test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(service);
        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private static Task<HttpResponseMessage> PostAsync(WebApplication app, string action, string role)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/programme-check/7/{action}");
        req.Headers.Add("X-Test-Role", role);
        return app.GetTestClient().SendAsync(req);
    }

    [Theory]
    [InlineData("check")]
    [InlineData("split")]
    public async Task Post_WithoutAntiforgeryToken_Rejected_ServiceNotCalled(string action)
    {
        var service = new Mock<IProgrammeCheckService>(MockBehavior.Strict);
        await using var app = await StartAsync(service.Object);

        var res = await PostAsync(app, action, "Admin");

        Assert.True(res.StatusCode == HttpStatusCode.BadRequest, await res.Content.ReadAsStringAsync());
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("check")]
    [InlineData("split")]
    public async Task Post_NotAdmin_Forbidden_ServiceNotCalled(string action)
    {
        var service = new Mock<IProgrammeCheckService>(MockBehavior.Strict);
        await using var app = await StartAsync(service.Object);

        var res = await PostAsync(app, action, "User");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Split_Refused_Conflict_PassesFingerprint()
    {
        var service = new Mock<IProgrammeCheckService>();
        service.Setup(s => s.SplitAsync(7, "2025-03-28=1072;2025-03-29=1618", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProgrammeSplitOutcome.Refused("план изменился — проверьте ещё раз"));

        var result = await new ProgrammeCheckAdminController(service.Object)
            .Split(7, new("2025-03-28=1072;2025-03-29=1618"), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("план изменился", Json(conflict.Value).GetProperty("error").GetString());
    }

    private static CompetitionDaySplitReport Report() => new(1665, "אליפות עמק חפר", 12160,
        [new(new DateOnly(2025, 3, 28), 1, 1700, false, 1072, 0), new(new DateOnly(2025, 3, 29), 2, 1665, true, 1618, 0)],
        new Dictionary<string, int>(), null, true, 40);

    [Fact]
    public async Task Split_Success_Message()
    {
        var service = new Mock<IProgrammeCheckService>();
        service.Setup(s => s.SplitAsync(7, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProgrammeSplitOutcome(null, Report()));

        var result = await new ProgrammeCheckAdminController(service.Object).Split(7, new("x"), CancellationToken.None);

        var json = Json(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(40, json.GetProperty("eventId").GetInt32());
        Assert.Contains("день 1 28/03/2025 #1700 — 1072 строк", json.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("recalcError").ValueKind);
    }

    [Fact]
    public async Task Split_AppliedButRecalcFailed_SaysSo()
    {
        var service = new Mock<IProgrammeCheckService>();
        service.Setup(s => s.SplitAsync(7, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProgrammeSplitOutcome(null, Report(), "timeout"));

        var result = await new ProgrammeCheckAdminController(service.Object).Split(7, new("x"), CancellationToken.None);

        var json = Json(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Contains("ПЕРЕСЧЁТ НЕ ЗАВЕРШЁН", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Check_ReturnsVerdictContract()
    {
        var service = new Mock<IProgrammeCheckService>();
        service.Setup(s => s.CheckAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(new ProgrammeCheckVerdict(
            ProgrammeVerdictKind.OnlyKnownDays, "В привязанной программе loglig только те дни", [], "h", null, null, false,
            AcceptWarning: "день, похоже, есть на loglig", Candidates: [new(11590, "תחרות", [new DateOnly(2024, 12, 7)])]));

        var result = await new ProgrammeCheckAdminController(service.Object).Check(7, CancellationToken.None);

        var json = Json(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("OnlyKnownDays", json.GetProperty("kind").GetString());
        var cand = json.GetProperty("candidates")[0];
        Assert.Equal(11590, cand.GetProperty("logligId").GetInt32());
        Assert.Equal("07.12.2024", cand.GetProperty("dates")[0].GetString());
        Assert.False(cand.TryGetProperty("url", out _)); // только номер — адрес собирает клиент
        Assert.False(json.GetProperty("acceptPrimary").GetBoolean());
        Assert.False(json.GetProperty("canSplit").GetBoolean());
        foreach (var p in new[] { "headline", "lines", "hint", "splitCompetitionId", "splitBlocked", "acceptWarning", "planFingerprint" })
            Assert.True(json.TryGetProperty(p, out _), p);
    }

    private static JsonElement Json(object? value) =>
        JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
