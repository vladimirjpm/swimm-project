using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.RateLimiting;
using Swimm.API.BackgroundServices;
using Swimm.API.Security;
using Swimm.Application;
using Swimm.Application.Abstractions;
using Swimm.Infrastructure;

using Swimm.API.BackgroundServices;
using Swimm.API.Services;

var builder = WebApplication.CreateBuilder(args);

// appsettings.Local.json — gitignored, для локального переключения на другую БД.
// Перекрывает appsettings.json и appsettings.{Environment}.json.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

builder.Services.AddMemoryCache();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact",
        policy => policy
            .WithOrigins("http://localhost:5173", "http://localhost:5203")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

builder.Services.AddControllers();
builder.Services.AddRazorPages();
builder.Services.AddHostedService<ImportBackgroundService>();

// Rate limiting для чувствительных к перебору auth-эндпоинтов (login/register/forgot/reset).
// Фиксированное окно по IP: 10 запросов в минуту, лишнее — 429.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});
// Antiforgery: header-based (double-submit) для защиты admin-мутаций.
// Клиент читает токен из JS-переменной, генерируемой в _Layout.cshtml, и посылает в этом заголовке.
builder.Services.AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN");

// Authentication: Cookie + (опционально) Google
var googleSection = builder.Configuration.GetSection("Authentication:Google");
var googleClientId = googleSection["ClientId"];
var googleClientSecret = googleSection["ClientSecret"];
var googleEnabled = !string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret);

var authBuilder = builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = googleEnabled
            ? GoogleDefaults.AuthenticationScheme
            : CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Name = "Swimm.Auth";
        options.LoginPath = "/auth/login";
        options.LogoutPath = "/auth/logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        // Ре-валидация сессии: отзыв доступа (IsActive), смена ролей и «выйти со всех»
        // через сверку SecurityStamp. Поход в БД троттлится интервалом внутри валидатора.
        options.Events.OnValidatePrincipal = CookieSecurityStampValidator.ValidateAsync;
    })
    // Транзитная схема для OAuth-рукопожатия. Google пишет промежуточные claims сюда,
    // а не в основную куку → OnValidatePrincipal их не трогает. Короткоживущая.
    .AddCookie(AuthSchemes.External, options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Name = "Swimm.External";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
    });

if (googleEnabled)
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId!;
        options.ClientSecret = googleClientSecret!;
        options.CallbackPath = "/signin-google";
        // Промежуточный вход — в транзитную схему, а не в основную куку.
        options.SignInScheme = AuthSchemes.External;
        options.Scope.Add("profile");
        options.Scope.Add("email");
    });
}

builder.Services.AddSingleton<DbStatusService>();
builder.Services.AddHostedService<DbPingBackgroundService>();

var app = builder.Build();

// Миграции применяются явно — либо через `dotnet ef database update`, либо передав флаг
// `--migrate` при запуске приложения. Авто-миграция при старте отключена: runtime-процесс
// не должен иметь DDL-прав (подготовка к least-privilege DB role, Phase 3).
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<IDbMigrator>().Migrate();
    return;
}

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseRouting();

app.UseRateLimiter();

if (app.Environment.IsDevelopment())
    app.UseCors("AllowReact");

app.UseAuthentication();

// Dev-обход логина в админку: appsettings.Development.json → "DevAdminBypass": true.
// Работает ТОЛЬКО в Development и только если флаг включён явно; в проде ветка мертва.
// Неаутентифицированный запрос получает синтетического пользователя с ролью Admin,
// чтобы можно было работать с /Admin без Google OAuth (например, при вёрстке админки).
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("DevAdminBypass"))
{
    app.Use(async (context, next) =>
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            var identity = new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "0"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "dev-admin"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin"),
            ], authenticationType: "DevAdminBypass");
            context.User = new System.Security.Claims.ClaimsPrincipal(identity);
        }
        await next();
    });
}

app.UseAuthorization();

// Maintenance mode — если MaintenanceMode = true, пускаем только админов
app.Use(async (context, next) =>
{
    var settings = context.RequestServices.GetRequiredService<ISettingsService>();
    if (settings.GetValue("MaintenanceMode", false)
        && !context.Request.Path.StartsWithSegments("/admin")
        && !context.Request.Path.StartsWithSegments("/api/admin")
        && !context.Request.Path.StartsWithSegments("/auth")
        && !context.Request.Path.StartsWithSegments("/signin-google"))
    {
        if (context.User.IsInRole("Admin")) { await next(); return; }

        context.Response.StatusCode = 503;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync("""
            <!DOCTYPE html>
            <html><head><title>Maintenance</title>
            <style>body{font-family:sans-serif;display:flex;justify-content:center;align-items:center;height:100vh;margin:0;background:#1a1a2e;color:#e0e0e0;}
            .box{text-align:center}.box h1{font-size:3rem;margin-bottom:.5rem}.box p{color:#90a4ae;font-size:1.1rem}</style></head>
            <body><div class="box"><h1>🔧</h1><h1>Site is under maintenance</h1><p>We'll be back shortly.</p></div></body></html>
            """);
        return;
    }
    await next();
});

// Блокируем /admin/ для не-админов
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/admin"))
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.Redirect("/auth/login/google?returnUrl=" + Uri.EscapeDataString(context.Request.Path));
            return;
        }
        if (!context.User.IsInRole("Admin"))
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("Forbidden: Admin role required.");
            return;
        }
    }
    await next();
});

app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "home.html", "index.html" }
});
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.File.PhysicalPath;
        if (!string.IsNullOrEmpty(path) && path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.ContentType = "text/html; charset=utf-8";
    }
});

app.MapRazorPages();
app.MapControllers();

// SPA не получает antiforgery-токен из Razor-разметки (_Layout.cshtml), поэтому
// выставляем отдельный эндпоинт. Токен нужен для всех мутаций FavoritesController.
app.MapGet("/api/antiforgery/token", (IAntiforgery af, HttpContext ctx) =>
{
    var tokens = af.GetAndStoreTokens(ctx);
    return Results.Ok(new { token = tokens.RequestToken });
}).RequireAuthorization();

app.MapGet("/api/db-status",
    (DbStatusService s) => Results.Ok(new { available = s.IsAvailable }))
    .AllowAnonymous();

app.Run();
