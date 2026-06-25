using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Локальный вход (email + пароль) поверх Sys_UserLocalCredentials.
/// HTTP-аспекты (выпуск cookie) остаются в контроллере; сервис — доменная логика и БД.
/// </summary>
public interface ILocalAuthService
{
    /// <summary>Регистрация. Создаёт/привязывает локальные креды, отправляет письмо подтверждения.
    /// Anti-enumeration: при существующем email возвращает Ok и шлёт соответствующее письмо.</summary>
    Task<RegisterResult> RegisterAsync(string email, string password, string? displayName, string baseUrl, CancellationToken ct = default);

    /// <summary>Вход. На Ok возвращает UserId (контроллер выпускает cookie). Управляет lockout-счётчиком.</summary>
    Task<LoginResult> LoginAsync(string email, string password, CancellationToken ct = default);

    /// <summary>Подтверждение email по токену. true — успех.</summary>
    Task<bool> ConfirmEmailAsync(string token, CancellationToken ct = default);

    /// <summary>Запрос сброса пароля. Всегда «успех» (anti-enumeration); письмо шлётся, только если аккаунт есть.</summary>
    Task ForgotPasswordAsync(string email, string baseUrl, CancellationToken ct = default);

    /// <summary>Сброс пароля по токену. На Ok бампает SecurityStamp (инвалидация всех сессий) и возвращает UserId.</summary>
    Task<ResetResult> ResetPasswordAsync(string token, string newPassword, CancellationToken ct = default);
}
