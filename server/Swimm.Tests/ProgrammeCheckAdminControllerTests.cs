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
/// Antiforgery проверяется настоящим конвейером MVC на TestServer (админ вошёл, токена нет → 400);
/// отказ и успех — прямым вызовом контроллера.
/// </summary>
public class ProgrammeCheckAdminControllerTests
{
    private sealed class AdminAuth(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e)
        : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var id = new ClaimsIdentity([new Claim(ClaimTypes.Name, "utest-admin"), new Claim(ClaimTypes.Role, "Admin")], "test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(id), "test")));
        }
    }

    [Theory]
    [InlineData("check")]
    [InlineData("split")]
    public async Task Post_WithoutAntiforgeryToken_Rejected_ServiceNotCalled(string action)
    {
        var service = new Mock<IProgrammeCheckService>(MockBehavior.Strict);
        var audit = new Mock<IAdminAuditService>(MockBehavior.Strict);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(ProgrammeCheckAdminController).Assembly);
        builder.Services.AddAntiforgery(o => o.HeaderName = "X-XSRF-TOKEN");
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, AdminAuth>("test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(service.Object);
        builder.Services.AddSingleton(audit.Object);
        await using var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();

        var res = await app.GetTestClient().PostAsync($"/api/admin/programme-check/7/{action}", null);

        Assert.True(res.StatusCode == HttpStatusCode.BadRequest, await res.Content.ReadAsStringAsync());
        service.VerifyNoOtherCalls();
        audit.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Split_Refused_Conflict_NoAudit()
    {
        var service = new Mock<IProgrammeCheckService>();
        service.Setup(s => s.SplitAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProgrammeSplitOutcome.Refused("у 40 строк нет однозначного дня — всё или ничего"));
        var audit = new Mock<IAdminAuditService>(MockBehavior.Strict);

        var result = await new ProgrammeCheckAdminController(service.Object, audit.Object).Split(7, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("нет однозначного дня", Json(conflict.Value).GetProperty("error").GetString());
        audit.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Split_Success_AuditedAsSplitDays()
    {
        var report = new CompetitionDaySplitReport(1665, "אליפות עמק חפר", 12160,
            [new(new DateOnly(2025, 3, 28), 1, 1700, false, 1072, 0), new(new DateOnly(2025, 3, 29), 2, 1665, true, 1618, 0)],
            new Dictionary<string, int>(), null, true, 40);
        var service = new Mock<IProgrammeCheckService>();
        service.Setup(s => s.SplitAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(new ProgrammeSplitOutcome(null, report));
        var audit = new Mock<IAdminAuditService>();

        var result = await new ProgrammeCheckAdminController(service.Object, audit.Object).Split(7, CancellationToken.None);

        var json = Json(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(40, json.GetProperty("eventId").GetInt32());
        Assert.Contains("день 1 28/03/2025 #1700 — 1072 строк", json.GetProperty("message").GetString());
        audit.Verify(a => a.LogAsync("competition.split-days", "Competition", "1665",
            It.Is<string>(d => d.Contains("событие #40") && d.Contains("discovery #7")),
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Check_ReturnsVerdictContract()
    {
        var service = new Mock<IProgrammeCheckService>();
        service.Setup(s => s.CheckAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(new ProgrammeCheckVerdict(
            ProgrammeVerdictKind.OnlyKnownDays, "В привязанной программе loglig только те дни", [], "h", null, null, false,
            AcceptWarning: "день, похоже, есть на loglig", Candidates: [new(11590, "תחרות", [new DateOnly(2024, 12, 7)])]));

        var result = await new ProgrammeCheckAdminController(service.Object, Mock.Of<IAdminAuditService>()).Check(7, CancellationToken.None);

        var json = Json(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("OnlyKnownDays", json.GetProperty("kind").GetString());
        var cand = json.GetProperty("candidates")[0];
        Assert.Equal(11590, cand.GetProperty("logligId").GetInt32());
        Assert.Equal("07.12.2024", cand.GetProperty("dates")[0].GetString());
        Assert.False(cand.TryGetProperty("url", out _)); // только номер — адрес собирает клиент
        Assert.False(json.GetProperty("acceptPrimary").GetBoolean());
        Assert.False(json.GetProperty("canSplit").GetBoolean());
        foreach (var p in new[] { "headline", "lines", "hint", "splitCompetitionId", "splitBlocked", "acceptWarning" })
            Assert.True(json.TryGetProperty(p, out _), p);
    }

    private static JsonElement Json(object? value) =>
        JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
