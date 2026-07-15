using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Swimm.API.Services;
using Npgsql;
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

        var db       = context.HttpContext.RequestServices.GetRequiredService<SwimmDbContext>();
        var dbStatus = context.HttpContext.RequestServices.GetRequiredService<DbStatusService>();

        // Быстрый выход: если БД уже известна как недоступная — пропускаем проверку штампа,
        // не тратим ~5 сек на 3 повторные попытки подключения.
        if (!dbStatus.IsAvailable)
        {
            var logger2 = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(nameof(CookieSecurityStampValidator));
            logger2.LogDebug("Security stamp check skipped — DB status cached as unavailable (userId={UserId}).", userId);
            return;
        }

        try
        {
            var snapshot = await db.AppUsers
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.IsActive, u.SecurityStamp })
                .FirstOrDefaultAsync();

            dbStatus.MarkAvailable(); // БД ответила

            if (snapshot == null || !snapshot.IsActive || snapshot.SecurityStamp != cookieStamp)
            {
                await RejectAsync(context);
                return;
            }

            // Метка активности для админки («онлайн сейчас»). Дёшево: мы уже на
            // троттленном пути (≤ раза в ValidateInterval на сессию), один UPDATE без чтения.
            await db.AppUsers
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSeenAt, now.UtcDateTime));
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or RetryLimitExceededException)
        {
            // БД недоступна (transient failure) — пропускаем проверку штампа для этого запроса,
            // сессию не отзываем. Следующий запрос после истечения ValidateInterval повторит попытку.
            dbStatus.MarkUnavailable(); // немедленно обновляем флаг, не дожидаясь пинга
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(nameof(CookieSecurityStampValidator));
            logger.LogWarning(ex, "Security stamp check skipped — DB unreachable (userId={UserId}).", userId);
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
