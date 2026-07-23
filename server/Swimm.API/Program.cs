using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.RateLimiting;
using Swimm.API.BackgroundServices;
using Swimm.API.Security;
using Swimm.API.Services;
using Swimm.Application;
using Swimm.Application.Abstractions;
using Swimm.Infrastructure;
using Swimm.Parsing;

var builder = WebApplication.CreateBuilder(args);

// appsettings.Local.json — gitignored, для локального переключения на другую БД.
// Перекрывает appsettings.json и appsettings.{Environment}.json.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

builder.Services.AddMemoryCache();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
// Парсеры протоколов + IResultSourceProvider (Swimm.Parsing) — потребитель появится
// на этапе 1.3 (страница Import: PDF → парс → превью → импорт).
builder.Services.AddParsing();

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

// Аудит админки (фаза 7.4): actor берётся из HTTP-контекста через ICurrentActor.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentActor, Swimm.API.Services.HttpCurrentActor>();
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
    // Реакции (❤/🎉): щедрее auth — это обычные клики, но защищаемся от бот-накрутки.
    // Ключ — userId (эндпоинты только для залогиненных), IP — фоллбек до авторизации.
    options.AddPolicy("reactions", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                          ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
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
builder.Services.AddHostedService<Swimm.API.BackgroundServices.CompetitionDiscoveryBackgroundService>();
builder.Services.AddHostedService<Swimm.API.BackgroundServices.LogligSuggestionVerificationBackgroundService>();
builder.Services.AddHostedService<Swimm.API.BackgroundServices.LogligBatchBackgroundService>();

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

// Одноразовый сид рекордов/нормативов из легаси JS-файлов клиента:
//   dotnet run -- --seed-records <путь к client/public/data> [--force]
// --force заменяет содержимое таблиц целиком (иначе непустые таблицы — отказ).
if (args.Contains("--seed-records"))
{
    var dirIndex = Array.IndexOf(args, "--seed-records") + 1;
    if (dirIndex >= args.Length || args[dirIndex].StartsWith("--"))
    {
        Console.Error.WriteLine("Usage: dotnet run -- --seed-records <data-dir> [--force]");
        Environment.Exit(1);
        return;
    }

    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<IRecordsSeeder>();
    var seedLog = await seeder.SeedAsync(args[dirIndex], args.Contains("--force"));
    foreach (var line in seedLog)
        Console.WriteLine(line);
    return;
}

// Одноразовый сид тренировок «Дельфин-мастерс» (соревнования не трогаем — они уже в БД):
//   dotnet run -- --seed-dolphin-training <json> <canon.csv> --group <hubGroupId> [--force]
if (args.Contains("--seed-dolphin-training"))
{
    string ArgAfter(string flag, string what)
    {
        var i = Array.IndexOf(args, flag) + 1;
        if (i <= 0 || i >= args.Length || args[i].StartsWith("--"))
        {
            Console.Error.WriteLine(
                "Usage: dotnet run -- --seed-dolphin-training <json> <canon.csv> --group <hubGroupId> [--force]");
            Environment.Exit(1);
        }
        return args[i];
    }

    var jsonPath = args[Array.IndexOf(args, "--seed-dolphin-training") + 1];
    var csvPath = args[Array.IndexOf(args, "--seed-dolphin-training") + 2];
    var groupId = int.Parse(ArgAfter("--group", "hubGroupId"));

    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<IDolphinTrainingSeeder>();
    var seedLog = await seeder.SeedAsync(jsonPath, csvPath, groupId, args.Contains("--force"));
    foreach (var line in seedLog)
        Console.WriteLine(line);
    return;
}

// Разовая синхронизация «входящих» автозабора isr.org.il (фаза 6) и выход:
//   dotnet run -- --discovery-sync
if (args.Contains("--discovery-sync"))
{
    using var scope = app.Services.CreateScope();
    var discovery = scope.ServiceProvider.GetRequiredService<ICompetitionDiscoveryService>();
    var sync = await discovery.SyncAsync();
    Console.WriteLine($"Discovery: на сайте {sync.TotalOnSite}, добавлено {sync.Added}, обновлено {sync.Updated}");
    return;
}

// Склейка пловцов-дублей (см. docs/tasks/dedup-report.md):
//   dotnet run -- --merge-swimmers <pairs.csv> [--apply]
// CSV: строки "canonicalId,duplicateId" (пустые и # -комментарии пропускаются).
// Без --apply — dry-run: печатает план, БД не меняется.
if (args.Contains("--merge-swimmers"))
{
    var csvIndex = Array.IndexOf(args, "--merge-swimmers") + 1;
    if (csvIndex >= args.Length || args[csvIndex].StartsWith("--"))
    {
        Console.Error.WriteLine("Usage: dotnet run -- --merge-swimmers <pairs.csv> [--apply]");
        Environment.Exit(1);
        return;
    }

    var mergePairs = new List<Swimm.Application.Dtos.SwimmerMergePair>();
    foreach (var line in File.ReadLines(args[csvIndex]))
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
        var parts = trimmed.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var canon) || !int.TryParse(parts[1], out var dup))
        {
            Console.Error.WriteLine($"Некорректная строка CSV: «{line}» (ожидается canonicalId,duplicateId)");
            Environment.Exit(1);
            return;
        }
        mergePairs.Add(new Swimm.Application.Dtos.SwimmerMergePair(canon, dup));
    }

    using var scope = app.Services.CreateScope();
    var merge = scope.ServiceProvider.GetRequiredService<ISwimmerMergeService>();
    var mergeReport = await merge.MergeAsync(mergePairs, dryRun: !args.Contains("--apply"));
    Console.WriteLine(mergeReport.DryRun
        ? "=== DRY-RUN: план склейки, БД не изменена (добавь --apply для применения) ==="
        : "=== ПРИМЕНЕНО ===");
    foreach (var p in mergeReport.Pairs)
    {
        Console.WriteLine($"[{p.Status}] canonical {p.CanonicalId} ← duplicate {p.DuplicateId}");
        foreach (var a in p.Actions) Console.WriteLine($"    {a}");
        foreach (var c in p.Conflicts) Console.WriteLine($"    !! {c}");
    }
    return;
}

