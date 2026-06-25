using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

public enum SecurityTokenPurpose
{
    EmailVerification = 1,
    PasswordReset = 2
}

/// <summary>
/// Одноразовый токен для подтверждения email / сброса пароля.
///
/// 🔐 Хранится ТОЛЬКО хеш токена (SHA-256), не сам токен: утечка БД не даёт рабочих ссылок.
/// Сырой высокоэнтропийный токен отправляется пользователю в письме и в БД не сохраняется.
/// Одноразовость — через <see cref="ConsumedAt"/>; срок — через <see cref="ExpiresAt"/>.
/// </summary>
public class UserSecurityToken
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public AppUser User { get; set; } = null!;

    public SecurityTokenPurpose Purpose { get; set; }

    /// <summary>SHA-256 хеша токена в hex (64 символа).</summary>
    [Required, MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    /// <summary>Момент использования (одноразовость). null — ещё не использован.</summary>
    public DateTime? ConsumedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
