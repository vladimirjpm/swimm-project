using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;
using Swimm.API.Data;
using Swimm.API.Services;

var builder = WebApplication.CreateBuilder(args);

// Кеш (доступен всему API)
builder.Services.AddMemoryCache();

// Database
builder.Services.AddDbContext<SwimmDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact",
        policy => policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

builder.Services.AddControllers();

// Сервисы
builder.Services.AddSingleton<AdminSettingsService>();
builder.Services.AddScoped<DbSchemaService>();
builder.Services.AddScoped<JsonImportService>();

// Authentication: Cookie + Google
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Name = "Swimm.Auth";
        options.LoginPath = "/auth/login";
        options.LogoutPath = "/auth/logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
    })
    .AddGoogle(options =>
    {
        var googleSection = builder.Configuration.GetSection("Authentication:Google");
        options.ClientId = googleSection["ClientId"]!;
        options.ClientSecret = googleSection["ClientSecret"]!;
        options.CallbackPath = "/signin-google";
        options.SaveTokens = false;

        // Запросить профиль и email
        options.Scope.Add("profile");
        options.Scope.Add("email");
    });

var app = builder.Build();

// Автоматическое применение миграций при старте
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SwimmDbContext>();
    db.Database.Migrate();

    // Нормализация схемы и данных (идемпотентно)
    db.Database.ExecuteSqlRaw(SwimmDbContext.NormalizeResultsSql);
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseRouting();

if (app.Environment.IsDevelopment())
{
    app.UseCors("AllowReact");
}

app.UseAuthentication();
app.UseAuthorization();

// Maintenance mode — если MaintenanceMode = true, пускаем только админов
app.Use(async (context, next) =>
{
    var settings = context.RequestServices.GetRequiredService<AdminSettingsService>();
    var maintenance = settings.GetValue("MaintenanceMode", false);

    if (maintenance
        && !context.Request.Path.StartsWithSegments("/admin")
        && !context.Request.Path.StartsWithSegments("/api/admin")
        && !context.Request.Path.StartsWithSegments("/auth")
        && !context.Request.Path.StartsWithSegments("/signin-google"))
    {
        if (context.User.IsInRole("Admin"))
        {
            await next();
            return;
        }

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
        {
            ctx.Context.Response.ContentType = "text/html; charset=utf-8";
        }
    }
});

app.MapControllers();
app.Run();
