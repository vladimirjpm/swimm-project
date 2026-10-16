using System.IO.Compression;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Swimm.API.BackgroundServices;
using Swimm.API.Hosting;
using Swimm.API.Security;
using Swimm.API.Services;
using Swimm.Application;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using System.Reflection;
using Swimm.Infrastructure;
using Swimm.Parsing;

var builder = WebApplication.CreateBuilder(args);

// appsettings.Local.json — gitignored, для локального переключения на другую БД.
// Перекрывает appsettings.json и appsettings.{Environment}.json.
// ⚠ ТОЛЬКО в Development: провайдер добавляется ПОСЛЕ переменных окружения (их подключил
// CreateBuilder), то есть в проде этот файл перебил бы строки подключения из App Service.
// docs/plans/azure-deploy-plan.md Б7.
if (builder.Environment.IsDevelopment())
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// ProdAdmin (решение В17): локальная админка против прод-БД. Прод-пароли и Google-секрет —
// в gitignored-файле, по той же схеме, что Local.json в разработке: процесс крутится на машине
// Влада, переменных App Service тут нет, перебивать нечего. Образец — *.example рядом.
if (builder.Environment.IsProdAdmin())
    builder.Configuration.AddJsonFile("appsettings.ProdAdmin.Local.json", optional: true, reloadOnChange: false);

// Форма процесса (В17): на проде админка и фоновые джобы выключены — appsettings.Production.json.
var adminEnabled = HostingMode.AdminEnabled(builder.Configuration);
var backgroundJobsEnabled = HostingMode.BackgroundJobsEnabled(builder.Configuration);

// Сжатие ответов. Замер 2026-08-25: справочники, которые тянет КАЖДАЯ страница
// (records + normative-standards), весят 1.9 МБ несжатыми и 118 КБ в gzip — 94%.
// ⚠ Только application/json, и это НЕ экономия ради экономии: HTML админки отдаётся с
// antiforgery-токеном, а сжатый ответ с секретом плюс подконтрольный злоумышленнику ввод —
// это BREACH. Публичный JSON секретов не несёт, поэтому его жмём и по HTTPS тоже.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = new[] { "application/json" };
});
// Optimal, а не Fastest: у brotli Fastest — это quality 1, он на справочниках проигрывает
// даже gzip. Замер на 971 КБ: Fastest 83 КБ / 3 мс, Optimal 60 КБ / 5 мс. Два миллисекунды
// за четверть трафика — выгодный размен, тем более что справочники живут в кэше браузера
// по 5 минут и жмутся редко.
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Optimal);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Optimal);

// ── За обратным прокси (Azure App Service) ──────────────────────────────────────────────
// App Service терминирует TLS и проксирует внутрь по HTTP. Без этого Request.Scheme == http,
// а значит: Google собирает redirect_uri на http:// и вход ломается; ссылки подтверждения
// почты и сброса пароля уезжают битыми (AuthController.BaseUrl); rate-limiter партиционирует
// по RemoteIpAddress и схлопывается в один общий лимит на всех клиентов.
// KnownIPNetworks/KnownProxies чистим: фронтенд App Service не в приватной сети, и с непустым
// списком заголовки молча игнорируются. docs/plans/azure-deploy-plan.md Б5.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// ── Data Protection ─────────────────────────────────────────────────────────────────────
// Этим кольцом ключей шифруются auth-кука (Swimm.Auth) и antiforgery-токены. По умолчанию
// оно живёт в файловой системе контейнера: на App Service это значит «новые ключи после
// каждого деплоя и рестарта» → у всех слетают сессии, а мутации в админке начинают падать
// на antiforgery. Явное имя приложения обязательно: без него ключи не переживают смену пути.
// Каталог задаётся DataProtection:KeysDir (в проде — персистентный /home/...); пусто =
// поведение по умолчанию, для дева это нормально. docs/plans/azure-deploy-plan.md Б6.
var dpBuilder = builder.Services.AddDataProtection().SetApplicationName("Swimm");
var dpKeysDir = builder.Configuration["DataProtection:KeysDir"];
if (!string.IsNullOrWhiteSpace(dpKeysDir))
    dpBuilder.PersistKeysToFileSystem(new DirectoryInfo(dpKeysDir));

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

