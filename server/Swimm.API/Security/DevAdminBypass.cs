using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
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
/// Работает ТОЛЬКО в Development и только при <c>"DevAdminBypass": true</c>; в проде ветка мертва.
/// </summary>
public static class DevAdminBypass
{
    public const string AuthenticationType = "DevAdminBypass";
    private const string AdminRole = "Admin";

    /// <summary>
    /// Claims выбранного пользователя. Кэш на весь процесс: middleware висит на КАЖДОМ
    /// неаутентифицированном запросе (включая статику), ходить за этим в БД каждый раз
    /// незачем. Обратная сторона — смена ролей/email подхватится только после рестарта;
    /// для dev-обхода это приемлемо.
    /// </summary>
    private static Claim[]? _cached;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static void UseDevAdminBypass(this WebApplication app)
    {
        var email = app.Configuration["DevAdminBypassEmail"];
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DevAdminBypass));

        app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                var claims = await ResolveClaimsAsync(context.RequestServices, email, logger);
                context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType));
            }
            await next();
        });
    }

    private static async Task<Claim[]> ResolveClaimsAsync(IServiceProvider services, string? email, ILogger logger)
    {
        var cached = _cached;
        if (cached is not null) return cached;

        await Gate.WaitAsync();
        try
        {
            if (_cached is not null) return _cached;

            AppUser? user;
            try
            {
                var db = services.GetRequiredService<SwimmDbContext>();
                var active = db.AppUsers.AsNoTracking()
                    .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                    .Where(u => u.IsActive);

                user = string.IsNullOrWhiteSpace(email)
                    ? await active.Where(u => u.UserRoles.Any(ur => ur.Role.Name == AdminRole))
                                  .OrderBy(u => u.Id).FirstOrDefaultAsync()
                    : await active.FirstOrDefaultAsync(u => u.Email == email);
            }
            catch (Exception ex)
            {
                // БД ещё не поднялась / нет миграций — отдаём синтетического админа, но НЕ
                // кэшируем: следующий запрос попробует снова.
                logger.LogWarning(ex, "DevAdminBypass: не смог прочитать пользователя из БД, беру синтетического админа.");
                return Synthetic();
            }

            if (user is null)
            {
                logger.LogWarning(
                    "DevAdminBypass: пользователь {Email} не найден — беру синтетического админа (id 0). "
                    + "Ручки, которым нужен реальный пользователь, на нём падают.",
                    email ?? "<первый админ>");
                return Synthetic();
            }

            if (!user.UserRoles.Any(ur => ur.Role.Name == AdminRole))
            {
                // Роль НЕ дорисовываем: если email указан явно, смотреть страницу надо ровно
                // такими глазами, какие у этого пользователя есть.
                logger.LogWarning("DevAdminBypass: у {Email} нет роли Admin — админка будет закрыта.", user.Email);
            }

            _cached = BuildClaims(user);
            logger.LogWarning(
                "DevAdminBypass ВКЛЮЧЁН: неаутентифицированные запросы идут от {Email} (id {Id}).",
                user.Email, user.Id);
            return _cached;
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
