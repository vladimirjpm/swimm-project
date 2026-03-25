using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Swimm.API.Data;
using Swimm.API.Services;

var builder = WebApplication.CreateBuilder(args);

// ──── Сервисы ────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact",
        policy => policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

builder.Services.AddMemoryCache();

builder.Services.AddDbContext<SwimmDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
})
.AddCookie()
.AddGoogle(options =>
{
    var section = builder.Configuration.GetSection("Authentication:Google");
    options.ClientId = section["ClientId"] ?? "";
    options.ClientSecret = section["ClientSecret"] ?? "";
    options.ClaimActions.MapJsonKey("picture", "picture");
});

builder.Services.AddSingleton<AdminSettingsService>();
builder.Services.AddScoped<DbSchemaService>();
builder.Services.AddControllers();

var app = builder.Build();

// ──── Авто-миграция в Development ────
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SwimmDbContext>();

    // Baseline: если таблицы уже созданы до появления EF-миграций,
    // помечаем InitialCreate как применённую, чтобы Migrate() не пересоздавал их.
    db.Database.ExecuteSqlRaw("""
IF OBJECT_ID(N'dbo.Roles', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[__EFMigrationsHistory](
            [MigrationId] NVARCHAR(150) NOT NULL,
            [ProductVersion] NVARCHAR(32) NOT NULL,
            CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory]
                   WHERE [MigrationId] = N'20260325160216_InitialCreate')
    BEGIN
        DELETE FROM [dbo].[__EFMigrationsHistory];

        INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
        VALUES (N'20260325160216_InitialCreate', N'8.0.12');
    END;
END;
""");

    db.Database.Migrate();

    // Гарантируем наличие начальных ролей (idempotent)
    db.Database.ExecuteSqlRaw("""
IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Name] = N'Admin')
    INSERT INTO [dbo].[Roles] ([Name]) VALUES (N'Admin');
IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Name] = N'User')
    INSERT INTO [dbo].[Roles] ([Name]) VALUES (N'User');
""");
}

// ──── Middleware pipeline ────
app.UseRouting();

if (app.Environment.IsDevelopment())
    app.UseCors("AllowReact");

app.UseAuthentication();
app.UseAuthorization();

// Maintenance-режим (проверяем настройку)
app.Use(async (ctx, next) =>
{
    var settings = ctx.RequestServices.GetRequiredService<AdminSettingsService>();
    if (settings.GetValue<bool>("MaintenanceMode", false)
        && !ctx.Request.Path.StartsWithSegments("/api/auth")
        && !ctx.Request.Path.StartsWithSegments("/api/admin"))
    {
        ctx.Response.StatusCode = 503;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync("{\"error\":\"Site is under maintenance\"}");
        return;
    }
    await next();
});

// Защита /admin/ — только для авторизованных админов
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/admin"))
    {
        if (ctx.User.Identity?.IsAuthenticated != true || !ctx.User.IsInRole("Admin"))
        {
            ctx.Response.StatusCode = 403;
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync("Forbidden: Admin access required");
            return;
        }
    }
    await next();
});

// Статические файлы
var defaultFiles = new DefaultFilesOptions();
defaultFiles.DefaultFileNames.Clear();
defaultFiles.DefaultFileNames.Add("home.html");
defaultFiles.DefaultFileNames.Add("index.html");
app.UseDefaultFiles(defaultFiles);

var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".js"] = "application/javascript; charset=utf-8";
provider.Mappings[".css"] = "text/css; charset=utf-8";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = provider });

app.MapControllers();
app.Run();
