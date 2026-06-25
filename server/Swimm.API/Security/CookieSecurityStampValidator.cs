using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Swimm.Infrastructure.Data;

namespace Swimm.API.Security;

/// <summary>
/// Ре-валидация cookie-сессии на каждом запросе (с троттлингом по интервалу).
/// Заменяет ASP.NET Identity SecurityStampValidator для нашей «ручной» cookie-аутентификации.
///
/// Что проверяет:
///  • пользователь существует и IsActive == true   → иначе отзыв доступа;
///  • SecurityStamp из куки == SecurityStamp в БД    → иначе принудительный re-login
///    (смена ролей / «выйти со всех устройств» бампают штамп в БД).
///
/// Троттлинг: к БД ходим не чаще, чем раз в <see cref="ValidateInterval"/> на сессию.
/// Время последней проверки храним в AuthenticationProperties куки.
/// </summary>
public static class CookieSecurityStampValidator
{
    /// <summary>Имя claim'а со штампом безопасности в куки.</summary>
    public const string SecurityStampClaim = "SecurityStamp";

    /// <summary>Ключ для отметки времени последней ре-валидации в AuthenticationProperties.</summary>
    private const string LastValidatedKey = "LastValidatedUtc";

    /// <summary>Как часто перечитывать пользователя из БД. Цена: до этого окна отзыв доступа «отложен».</summary>
    private static readonly TimeSpan ValidateInterval = TimeSpan.FromMinutes(5);

    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        if (principal?.Identity?.IsAuthenticated != true)
            return;

        // Троттлинг: если недавно проверяли — пропускаем поход в БД.
        var now = DateTimeOffset.UtcNow;
        var lastRaw = context.Properties.GetString(LastValidatedKey);
        if (lastRaw != null
            && DateTimeOffset.TryParse(lastRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var last)
            && now - last < ValidateInterval)
        {
            return;
        }

        var userIdRaw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var cookieStamp = principal.FindFirstValue(SecurityStampClaim);
        if (!int.TryParse(userIdRaw, out var userId) || string.IsNullOrEmpty(cookieStamp))
        {
            await RejectAsync(context);
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<SwimmDbContext>();
        var snapshot = await db.AppUsers
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.SecurityStamp })
            .FirstOrDefaultAsync();

        if (snapshot == null || !snapshot.IsActive || snapshot.SecurityStamp != cookieStamp)
        {
            await RejectAsync(context);
            return;
        }

        // Проверка пройдена — обновляем отметку времени и переиздаём куки.
        context.Properties.SetString(LastValidatedKey, now.ToString("o", CultureInfo.InvariantCulture));
        context.ShouldRenew = true;
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
