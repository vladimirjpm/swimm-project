using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.API.Security;

/// <summary>
/// Dev-обход логина: неаутентифицированный запрос получает личность РЕАЛЬНОГО пользователя
/// из БД (по умолчанию — первый активный админ; можно указать другого через
/// <c>DevAdminBypassEmail</c> в appsettings.Development.json).
///
/// Почему из БД, а не синтетический id 0 (как было до 09.09.2026): у синтетической личности
/// не было ни email-claim, ни строки в <c>Sys_AppUsers</c>, из-за чего обход работал только
/// для чтения админских ручек, а всё, что упирается в пользователя, ломалось:
///  • <c>/auth/me</c> ищет юзера по email-claim → отвечал <c>isAuthenticated:false</c>, и
///    браузер считал себя гостем (таб Admin не появлялся, /my-media показывала «Sign in»);
///  • <c>DecidedByUserId</c> ссылается на <c>Sys_AppUsers</c> → решение по заявке падало 500 на FK;
///  • owner-scoped ручки (<c>/api/me/*</c>) возвращали пусто — владелец медиа не id 0.
/// Синтетическая личность осталась ФОЛЛБЕКОМ: на пустой БД админку всё равно надо поднять.
///
/// <b>Переключатель персонажа</b> (docs/plans/test-personas-plan.md, этап 3): dev-кука
/// <see cref="PersonaCookie"/> выбирает, кем идёт запрос, без перезапуска API — utest-персонаж
/// из <c>TestPersonas.All</c>, <c>utest-guest</c> (без входа) или ничего (личность по умолчанию).
/// Ставится ручкой <c>GET /api/dev/persona?as=…</c>, список — <c>GET /api/dev/personas</c>;
/// на клиенте это плашка, которую Vite внедряет только в dev. Настоящий вход (кука логина)
/// сильнее переключателя: персонаж действует только на неаутентифицированный запрос.
///
/// Работает ТОЛЬКО в Development и только при <c>"DevAdminBypass": true</c>; в проде ветка мертва
/// и ручек /api/dev/* нет вовсе.
/// </summary>
public static class DevAdminBypass
{
    public const string AuthenticationType = "DevAdminBypass";
    private const string AdminRole = "Admin";

    /// <summary>Кука выбора персонажа: ник utest-персонажа или <see cref="Guest"/>.</summary>
    public const string PersonaCookie = "swimm_dev_persona";
    public const string Guest = "utest-guest";
    /// <summary>Значение ?as= для сброса к личности по умолчанию.</summary>
    public const string Default = "default";

