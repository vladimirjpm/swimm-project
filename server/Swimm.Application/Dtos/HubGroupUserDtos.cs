namespace Swimm.Application.Dtos;

/// <summary>
/// Может ли текущий пользователь создать группу — для кнопки и счётчика «2 / 3» в UI.
/// Считает <see cref="Swimm.Application.Mapping.HubGroupCreationRules"/>.
/// </summary>
public sealed class HubGroupCreateEligibilityDto
{
    public bool CanCreate { get; set; }

    /// <summary>Причина отказа — текст для витрины, по-английски. null — отказа нет.</summary>
    public string? Reason { get; set; }

    /// <summary>Сколько групп пользователь уже ВЛАДЕЕТ (официальные тоже).</summary>
    public int Owned { get; set; }

    /// <summary>Действующий лимит: персональный или по роли; null — без лимита (админ).</summary>
    public int? Limit { get; set; }

    /// <summary>Сколько ещё групп можно создать; null — без лимита (админ).</summary>
    public int? Remaining { get; set; }
}

/// <summary>Админ группы — для списка управления в пользовательской форме.</summary>
public sealed class HubGroupAdminMemberDto
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Участник-аккаунт группы (Sys_HubGroupUserMembers) — для приватного списка состава в
/// панели управления. Отличается от пловца (<see cref="HubGroupMemberRowDto"/>): это аккаунт.
/// </summary>
public sealed class HubGroupUserMemberRowDto
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    /// <summary>active | pending.</summary>
    public string Status { get; set; } = "";
    /// <summary>true = вступил сам (AddedByUserId == null), false = добавлен владельцем/админом.</summary>
    public bool SelfJoined { get; set; }
    public DateTime JoinedAt { get; set; }

    /// <summary>Ярлык «за какого пловца этот аккаунт» — чисто отображение, прав не даёт. null = не привязан.</summary>
    public int? SwimmerId { get; set; }
    public string? SwimmerName { get; set; }
    /// <summary>Произвольная подпись («родитель», …), опционально.</summary>
    public string? Note { get; set; }
}

/// <summary>Группа, в которую текущий пользователь вступил как участник-аккаунт (список «участвую»).</summary>
public sealed class JoinedHubGroupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? IconUrl { get; set; }
    /// <summary>active | pending.</summary>
    public string Status { get; set; } = "";
    public DateTime JoinedAt { get; set; }
    /// <summary>Заявка тренера висит (docs/plans/coach-access-request-plan.md).</summary>
    public bool CoachRequested { get; set; }
}

/// <summary>
/// Заявка тренера на группу — строка карточки «Coach requests» в табе Admin
/// (docs/plans/coach-access-request-plan.md, Т2). Видит только тот, у кого CanManageAdmins.
/// </summary>
public sealed class HubGroupCoachRequestDto
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime RequestedAt { get; set; }
    /// <summary>Членство заявителя: active | pending (группа с одобрением — одобрение заявки его активирует).</summary>
    public string MembershipStatus { get; set; } = "";
    /// <summary>Его пловец «Me» (Sys_UserFavorites.IsPrimary); null — не отмечен.</summary>
    public int? MeSwimmerId { get; set; }
    /// <summary>Имя «Me»-пловца — на иврите, английское только фоллбеком.</summary>
    public string? MeSwimmerName { get; set; }
    /// <summary>«Me»-пловец стоит в видимом составе группы — одобрение поставит метку на него.</summary>
    public bool MeSwimmerInGroup { get; set; }
}

/// <summary>Итог заявки тренера: <see cref="Created"/> — заявка новая (повод для push владельцу).</summary>
public sealed record HubGroupCoachRequestResult(bool Success, string? Error, bool Created, bool AlreadyCoach)
{
    public static HubGroupCoachRequestResult New() => new(true, null, Created: true, AlreadyCoach: false);
    public static HubGroupCoachRequestResult Repeat() => new(true, null, Created: false, AlreadyCoach: false);
    public static HubGroupCoachRequestResult Coach() => new(false, CoachAccessRules.AlreadyCoachError, Created: false, AlreadyCoach: true);
    public static HubGroupCoachRequestResult Fail(string error) => new(false, error, Created: false, AlreadyCoach: false);
}

/// <summary>Тексты заявки тренера — показываются в UI как есть, поэтому по-английски.</summary>
public static class CoachAccessRules
{
    public const string AlreadyCoachError = "You already manage this group.";
    public const string NoRequestError = "No coach request from this member.";
}
