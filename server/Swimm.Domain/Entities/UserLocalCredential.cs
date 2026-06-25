using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Локальные учётные данные пользователя (вход по email + паролю).
/// Связь 1:1 с <see cref="AppUser"/> (общий первичный ключ = UserId).
///
/// ⚠️ ПОДГОТОВЛЕНО, НО НЕ АКТИВИРОВАНО: таблица и поля заведены заранее, однако
/// логика локального входа (регистрация, проверка пароля, подтверждение email,
/// сброс пароля) на этом этапе НЕ реализована. Вход выполняется только через
/// внешних провайдеров (<see cref="UserExternalLogin"/>).
/// </summary>
public class UserLocalCredential
{
    /// <summary>PK и одновременно FK на AppUser (shared primary key, связь 1:1).</summary>
    [Key]
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public AppUser User { get; set; } = null!;

    /// <summary>Хеш пароля (Argon2id / BCrypt). null — пароль ещё не задан.</summary>
    [MaxLength(256)]
    public string? PasswordHash { get; set; }

    /// <summary>
    /// Идентификатор алгоритма/параметров хеширования (напр. "argon2id-v1").
    /// Позволяет в будущем мигрировать на другой алгоритм без сброса паролей.
    /// </summary>
    [MaxLength(50)]
    public string? PasswordAlgorithm { get; set; }

    /// <summary>Подтверждён ли email владельцем (через письмо со ссылкой).</summary>
    public bool EmailConfirmed { get; set; }

    public DateTime? EmailConfirmedAt { get; set; }

    /// <summary>Счётчик последовательных неудачных входов (для rate-limit / блокировки).</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>До какого момента вход заблокирован после серии неудачных попыток.</summary>
    public DateTime? LockoutEnd { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
