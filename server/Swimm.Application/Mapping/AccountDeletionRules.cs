namespace Swimm.Application.Mapping;

/// <summary>
/// Можно ли удалить аккаунт и как это подтверждается (docs/plans/account-deletion-plan.md,
/// решения Р2, Р3, Р6 от 23.10.2026). Единственное место правила: его читают и окно на
/// /setup-profile (через impact), и само удаление, и кнопка в /Admin/Users — чтобы окно не
/// обещало то, что удаление потом откажется делать.
///
/// - Р6: аккаунт с site-ролью Admin не удаляется — сначала снять роль (защита от потери
///   последнего админа и от удаления того, чьё имя стоит в аудите решений).
/// - Р2: группа, где кроме владельца есть другие аккаунты (участники или админы), блокирует
///   удаление — её нужно удалить или передать. Группа «только своя» уходит вместе с аккаунтом.
///   Админ сайта по письму может снять блок, решив за владельца удалить и такие группы.
/// - Р3: подтверждение — свой email; плюс пароль, если есть локальный вход, иначе (только
///   Google) вход не старше <see cref="FreshSignInWindow"/>.
///
/// Тексты причин идут на витрину — поэтому по-английски (правило UI).
/// </summary>
public static class AccountDeletionRules
{
    public const string ReasonSiteAdmin = "site-admin";
    public const string ReasonSharedGroup = "owns-shared-group";

    public const string SiteAdminMessage =
        "Site admins can't delete their account. Remove the Admin role first.";

    public const string SharedGroupMessage =
        "You own a group with other members. Delete the group or hand it over first.";

    /// <summary>Claim с моментом входа (UTC, unix-секунды) — его пишет выпуск auth-куки.</summary>
    /// <remarks>
    /// Не <c>AuthenticationProperties.IssuedUtc</c>: при sliding expiration куку перевыпускают,
    /// и IssuedUtc сдвигается — «свежим входом» стала бы любая активная вкладка.
    /// </remarks>
    public const string AuthTimeClaim = "swimm:auth_time";

    /// <summary>Насколько свежим должен быть вход через Google, чтобы удалить аккаунт без пароля.</summary>
    public static readonly TimeSpan FreshSignInWindow = TimeSpan.FromMinutes(10);

    /// <summary>Группа делится с другими людьми: в ней есть аккаунты кроме владельца.</summary>
    public static bool IsShared(int otherAccountMembers, int otherAdmins) =>
        otherAccountMembers > 0 || otherAdmins > 0;

    /// <summary>
    /// Причины, по которым удалить нельзя (коды <see cref="ReasonSiteAdmin"/>,
    /// <see cref="ReasonSharedGroup"/>). Пусто — можно.
    /// <paramref name="ownsSharedGroups"/> — владеет хотя бы одной группой с другими людьми;
    /// <paramref name="deleteSharedGroups"/> — админ сайта решил удалить и их (путь /Admin/Users).
    /// </summary>
    public static IReadOnlyList<string> BlockReasons(bool isSiteAdmin, bool ownsSharedGroups, bool deleteSharedGroups = false)
    {
        var reasons = new List<string>();
        if (isSiteAdmin) reasons.Add(ReasonSiteAdmin);
        if (ownsSharedGroups && !deleteSharedGroups) reasons.Add(ReasonSharedGroup);
        return reasons;
    }

    /// <summary>Текст причины для витрины.</summary>
    public static string Message(string reason) => reason switch
    {
        ReasonSiteAdmin => SiteAdminMessage,
        ReasonSharedGroup => SharedGroupMessage,
        _ => "Your account can't be deleted right now.",
    };

    /// <summary>Введённый email совпадает с email аккаунта (без учёта регистра и пробелов).</summary>
    public static bool EmailMatches(string? typed, string accountEmail) =>
        !string.IsNullOrWhiteSpace(typed)
        && string.Equals(typed.Trim(), accountEmail.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Вход свежий: был не раньше <see cref="FreshSignInWindow"/> назад (и не «из будущего»).</summary>
    public static bool IsFreshSignIn(DateTime? authTimeUtc, DateTime nowUtc) =>
        authTimeUtc is { } t
        && t <= nowUtc.AddMinutes(1)
        && nowUtc - t <= FreshSignInWindow;

    /// <summary>Разбор claim <see cref="AuthTimeClaim"/>; null — claim нет или он битый.</summary>
    public static DateTime? ParseAuthTime(string? claimValue) =>
        long.TryParse(claimValue, out var seconds) && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;
}