builder.Services.AddProblemDetails();
builder.Services.AddControllers();
builder.Services.AddRazorPages();
// Красная полоса админки в ProdAdmin: куда подключены и с какой ветки (читается раз на старте).
builder.Services.AddSingleton(_ => ProdAdminGuard.Describe(builder.Configuration, builder.Environment));

// Аудит админки (фаза 7.4): actor берётся из HTTP-контекста через ICurrentActor.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentActor, Swimm.API.Services.HttpCurrentActor>();

// Фоновые джобы — только где есть админка, которая их ставит и смотрит (В17). На проде из
// фоновых остаются пинг БД (ниже) и мост сброса кэша (AddInfrastructure): от флага они не зависят.
if (backgroundJobsEnabled)
{
    builder.Services.AddHostedService<ImportBackgroundService>();

    // Прогон рекордов по странам (11.1.2) — 470 файлов, час-два; в HTTP-запрос не пролезет.
    builder.Services.AddHostedService<Swimm.API.BackgroundServices.RecordCountryRunBackgroundService>();

    builder.Services.AddHostedService<Swimm.API.BackgroundServices.CompetitionDiscoveryBackgroundService>();
    // Публичная кнопка «предложить loglig-id» на проде остаётся: предложения копятся в БД
    // (Suggested) и проверяются этим джобом, когда запущена локальная админка (ProdAdmin).
    builder.Services.AddHostedService<Swimm.API.BackgroundServices.LogligSuggestionVerificationBackgroundService>();
    builder.Services.AddHostedService<Swimm.API.BackgroundServices.LogligBatchBackgroundService>();
}

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
    // Медиа-мутации (добавление ссылок, заявки на публикацию): защита от массового
    // замусоривания таблицы и модерационной ленты. Ключ — userId ([Authorize]-эндпоинты).
    options.AddPolicy("media", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                          ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    // Мутации групп (Ш3.0, 28.09.2026): вступление, RSVP, состав, уровни, планы дорожек, медиа
    // группы. Вешается на весь контроллер группы, поэтому чтение (GET/HEAD) пропускаем без
    // лимита: иначе поиск пловца «по мере набора» упирался бы в него. Ключ — userId.
    options.AddPolicy(HubGroupQuotaRules.RateLimitPolicy, httpContext =>
        HttpMethods.IsGet(httpContext.Request.Method) || HttpMethods.IsHead(httpContext.Request.Method)
            ? RateLimitPartition.GetNoLimiter("read")
            : RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                              ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = HubGroupQuotaRules.RateLimitPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
    // Жалобы «Report» на медиа (Р62): свободный текст «Other» — поэтому строже медиа-мутаций.
    // Ключ — userId ([Authorize]-эндпоинт).
    options.AddPolicy(MediaReportRules.RateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                          ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = MediaReportRules.RateLimitPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    // Тело отказа — { error }: так его читают клиентские обработчики (saveResultFrom, RSVP);
    // вход (login-modal) смотрит только на статус 429, тело ему не мешает.
    options.OnRejected = async (context, ct) =>
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = HubGroupQuotaRules.RateLimitedError }, ct);
});
// Antiforgery: header-based (double-submit) для защиты admin-мутаций.
// Клиент читает токен из JS-переменной, генерируемой в _Layout.cshtml, и посылает в этом заголовке.
// В проде кука уходит только по HTTPS; в деве (и в ProdAdmin — та же машина Влада) сайт живёт
// на http://localhost, поэтому SameAsRequest — иначе локальный вход перестал бы работать.
var secureCookiePolicy = builder.Environment.IsLocalMachine()
    ? CookieSecurePolicy.SameAsRequest
    : CookieSecurePolicy.Always;

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.SecurePolicy = secureCookiePolicy;
});

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
        options.Cookie.SecurePolicy = secureCookiePolicy;
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
        options.Cookie.SecurePolicy = secureCookiePolicy;
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
        // Признак «email подтверждён» — без него новый пользователь Google не войдёт (GoogleClaims).
        GoogleClaims.Configure(options);
    });
}

