using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;

namespace Swimm.API.Security;

/// <summary>
/// Claims, которые обработчик Google переносит из ответа userinfo, сверх стандартных.
///
/// <b>email_verified.</b> Стандартный набор GoogleOptions — id, имя, фамилия, email; признак
/// «Google подтвердил владение email» он НЕ переносит. А <c>AuthController</c> без этого признака
/// не создаёт нового пользователя и не связывает вход с существующим аккаунтом по email — защита
/// от захвата чужого аккаунта. Без сопоставления claim пуст ВСЕГДА, и войти через Google не мог ни
/// один новый человек (поймано на первом входе на прод 15.10.2026: локально аккаунт Влада уже был
/// привязан к Google и до проверки не доходил).
///
/// Имя поля зависит от эндпоинта userinfo: v2 (им ходит обработчик) отдаёт <c>verified_email</c>,
/// v3 и id_token — <c>email_verified</c>. Сопоставляем оба в один claim <see cref="EmailVerified"/>;
/// ключа нет в ответе — действие ничего не добавляет.
/// </summary>
public static class GoogleClaims
{
    public const string EmailVerified = "email_verified";

    public static void Configure(GoogleOptions options)
    {
        options.ClaimActions.MapJsonKey(EmailVerified, "verified_email");
        options.ClaimActions.MapJsonKey(EmailVerified, "email_verified");
    }
}