// Разовый бэкфилл структурного состава эстафет (RelayMembers) для данных,
// импортированных до появления структурных ног:
//   dotnet run -- --backfill-relay-members [--apply]
// Без --apply — dry-run: печатает отчёт, БД не меняется. Идемпотентно.
if (args.Contains("--backfill-relay-members"))
{
    using var scope = app.Services.CreateScope();
    var backfill = scope.ServiceProvider.GetRequiredService<IRelayMemberBackfillService>();
    var rep = await backfill.BackfillAsync(apply: args.Contains("--apply"));
    Console.WriteLine(rep.Applied
        ? "=== ПРИМЕНЕНО: RelayMembers бэкфилл ==="
        : "=== DRY-RUN: RelayMembers бэкфилл, БД не изменена (добавь --apply) ===");
    Console.WriteLine($"Эстафет всего (без состава): {rep.RelaysTotal}");
    Console.WriteLine($"  уже с составом (пропущены):  {rep.RelaysAlreadyPopulated}");
    Console.WriteLine($"  залинковано (>=1 нога):      {rep.RelaysLinked}");
    Console.WriteLine($"Ног привязано:  {rep.LegsLinked}");
    Console.WriteLine($"Ног не сопоставлено: {rep.LegsUnmatched}");
    foreach (var s in rep.UnmatchedSamples) Console.WriteLine($"    ? {s}");
    return;
}

// Разовый бэкфилл Competition.OrgCompId по Discovery-строкам (для соревнований, импортированных
// до того, как импорт научился штамповать OrgCompId; кросс-линк Competitions↔Discovery для них пуст):
//   dotnet run -- --backfill-discovery-orgcompid [--apply]
// Без --apply — dry-run: печатает mapping-таблицу, БД не меняется. Идемпотентно.
if (args.Contains("--backfill-discovery-orgcompid"))
{
    using var scope = app.Services.CreateScope();
    var discovery = scope.ServiceProvider.GetRequiredService<ICompetitionDiscoveryService>();
    var apply = args.Contains("--apply");
    var rows = await discovery.BackfillImportedOrgCompIdsAsync(apply: apply);

    Console.WriteLine(apply
        ? "=== ПРИМЕНЕНО ==="
        : "=== DRY-RUN: бэкфилл Discovery→OrgCompId, БД не изменена (добавь --apply) ===");
    foreach (var r in rows)
    {
        Console.WriteLine(
            $"[{r.Action}] compID {r.OrgCompId} → comp #{r.CompetitionId} «{r.CompetitionName}»  (discovered: «{r.DiscoveredName}»)");
    }

    var wouldLink = rows.Count(r => r.Action is "WouldLink");
    var linked = rows.Count(r => r.Action is "Linked");
    var already = rows.Count(r => r.Action is "AlreadyLinked");
    var takenByOther = rows.Count(r => r.Action is "TakenByOther");
    Console.WriteLine($"Итого: would-link {wouldLink}, linked {linked}, already {already}, taken-by-other {takenByOther}");
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

// Публичная конфигурация клиента (не секреты!). resultsLoadMode: full/paged — принудительный
// режим загрузки результатов (?loadMode= игнорируется), client — режим выбирает клиент.
// См. ResultsLoadMode в Admin/Settings и results-load-mode.ts на клиенте.
app.MapGet("/api/client-config",
    (ISettingsService settings) => Results.Ok(new
    {
        resultsLoadMode = settings.GetValue("ResultsLoadMode", "client")
    }))
    .AllowAnonymous();

app.Run();
