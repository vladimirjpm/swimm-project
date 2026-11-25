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

    private static async Task<WebApplication> StartAsync(IProgrammeCheckService service, ISiblingDayPullService? siblings = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(ProgrammeCheckAdminController).Assembly);
        builder.Services.AddAntiforgery(o => o.HeaderName = "X-XSRF-TOKEN");
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, RoleAuth>("test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(service);
        builder.Services.AddSingleton(siblings ?? new Mock<ISiblingDayPullService>(MockBehavior.Strict).Object);
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
    [InlineData("sibling-preview")]
    [InlineData("sibling-import")]
    public async Task Post_WithoutAntiforgeryToken_Rejected_ServiceNotCalled(string action)
    {
        var service = new Mock<IProgrammeCheckService>(MockBehavior.Strict);
        var siblings = new Mock<ISiblingDayPullService>(MockBehavior.Strict);
        await using var app = await StartAsync(service.Object, siblings.Object);

        var res = await PostAsync(app, action, "Admin");

        Assert.True(res.StatusCode == HttpStatusCode.BadRequest, await res.Content.ReadAsStringAsync());
        service.VerifyNoOtherCalls();
        siblings.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("check")]
    [InlineData("split")]
    [InlineData("sibling-preview")]
    [InlineData("sibling-import")]
    public async Task Post_NotAdmin_Forbidden_ServiceNotCalled(string action)
    {
        var service = new Mock<IProgrammeCheckService>(MockBehavior.Strict);
        var siblings = new Mock<ISiblingDayPullService>(MockBehavior.Strict);
        await using var app = await StartAsync(service.Object, siblings.Object);

        var res = await PostAsync(app, action, "User");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        service.VerifyNoOtherCalls();
        siblings.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Split_Refused_Conflict_PassesFingerprint()
    {
        var service = new Mock<IProgrammeCheckService>();
        service.Setup(s => s.SplitAsync(7, "2025-03-28=1072;2025-03-29=1618", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProgrammeSplitOutcome.Refused("план изменился — проверьте ещё раз"));

        var result = await new ProgrammeCheckAdminController(service.Object, Mock.Of<ISiblingDayPullService>())
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

        var result = await new ProgrammeCheckAdminController(service.Object, Mock.Of<ISiblingDayPullService>()).Split(7, new("x"), CancellationToken.None);

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

        var result = await new ProgrammeCheckAdminController(service.Object, Mock.Of<ISiblingDayPullService>()).Split(7, new("x"), CancellationToken.None);

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

        var result = await new ProgrammeCheckAdminController(service.Object, Mock.Of<ISiblingDayPullService>()).Check(7, CancellationToken.None);

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

    [Fact]
    public async Task Check_OneCandidate_PullCandidate_SeveralCandidates_None()
    {
        ProgrammeCheckVerdict V(params ProgrammeNeighbour[] c) => new(ProgrammeVerdictKind.OnlyKnownDays, "h", [], "x", null, null, false,
            Candidates: c);
        var one = new Mock<IProgrammeCheckService>();
        one.Setup(s => s.CheckAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(V(new ProgrammeNeighbour(11590, "t", [new DateOnly(2024, 12, 7)])));
        var two = new Mock<IProgrammeCheckService>();
        two.Setup(s => s.CheckAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(V(new ProgrammeNeighbour(11590, "t", [new DateOnly(2024, 12, 7)]), new ProgrammeNeighbour(11591, "t", [new DateOnly(2024, 12, 7)])));

        var a = Json(Assert.IsType<OkObjectResult>(await new ProgrammeCheckAdminController(one.Object, Mock.Of<ISiblingDayPullService>())
            .Check(7, CancellationToken.None)).Value);
        var b = Json(Assert.IsType<OkObjectResult>(await new ProgrammeCheckAdminController(two.Object, Mock.Of<ISiblingDayPullService>())
            .Check(7, CancellationToken.None)).Value);

        Assert.Equal(11590, a.GetProperty("pullCandidate").GetInt32());
        Assert.Equal(JsonValueKind.Null, b.GetProperty("pullCandidate").ValueKind);
    }

    private static SiblingDayImportRequest ImportRequest() =>
        new(Guid.NewGuid(), 11590, null, "תחרות חלופית ל\"מילניום\"", "25m", false, false, false, false, null);

    [Fact]
    public async Task SiblingImport_Refused_Conflict()
    {
        var siblings = new Mock<ISiblingDayPullService>();
        siblings.Setup(s => s.ImportAsync(7, It.IsAny<SiblingDayImportRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SiblingDayEnqueue.Refused("этот день уже затягивается — дождитесь конца"));
        var controller = new ProgrammeCheckAdminController(Mock.Of<IProgrammeCheckService>(), siblings.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() },
        };

        var result = await controller.SiblingImport(7, ImportRequest(), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("уже затягивается", Json(conflict.Value).GetProperty("error").GetString());
    }

    [Fact]
    public async Task SiblingImport_Queued_Accepted()
    {
        var job = Guid.NewGuid();
        var siblings = new Mock<ISiblingDayPullService>();
        siblings.Setup(s => s.ImportAsync(7, It.IsAny<SiblingDayImportRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiblingDayEnqueue(null, job, null, "תחרות"));
        var controller = new ProgrammeCheckAdminController(Mock.Of<IProgrammeCheckService>(), siblings.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() },
        };

        var result = await controller.SiblingImport(7, ImportRequest(), CancellationToken.None);

        Assert.Equal(job, Json(Assert.IsType<AcceptedResult>(result).Value).GetProperty("jobId").GetGuid());
    }

    private static JsonElement Json(object? value) =>
        JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
