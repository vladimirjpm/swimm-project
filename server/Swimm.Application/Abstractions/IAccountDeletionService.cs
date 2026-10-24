using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Удаление аккаунта (docs/plans/account-deletion-plan.md): кнопка «Delete account» на
/// /setup-profile и тот же путь из /Admin/Users по письму на privacy@. Правило — только
/// <c>AccountDeletionRules</c>.
/// </summary>
public interface IAccountDeletionService
{
    /// <summary>
    /// Что удалится и можно ли удалить. <paramref name="authTimeUtc"/> — момент входа из куки
    /// (для «свежего входа»); null — неизвестен (админка).
    /// </summary>
    Task<AccountDeletionImpactDto?> GetImpactAsync(int userId, DateTime? authTimeUtc = null, CancellationToken ct = default);

    /// <summary>Удалить свой аккаунт с подтверждением (Р3).</summary>
    Task<AccountDeletionResult> DeleteSelfAsync(int userId, AccountDeletionRequest request, DateTime? authTimeUtc, CancellationToken ct = default);

    /// <summary>
    /// Удалить аккаунт из админки (по письму). Без пароля; <paramref name="reason"/> пишется в
    /// аудит. <paramref name="deleteSharedGroups"/> — админ решил за владельца удалить и группы
    /// с другими людьми (иначе такие группы блокируют, как у кнопки).
    /// </summary>
    Task<AccountDeletionResult> DeleteByAdminAsync(int userId, string reason, bool deleteSharedGroups, CancellationToken ct = default);
}
