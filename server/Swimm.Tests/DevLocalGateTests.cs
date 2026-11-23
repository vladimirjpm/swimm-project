using System.Net;
using Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Swimm.API.Security;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Tests;

/// <summary>
/// Локальная админка под utest-admin (docs/plans/test-personas-plan.md, «Локальная админка под
/// utest-admin»): обход и переключатель персонажа живут, только если ВСЕ условия
/// <see cref="DevLocalGate"/> выполнены — Development, локальная база, запрос с loopback.
/// </summary>
public class DevLocalGateTests
{
    private const string LocalCs = "Host=localhost;Port=5445;Database=swimm;Username=swimm";
    private const string ProdCs = "Host=swimhub-db.postgres.database.azure.com;Database=swimm;Username=swimm_rw";

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Swimm.API";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.GroupBy(p => p.Key).Select(g => KeyValuePair.Create(g.Key, (string?)g.Last().Value))).Build();

    // ── Условия процесса ──────────────────────────────────────────────────

    [Fact]
    public void Development_LocalDb_Allowed() =>
        Assert.Null(DevLocalGate.WhyDisabled(new Env("Development"), Config(("ConnectionStrings:DefaultConnection", LocalCs))));

    [Theory]
    [InlineData("Production")]
    [InlineData("ProdAdmin")]
    [InlineData("Staging")]
    public void NotDevelopment_Denied(string env) =>
        Assert.NotNull(DevLocalGate.WhyDisabled(new Env(env), Config(("ConnectionStrings:DefaultConnection", LocalCs))));

    [Theory]
    [InlineData("DefaultConnection")]
    [InlineData("AdminConnection")]
    [InlineData("ReadConnection")]
    [InlineData("MigrationConnection")]
    public void Development_AnyAppConnectionToProd_Denied(string name)
    {
        var cfg = Config(("ConnectionStrings:DefaultConnection", LocalCs), ($"ConnectionStrings:{name}", ProdCs));
        Assert.NotNull(DevLocalGate.WhyDisabled(new Env("Development"), cfg));
    }

    [Fact]
    public void Development_ProdVersionConnectionToProd_StillAllowed()
    {
        // Сверка «локальная отстаёт» читает номер версии с прода — это не «работаем с прод-базой».
        var cfg = Config(("ConnectionStrings:DefaultConnection", LocalCs), ("ConnectionStrings:ProdVersionConnection", ProdCs));
        Assert.Null(DevLocalGate.WhyDisabled(new Env("Development"), cfg));
    }

    [Fact]
    public void Development_NoConnectionString_Denied() =>
        Assert.NotNull(DevLocalGate.WhyDisabled(new Env("Development"), Config()));

    [Theory]
    [InlineData("Host=localhost;Database=swimm", true)]
    [InlineData("Host=127.0.0.1;Port=5445", true)]
    [InlineData("Host=::1", true)]
    [InlineData("Host=localhost:5445,127.0.0.1:5446", true)]
    [InlineData("Host=localhost,swimhub-db.postgres.database.azure.com", false)]
    [InlineData("Host=10.0.0.5", false)]
    [InlineData("Host=localhost.evil.com", false)]
    [InlineData("Database=swimm", false)]          // хост не указан — не считаем локальным
    [InlineData("это не строка подключения", false)]
    public void IsLocalConnectionString(string cs, bool expected) =>
        Assert.Equal(expected, DevLocalGate.IsLocalConnectionString(cs));