builder.Services.AddSingleton<DbStatusService>();
builder.Services.AddHostedService<DbPingBackgroundService>();

// Разработка: не отстаёт ли локальная копия от прода (В17). Молчит без ProdVersionConnection.
if (builder.Environment.IsDevelopment())
    builder.Services.AddHostedService<DataSyncCheckBackgroundService>();

var app = builder.Build();

// Консольные команды (--migrate, --repull, --check-data, …) — Cli/CliCommands.cs: выполнить одну и выйти.
if (await Swimm.API.Cli.CliCommands.TryRunAsync(app, args)) return;

// ProdAdmin пишет в прод-БД — стартовать можно только с кодом, совпадающим со схемой прода,
// и только против настоящей прод-базы. CLI (--migrate) выше этой проверки намеренно.
if (app.Environment.IsProdAdmin())
    await ProdAdminGuard.EnsureSafeToStartAsync(app);

// ПЕРВЫМ в конвейере: остальные middleware (и OAuth, и rate-limiter) должны видеть уже
// исправленные Scheme и RemoteIpAddress. docs/plans/azure-deploy-plan.md Б5.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    // Без этого в Production при исключении отдавалась пустая страница: developer exception
    // page выключена, а своего обработчика не было. Для /api/* — ProblemDetails (JSON),
    // для остальных — минимальная страница с кодом.
    app.UseExceptionHandler("/error");
    app.UseStatusCodePagesWithReExecute("/error", "?code={0}");
}

// ProdAdmin — localhost по http, как разработка: HSTS и редирект на https ему не нужны.
if (!app.Environment.IsLocalMachine())
{
    app.UseHsts();
    // /healthz — мимо HTTPS-редиректа: health-check App Service считает инстанс здоровым
    // только на 2xx, а 307 сочтёт отказом и начнёт перезапускать живой процесс.
    app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/healthz"),
        branch => branch.UseHttpsRedirection());

    // www.swimhub.co.il и swimhub.azurewebsites.net → swimhub.co.il (301): у сайта один адрес — в
    // выдаче, в canonical, в куке входа и в redirect URI Google. /healthz, /readyz, /version отвечают
    // на любом хосте — их ждут health check App Service и проверка деплоя (SeoPages.CanonicalRedirect).
    var seoBaseUrl = app.Configuration[SeoPages.BaseUrlKey] ?? SeoPages.DefaultBaseUrl;
    app.Use(async (context, next) =>
    {
        var target = SeoPages.CanonicalRedirect(context.Request.Host.Host, context.Request.Path.Value ?? "",
            $"{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}", seoBaseUrl);
        if (target is null) { await next(); return; }
        context.Response.Redirect(target, permanent: true);
    });
}