    /// <summary>
    /// Claims по email (ключ "" — личность по умолчанию). Middleware висит на КАЖДОМ
    /// неаутентифицированном запросе (включая статику), поэтому без кэша никак. Срок короткий:
    /// сидер <c>--seed-personas --reset</c> пересоздаёт utest-пользователей с новыми id, и
    /// вечный кэш отдавал бы личность, которой в базе уже нет.
    /// </summary>
    private static readonly ConcurrentDictionary<string, (Claim[]? Claims, DateTime At)> Cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static void UseDevAdminBypass(this WebApplication app)
    {
        var defaultEmail = app.Configuration["DevAdminBypassEmail"];
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DevAdminBypass));

        app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                var persona = ReadPersona(context.Request.Cookies[PersonaCookie]);
                // utest-guest — ровно то, что видит посетитель без входа: личность не подставляем.
                if (persona != Guest)
                {
                    var claims = await ResolveClaimsAsync(context.RequestServices, persona ?? defaultEmail, isPersona: persona != null, logger);
                    // Персонаж есть в списке, но в базе его нет или он заблокирован — как гость:
                    // настоящий вход такого пользователя тоже не пустил бы.
                    if (claims != null)
                        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType));
                }
            }
            await next();
        });

        MapPersonaEndpoints(app, defaultEmail);
    }

    /// <summary>Ник из куки → email персонажа или <see cref="Guest"/>; мусор — null (по умолчанию).</summary>
    private static string? ReadPersona(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (raw == Guest) return Guest;
        return TestPersonas.All.FirstOrDefault(p => p.Nick == raw)?.Email;
    }

    private static void MapPersonaEndpoints(WebApplication app, string? defaultEmail)
    {
        app.MapGet("/api/dev/personas", (HttpContext context, SwimmDbContext db) =>
            DescribeAsync(context, db, context.Request.Cookies[PersonaCookie], defaultEmail));

        // GET, а не POST: ручка есть только в Development, а так её можно дёрнуть и адресной
        // строкой (удобно агенту и руками). Ответ — тот же, что у /api/dev/personas.
        app.MapGet("/api/dev/persona", (HttpContext context, SwimmDbContext db, string? @as) =>
        {
            string? chosen;
            if (string.IsNullOrWhiteSpace(@as) || @as == Default) chosen = null;
            else if (@as == Guest || TestPersonas.All.Any(p => p.Nick == @as)) chosen = @as;
            else return Task.FromResult(Results.BadRequest(new
            {
                error = $"Unknown persona '{@as}'. Use one of: {Default}, {Guest}, "
                        + string.Join(", ", TestPersonas.All.Select(p => p.Nick))
            }));

            if (chosen == null) context.Response.Cookies.Delete(PersonaCookie);
            else context.Response.Cookies.Append(PersonaCookie, chosen, new CookieOptions
            {
                HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/", IsEssential = true,
            });
            return DescribeAsync(context, db, chosen, defaultEmail);
        });
    }

    /// <summary>Кто сейчас выбран, кто есть и какие тест-группы — для плашки переключателя.</summary>
    private static async Task<IResult> DescribeAsync(HttpContext context, SwimmDbContext db, string? chosen, string? defaultEmail)
    {
        var emails = TestPersonas.All.Select(p => p.Email).ToList();
        var existing = await db.AppUsers.AsNoTracking()
            .Where(u => emails.Contains(u.Email))
            .Select(u => new { u.Email, u.IsActive })
            .ToListAsync();
        var testGroups = await db.HubGroups.AsNoTracking()
            .Where(g => g.IsTest)
            .OrderBy(g => g.Name)
            .Select(g => new { slug = g.Slug, name = g.Name, joinPolicy = g.JoinPolicy })
            .ToListAsync();

        // Настоящий вход перекрывает персонажа — плашка должна об этом сказать, иначе
        // «переключил, а ничего не поменялось».
        var realLogin = context.User.Identity is { IsAuthenticated: true } id && id.AuthenticationType != AuthenticationType;

        return Results.Ok(new
        {
            current = string.IsNullOrEmpty(chosen) ? Default : chosen,
            defaultEmail = string.IsNullOrWhiteSpace(defaultEmail) ? "(first active admin)" : defaultEmail,
            realLogin,
            personas = TestPersonas.All.Select(p =>
            {
                var row = existing.FirstOrDefault(u => u.Email == p.Email);
                return new
                {
                    nick = p.Nick,
                    email = p.Email,
                    description = p.Description,
                    exists = row != null,
                    isActive = row?.IsActive ?? false,
                };
            }),
            testGroups,
        });
    }

    /// <summary>
    /// Claims пользователя по email. Персонаж (<paramref name="isPersona"/>): не найден или
    /// заблокирован — null (запрос пойдёт гостем). Личность по умолчанию: не найдена — синтетический
    /// админ, как раньше.
    /// </summary>
    private static async Task<Claim[]?> ResolveClaimsAsync(IServiceProvider services, string? email, bool isPersona, ILogger logger)
    {
        var key = email ?? "";
        if (Cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < CacheTtl) return hit.Claims;

        await Gate.WaitAsync();
        try
        {
            if (Cache.TryGetValue(key, out hit) && DateTime.UtcNow - hit.At < CacheTtl) return hit.Claims;

            AppUser? user;
            try
            {
                var db = services.GetRequiredService<SwimmDbContext>();
                var users = db.AppUsers.AsNoTracking()
                    .Include(u => u.UserRoles).ThenInclude(ur => ur.Role);

                user = string.IsNullOrWhiteSpace(email)
                    ? await users.Where(u => u.IsActive && u.UserRoles.Any(ur => ur.Role.Name == AdminRole))
                                 .OrderBy(u => u.Id).FirstOrDefaultAsync()
                    : await users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive);
            }
            catch (Exception ex)
            {
                // БД ещё не поднялась / нет миграций — отдаём синтетического админа, но НЕ
                // кэшируем: следующий запрос попробует снова.
                logger.LogWarning(ex, "DevAdminBypass: не смог прочитать пользователя из БД, беру синтетического админа.");
                return isPersona ? null : Synthetic();
            }

            if (user is null)
            {
                if (isPersona)
                {
                    logger.LogWarning(
                        "DevAdminBypass: персонаж {Email} не найден или заблокирован — запрос идёт гостем. "
                        + "Нет персонажей — dotnet run -- --seed-personas.", email);
                    Cache[key] = (null, DateTime.UtcNow);
                    return null;
                }
                logger.LogWarning(
                    "DevAdminBypass: пользователь {Email} не найден — беру синтетического админа (id 0). "
                    + "Ручки, которым нужен реальный пользователь, на нём падают.",
                    email ?? "<первый админ>");
                return Synthetic();
            }

            if (!isPersona && !user.UserRoles.Any(ur => ur.Role.Name == AdminRole))
            {
                // Роль НЕ дорисовываем: если email указан явно, смотреть страницу надо ровно
                // такими глазами, какие у этого пользователя есть.
                logger.LogWarning("DevAdminBypass: у {Email} нет роли Admin — админка будет закрыта.", user.Email);
            }

            var claims = BuildClaims(user);
            var first = !Cache.ContainsKey(key);
            Cache[key] = (claims, DateTime.UtcNow);
            if (first)
                logger.LogWarning(
                    "DevAdminBypass ВКЛЮЧЁН: неаутентифицированные запросы идут от {Email} (id {Id}).",
                    user.Email, user.Id);
            return claims;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Тот же набор claims, что выдаёт настоящий логин (AuthController.IssueAuthCookieAsync).</summary>
    private static Claim[] BuildClaims(AppUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName),
            new(CookieSecurityStampValidator.SecurityStampClaim, user.SecurityStamp),
        };
        foreach (var ur in user.UserRoles)
            claims.Add(new Claim(ClaimTypes.Role, ur.Role.Name));
        return [.. claims];
    }

    /// <summary>Старое поведение: админ без строки в БД. Годится только на чтение админки.</summary>
    private static Claim[] Synthetic() =>
    [
        new(ClaimTypes.NameIdentifier, "0"),
        new(ClaimTypes.Name, "dev-admin"),
        new(ClaimTypes.Role, AdminRole),
    ];
}