    [Fact]
    public void UseDevAdminBypass_DirectCall_OutsideDevelopment_Throws()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection([KeyValuePair.Create("ConnectionStrings:DefaultConnection", (string?)LocalCs)]);
        var app = builder.Build();
        Assert.Throws<InvalidOperationException>(() => app.UseDevAdminBypass());
    }

    // ── Живой конвейер: тот же UseDevAdminBypassIfAllowed, что в Program.cs ──

    private static async Task<WebApplication> StartAsync(string env, string cs, bool flag = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = env });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
        [
            KeyValuePair.Create("ConnectionStrings:DefaultConnection", (string?)cs),
            KeyValuePair.Create("DevAdminBypass", (string?)(flag ? "true" : "false")),
        ]);
        var dbName = $"devgate-{Guid.NewGuid()}";
        builder.Services.AddDbContext<SwimmDbContext>(o => o.UseInMemoryDatabase(dbName));

        var app = builder.Build();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SwimmDbContext>();
            var admin = new AppRole { Name = "Admin" };
            var user = new AppRole { Name = "User" };
            db.AppRoles.AddRange(admin, user);
            var u = new AppUser { Email = TestPersonas.EmailOf(TestPersonas.SiteAdmin), DisplayName = TestPersonas.SiteAdmin, SecurityStamp = "s", IsActive = true };
            db.AppUsers.Add(u);
            await db.SaveChangesAsync();
            db.AppUserRoles.AddRange(new AppUserRole { UserId = u.Id, RoleId = admin.Id }, new AppUserRole { UserId = u.Id, RoleId = user.Id });
            await db.SaveChangesAsync();
        }

        app.UseRouting();
        app.UseDevAdminBypassIfAllowed();
        // Та же проверка, что блок «Блокируем /admin/ для не-админов» в Program.cs.
        app.MapGet("/Admin/Competitions", (HttpContext ctx) =>
            ctx.User.Identity?.IsAuthenticated != true ? Results.Redirect("/auth/login/google")
            : ctx.User.IsInRole("Admin") ? Results.Ok("admin") : Results.StatusCode(403));
        await app.StartAsync();
        return app;
    }

    private static Task<HttpContext> SendAsync(WebApplication app, string path, IPAddress? ip, string? forwardedFor = null) =>
        app.GetTestServer().SendAsync(ctx =>
        {
            ctx.Request.Method = "GET";
            ctx.Request.Path = path;
            ctx.Connection.RemoteIpAddress = ip;
            ctx.Request.Headers.Cookie = $"{DevAdminBypass.PersonaCookie}={TestPersonas.SiteAdmin}";
            if (forwardedFor != null) ctx.Request.Headers["X-Forwarded-For"] = forwardedFor;
        });

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task Development_LocalDb_Loopback_UtestAdminOpensAdmin(string ip)
    {
        await using var app = await StartAsync("Development", LocalCs);
        var admin = await SendAsync(app, "/Admin/Competitions", IPAddress.Parse(ip));
        Assert.Equal(200, admin.Response.StatusCode);
        Assert.Equal(TestPersonas.EmailOf(TestPersonas.SiteAdmin), admin.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value);

        var personas = await SendAsync(app, "/api/dev/personas", IPAddress.Parse(ip));
        Assert.Equal(200, personas.Response.StatusCode);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("ProdAdmin")]
    public async Task NotDevelopment_NoBypass_NoDevEndpoints(string env)
    {
        await using var app = await StartAsync(env, LocalCs);
        Assert.Equal(302, (await SendAsync(app, "/Admin/Competitions", IPAddress.Loopback)).Response.StatusCode);
        Assert.Equal(404, (await SendAsync(app, "/api/dev/persona", IPAddress.Loopback)).Response.StatusCode);
    }

    [Fact]
    public async Task Development_ProdConnectionString_NoBypass()
    {
        await using var app = await StartAsync("Development", ProdCs);
        Assert.Equal(302, (await SendAsync(app, "/Admin/Competitions", IPAddress.Loopback)).Response.StatusCode);
        Assert.Equal(404, (await SendAsync(app, "/api/dev/personas", IPAddress.Loopback)).Response.StatusCode);
    }

    [Fact]
    public async Task Development_FlagOff_NoBypass()
    {
        await using var app = await StartAsync("Development", LocalCs, flag: false);
        Assert.Equal(302, (await SendAsync(app, "/Admin/Competitions", IPAddress.Loopback)).Response.StatusCode);
    }

    [Fact]
    public async Task Development_RemoteAddress_NoBypass_DevEndpoints404()
    {
        await using var app = await StartAsync("Development", LocalCs);
        var remote = IPAddress.Parse("192.168.1.20");
        Assert.Equal(302, (await SendAsync(app, "/Admin/Competitions", remote)).Response.StatusCode);
        Assert.Equal(404, (await SendAsync(app, "/api/dev/persona", remote)).Response.StatusCode);
        Assert.Equal(404, (await SendAsync(app, "/api/dev/personas", null)).Response.StatusCode);
    }

    [Fact]
    public async Task Development_LoopbackButProxied_NoBypass()
    {
        // UseForwardedHeaders в Program.cs доверяет любому источнику: прокси-заголовок = «не локальный».
        await using var app = await StartAsync("Development", LocalCs);
        Assert.Equal(302, (await SendAsync(app, "/Admin/Competitions", IPAddress.Loopback, "127.0.0.1")).Response.StatusCode);
        Assert.Equal(404, (await SendAsync(app, "/api/dev/personas", IPAddress.Loopback, "127.0.0.1")).Response.StatusCode);
    }

    [Fact]
    public void UtestAdmin_IsTestAccount_WithAdminRole()
    {
        var p = TestPersonas.All.Single(x => x.Nick == TestPersonas.SiteAdmin);
        Assert.Equal("utest-admin@swimm.test", p.Email);
        Assert.True(TestAccountRules.IsTestEmail(p.Email));
        Assert.Contains(TestPersonas.RoleAdmin, p.Roles);
        // Ни один другой персонаж Admin не получает — права админа есть ровно у одного.
        Assert.Single(TestPersonas.All, x => x.Roles.Contains(TestPersonas.RoleAdmin));
    }
}