// Админка выключена (прод, В17): её пути — 404, как будто их нет. Раньше аутентификации, чтобы
// аноним не получал редирект на вход в несуществующую админку, а вошедший — 403 вместо 404.
// Страницу 404 рисует UseStatusCodePagesWithReExecute выше.
if (!adminEnabled)
{
    app.Use(async (context, next) =>
    {
        if (HostingMode.IsAdminPath(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await next();
    });
}

// До статики и эндпоинтов: иначе middleware не увидит ответ, который надо сжать.
app.UseResponseCompression();

app.UseRouting();

app.UseRateLimiter();

if (app.Environment.IsDevelopment())
    app.UseCors("AllowReact");

app.UseAuthentication();

// Dev-обход логина в админку: appsettings.Development.json → "DevAdminBypass": true
// (кем именно входим — "DevAdminBypassEmail", по умолчанию первый активный админ).
// Работает ТОЛЬКО в Development и только если флаг включён явно; в проде ветка мертва.
// Устройство и почему личность берётся из БД — в Security/DevAdminBypass.cs.
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("DevAdminBypass"))
    app.UseDevAdminBypass();

app.UseAuthorization();

// Maintenance mode — если MaintenanceMode = true, пускаем только админов
app.Use(async (context, next) =>
{
    var settings = context.RequestServices.GetRequiredService<ISettingsService>();
    if (settings.GetValue("MaintenanceMode", false)
        && !context.Request.Path.StartsWithSegments("/admin")
        && !context.Request.Path.StartsWithSegments("/api/admin")
        && !context.Request.Path.StartsWithSegments("/auth")
        && !context.Request.Path.StartsWithSegments("/signin-google")
        // Служебные пути мимо заглушки: иначе health-check App Service получит 503 и
        // перезапустит инстанс ровно тогда, когда мы сами включили режим обслуживания.
        && !context.Request.Path.StartsWithSegments("/healthz")
        && !context.Request.Path.StartsWithSegments("/readyz")
        && !context.Request.Path.StartsWithSegments("/version")
        && !context.Request.Path.StartsWithSegments("/error"))
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

// Чистые URL → физический html в wwwroot (контракт синхронен с client/src/utils/routes.ts).
// Меняем ТОЛЬКО Request.Path, поэтому UseStaticFiles ниже отдаёт нужный файл, а в адресной
// строке браузера остаётся чистый путь; клиент читает идентичность из location.pathname.
// Query-строка сохраняется автоматически (её не трогаем).
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;

    // Пропускаем API/служебные ветки и любые запросы к реальным файлам (есть расширение).
    var lastSeg = path.AsSpan(path.LastIndexOf('/') + 1);
    var hasExtension = lastSeg.Contains('.');
    if (!hasExtension
        && !path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWith("/auth", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
    {
        var rewrite = ResolveCleanUrl(path);
        if (rewrite is not null)
        {
            // Страница пловца/клуба/соревнования — с серверными title/description/canonical
            // (A10). Не наша страница или клиента нет в wwwroot — отдаём файл как раньше.
            if (await SeoPages.TryServeAsync(context, path, rewrite))
                return;
            context.Request.Path = rewrite;
        }
    }

    await next();

    // Локальная функция: чистый путь → html-файл (null = не наш маршрут).
    static string? ResolveCleanUrl(string path)
    {
        // Нормализуем завершающий слэш (кроме корня).
        var p = path.Length > 1 ? path.TrimEnd('/') : path;
        var seg = p.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return seg.Length switch
        {
            0 => null, // корень отдаёт UseDefaultFiles (home.html)
            1 => seg[0] switch
            {
                "results" => "/results_main.html",
                "competitions" => "/competitions.html",
                "groups" => "/groups.html",
                "swimmers" => "/results_main.html", // /swimmers без id — пусть падает штатно
                "clubs" => "/results_main.html",     // /clubs без id — пусть падает штатно
                "my-media" => "/media.html",
                "my-favorites" => "/my-favorites.html", // своё избранное и пометка «семья»
                "about" => "/about.html",
                "privacy" => "/help.html",              // политика приватности — справка с ключом privacy
                "terms" => "/help.html",                // условия пользования — справка с ключом terms
                "season-best" => "/season-best.html",   // весь фильтр в query
                "h2h" => "/h2h.html",                   // сравнение двух пловцов, пара в query
                "records" => "/records.html",           // рейтинг рекордов стран, дисциплина в query
                _ => null,
            },
            >= 2 => seg[0] switch
            {
                "records" when seg[1] == "compare" => "/records-compare.html", // сравнение двух стран
                "competitions" => "/results_main.html",              // /competitions/{id}
                "swimmers" => "/swimmer.html",                       // /swimmers/{id}
                "clubs" => "/club.html",                             // /clubs/{id}
                "help" => "/help.html",                              // /help/{key} — справка из src/content/help
                "groups" when seg.Length >= 3 && seg[2] == "results"
                    => "/results_main.html",                         // /groups/{slug}/results
                "groups" => "/groups.html",                          // /groups/{slug}
                _ => null,
            },
            _ => null,
        };
    }
});

app.UseDefaultFiles(new DefaultFilesOptions
{
    // Порядок значим. В ПРОДЕ publish кладёт сюда клиента, и `/` отдаёт его витрину.
    // ЛОКАЛЬНО клиента в wwwroot нет (он живёт на Vite :5173), и `/` откатывается на
    // admin-home.html — серверную страницу входа в админку. Один и тот же адрес показывает
    // ту сторону, которая в этом окружении вообще есть. Без админки (прод, В17) её страницы
    // входа в списке нет: потеряйся home.html в сборке — `/` даст 404, а не вход в админку.
    DefaultFileNames = adminEnabled
        ? new List<string> { "home.html", "admin-home.html", "index.html" }
        : new List<string> { "home.html", "index.html" }
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

// Razor Pages в проекте — только админка (Pages/Admin). Без неё не маппим вовсе: 404 даёт и
// middleware выше, это вторая линия.
if (adminEnabled)
    app.MapRazorPages();
app.MapControllers();

// SPA не получает antiforgery-токен из Razor-разметки (_Layout.cshtml), поэтому
// выставляем отдельный эндпоинт. Токен нужен для всех мутаций FavoritesController.
app.MapGet("/api/antiforgery/token", (IAntiforgery af, HttpContext ctx) =>
{
    var tokens = af.GetAndStoreTokens(ctx);
    return Results.Ok(new { token = tokens.RequestToken });
}).RequireAuthorization();

// sitemap.xml (A10 шаг 3): соревнования, клубы, пловцы — без скрытых из поиска и пловцов групп.
// Ссылка на него — в robots.txt. Кэш списка — в репозитории, ответ браузеру — на час.
app.MapGet("/sitemap.xml", async (ISeoRepository seo, IConfiguration config, HttpContext ctx) =>
{
    var paths = SeoPages.StaticSitemapPaths.Concat(await seo.GetSitemapPathsAsync(ctx.RequestAborted));
    ctx.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.Text(SeoPages.BuildSitemap(paths, config[SeoPages.BaseUrlKey] ?? SeoPages.DefaultBaseUrl),
        "application/xml; charset=utf-8");
}).AllowAnonymous();

app.MapGet("/api/db-status",
    (DbStatusService s) => Results.Ok(new { available = s.IsAvailable }))
    .AllowAnonymous();
// ── Служебные эндпоинты ─────────────────────────────────────────────────────────────────
// /healthz — liveness для health-check App Service. Намеренно НЕ трогает БД: иначе просадка
// базы приводила бы к перезапуску веб-инстанса, который сам по себе жив.
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

// /readyz — readiness для алертов и ручной диагностики. Статус БД уже посчитан фоновым
// пингом (DbPingBackgroundService), поэтому проба бесплатная.
app.MapGet("/readyz", (DbStatusService s) => s.IsAvailable
        ? Results.Ok(new { status = "ready", database = "up" })
        : Results.Json(new { status = "degraded", database = "down" }, statusCode: StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous();

// /version — что именно крутится в проде. Значение проставляется на сборке
// (-p:InformationalVersion=<sha>), локально — версия сборки.
app.MapGet("/version", () => Results.Ok(new
{
    version = Assembly.GetEntryAssembly()?
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
    environment = app.Environment.EnvironmentName,
})).AllowAnonymous();

// /error — единая точка для UseExceptionHandler и UseStatusCodePagesWithReExecute (Production).
// До этого в проде необработанное исключение отдавало пользователю пустую страницу.
app.Map("/error", (HttpContext ctx) =>
{
    var handled = ctx.Features.Get<IExceptionHandlerPathFeature>();
    var status = handled is not null
        ? StatusCodes.Status500InternalServerError
        : int.TryParse(ctx.Request.Query["code"], out var c) && c is >= 400 and < 600
            ? c
            : StatusCodes.Status500InternalServerError;

    var originalPath = handled?.Path
        ?? ctx.Features.Get<IStatusCodeReExecuteFeature>()?.OriginalPath
        ?? string.Empty;

    // Клиенту API нужен машиночитаемый ответ, человеку — страница.
    if (originalPath.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        return Results.Problem(statusCode: status, title: status == 404 ? "Not found" : "Request failed");

    var title = status == 404 ? "Page not found" : "Something went wrong";
    return Results.Content($$"""
        <!DOCTYPE html>
        <html lang="en"><head><meta charset="utf-8"><title>{{title}}</title>
        <style>body{font-family:system-ui,sans-serif;display:flex;justify-content:center;align-items:center;
        height:100vh;margin:0;background:#0d1418;color:#e3edf1}.b{text-align:center}.b h1{font-size:3rem;margin:0 0 .5rem}
        .b p{color:#9aacb6;margin:0 0 1.5rem}.b a{color:#4cbfc9}</style></head>
        <body><div class="b"><h1>{{status}}</h1><p>{{title}}.</p><a href="/">Back to home</a></div></body></html>
        """, "text/html; charset=utf-8", statusCode: status);
}).AllowAnonymous();


// Публичная конфигурация клиента (не секреты!). resultsLoadMode: full/paged — принудительный
// режим загрузки результатов (?loadMode= игнорируется), client — режим выбирает клиент.
// См. ResultsLoadMode в Admin/Settings и results-load-mode.ts на клиенте.
//
// recordAgeAxis: по какой оси сверять заплывы со справочником рекордов (calendar/season,
// docs/data-integrity.md §13). Клиенту она нужна затем же, зачем серверу: бейдж рекорда в
// строке результата обязан совпадать с карточкой «New records», а её считает сервер.
//
// hubGroupCreationPolicy (admin/coach/any): кто может создавать группы. Нужна ГОСТЮ на
// /groups — подсказка «войдите и создайте группу» не должна обещать невозможного, когда
// создание закрыто. Вошедшему сервер и так отдаёт create-eligibility с причиной отказа.
//
// favoritesLimits: сколько пловцов и клубов можно держать в избранном, по типу избранного
// (ключи — те же target_type, что в /api/me/favorites). Клиенту — чтобы сердечко на пределе
// гасло с подсказкой ДО клика, а не отказом после; подсказка та же, что в тексте отказа 422
// (FavoritesRules.FullHint), второй копии текста на клиенте нет.
//
// familyLimit: сколько избранных можно пометить «семьёй» (FavoritesRules.MaxFamily) и текст
// отказа — страница My favorites гасит кнопку Family на пределе до клика.
app.MapGet("/api/client-config",
    async (ISettingsService settings, IDebugOptionsService debug) => Results.Ok(new
    {
        resultsLoadMode = settings.GetValue("ResultsLoadMode", "client"),
        recordAgeAxis = RecordAgeAxisSetting.From(settings) == RecordAgeAxis.Season
            ? "season"
            : "calendar",
        hubGroupCreationPolicy = settings.GetValue(HubGroupCreationRules.PolicyKey, HubGroupCreationRules.DefaultPolicy),
        favoritesLimits = new[] { FavoritesRules.TargetSwimmer, FavoritesRules.TargetClub }
            .ToDictionary(t => t, t =>
            {
                var max = FavoritesRules.LimitFor(settings, t);
                return new { max, fullHint = FavoritesRules.FullHint(t, max) };
            }),
        familyLimit = new { max = FavoritesRules.MaxFamily, fullHint = FavoritesRules.FamilyFullHint },
        // Отладочные подробности витрин: только ДЕЙСТВУЮЩИЕ (общий тумблер × галочка опции),
        // клиенту знать про два уровня незачем. См. DebugOption.
        debug = new
        {
            ageRecordsDetails = await debug.IsEnabledAsync(DebugOptionKeys.ShowAgeRecordsDetails)
        }
    }))
    .AllowAnonymous();

// ── Диагностика конфигурации на старте ──────────────────────────────────────────────────
// Все эти вещи в проде отказывают МОЛЧА: без Google-секрета выключается вход (а вместе с ним
// и админка), без SMTP письма с одноразовыми токенами уходят в лог, забытая AdminConnection
// молча роняет рантайм на owner-роль. Пусть отказ будет видно в логе App Service.
// docs/plans/azure-deploy-plan.md A2.
{
    // Форма процесса — одной строкой в лог при каждом старте, в любом окружении: по логу App
    // Service сразу видно, не приехала ли на прод включённая админка.
    var modeLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Swimm.Startup");
    modeLog.LogInformation("Окружение {Environment}: админка {Admin}, фоновые джобы {Jobs}.",
        app.Environment.EnvironmentName, adminEnabled ? "ВКЛ" : "выкл", backgroundJobsEnabled ? "ВКЛ" : "выкл");
    if (adminEnabled && !backgroundJobsEnabled)
        modeLog.LogWarning(
            "Админка включена, а фоновые джобы — нет: импорт из очереди, прогон по странам и пакетный забор loglig встанут в очередь и не выполнятся.");
}

if (!app.Environment.IsDevelopment())
{
    var startupLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Swimm.Startup");
    var cfg = app.Configuration;
    var prodAdmin = app.Environment.IsProdAdmin();

    if (!googleEnabled)
        startupLog.LogCritical(adminEnabled
            ? "Google OAuth ВЫКЛЮЧЕН: не задан Authentication__Google__ClientSecret. Вход через Google не работает, а /admin для анонима ведёт именно туда — админка недостижима."
            : "Google OAuth ВЫКЛЮЧЕН: не задан Authentication__Google__ClientSecret. Пользователи сайта не могут войти через Google.");

    if (string.IsNullOrWhiteSpace(cfg["Email:Smtp:Host"]))
        startupLog.LogCritical(prodAdmin
            ? "SMTP НЕ НАСТРОЕН (пуст Email__Smtp__Host): письма, которые шлёт локальная админка (решения по заявкам групп на клуб), уходят только в лог. Пароль Resend — Email:Smtp:Password в appsettings.ProdAdmin.Local.json."
            : "SMTP НЕ НАСТРОЕН (пуст Email__Smtp__Host): письма подтверждения почты и сброса пароля никуда не уходят. Регистрация и восстановление доступа в проде не работают.");

    // MigrationConnection (owner-роль) сайту не нужна: миграции идут из ProdAdmin с машины Влада
    // (В8, В17). На App Service её не задаём — пароль владельца базы на веб-сервере не лежит, —
    // поэтому и не проверяем: там она — localhost-заглушка из appsettings.json, и это нормально.
    var connectionNames = prodAdmin
        ? new[] { "DefaultConnection", "MigrationConnection", "AdminConnection", "ReadConnection" }
        : new[] { "DefaultConnection", "AdminConnection", "ReadConnection" };
    foreach (var name in connectionNames)
    {
        var cs = cfg.GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(cs))
            startupLog.LogWarning(
                "Строка подключения {Name} не задана — используется фоллбек на DefaultConnection. Для AdminConnection/ReadConnection это значит работу под owner-ролью мимо least-privilege.", name);
        else if (cs.Contains("localhost", StringComparison.OrdinalIgnoreCase) || cs.Contains("127.0.0.1"))
            startupLog.LogCritical(
                "Строка подключения {Name} указывает на localhost вне Development — почти наверняка забыли задать ConnectionStrings__{Name} в App Service.", name, name);
        else if (!cs.Contains("SslMode", StringComparison.OrdinalIgnoreCase))
            startupLog.LogWarning(
                "В строке подключения {Name} не задан SslMode. Azure Database for PostgreSQL требует TLS — добавь SslMode=Require.", name);
    }

    // ProdAdmin слушает только localhost и наружу не смотрит: ни AllowedHosts, ни общее кольцо
    // ключей Data Protection ему не нужны (кука локальной админки живёт на этой машине).
    if (!prodAdmin && cfg["AllowedHosts"] is null or "*")
        startupLog.LogWarning(
            "AllowedHosts = \"*\": ссылки в письмах строятся из заголовка Host, то есть их можно подделать. Сузь до реального хоста прода.");

    if (!prodAdmin && string.IsNullOrWhiteSpace(cfg["DataProtection:KeysDir"]))
        startupLog.LogWarning(
            "DataProtection:KeysDir не задан — кольцо ключей лежит в эфемерной файловой системе. После деплоя или рестарта у всех слетят сессии и сломается antiforgery в админке.");
}

app.Run();
