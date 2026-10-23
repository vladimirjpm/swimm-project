namespace Swimm.Application.Dtos;

/// <summary>
/// Что удалится вместе с аккаунтом и можно ли его удалить сейчас — для окна «Delete account»
/// на /setup-profile и карточки пользователя в /Admin/Users (docs/plans/account-deletion-plan.md).
/// </summary>
public sealed class AccountDeletionImpactDto
{
    public int UserId { get; set; }

    public int Favorites { get; set; }

    /// <summary>Свои ссылки на медиа (Sys_UserMedia).</summary>
    public int Media { get; set; }

    /// <summary>Из них опубликованы в группы/клубы — пропадут и оттуда.</summary>
    public int PublishedMedia { get; set; }

    /// <summary>Реакции («нравится», «болею»), поставленные пользователем.</summary>
    public int Reactions { get; set; }

    public int StartListPlans { get; set; }

    /// <summary>Участие в чужих группах (Sys_HubGroupUserMembers, без своих).</summary>
    public int GroupMemberships { get; set; }

    /// <summary>Админство в чужих группах.</summary>
    public int GroupAdminships { get; set; }

    /// <summary>Устройства с включёнными уведомлениями.</summary>
    public int PushDevices { get; set; }

    /// <summary>
    /// Ссылки и тренировки, добавленные в группы (галерея, тренировки, планы дорожек) — остаются
    /// в группах без автора (Р4). Свои группы, уходящие целиком, сюда не входят.
    /// </summary>
    public int GroupContentKept { get; set; }

    /// <summary>Свои группы без других людей — удалятся вместе с аккаунтом.</summary>
    public List<HubGroupDeleteImpactDto> GroupsDeleted { get; set; } = new();

    /// <summary>Свои группы с другими людьми — блокируют удаление (Р2).</summary>
    public List<AccountSharedGroupDto> SharedGroups { get; set; } = new();

    /// <summary>Есть локальный вход — подтверждение паролем (Р3).</summary>
    public bool HasPassword { get; set; }

    /// <summary>Вход свежий (≤ 10 минут) — для аккаунтов без пароля этого достаточно (Р3).</summary>
    public bool SignInFresh { get; set; }

    /// <summary>Коды причин, почему нельзя (<c>AccountDeletionRules.Reason*</c>). Пусто — можно.</summary>
    public List<string> BlockReasons { get; set; } = new();

    /// <summary>Те же причины текстом для витрины (по-английски).</summary>
    public List<string> BlockMessages { get; set; } = new();
}

/// <summary>Своя группа, в которой есть другие аккаунты.</summary>
public sealed class AccountSharedGroupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? NameEn { get; set; }
    public string Slug { get; set; } = "";

    /// <summary>Другие аккаунты: участники и админы группы, кроме владельца.</summary>
    public int OtherPeople { get; set; }
}

/// <summary>Подтверждение удаления своего аккаунта (Р3).</summary>
public sealed class AccountDeletionRequest
{
    public string? ConfirmEmail { get; set; }

    /// <summary>Обязателен, если у аккаунта есть локальный вход.</summary>
    public string? Password { get; set; }
}

/// <summary>
/// Итог удаления. <c>Status</c> — HTTP-код для контроллера: 204 — удалён, 404 — нет такого,
/// 409 — заблокировано правилом (<c>Code</c> — причина), 403 — подтверждение не прошло.
/// </summary>
public sealed record AccountDeletionResult(int Status, string? Error = null, string? Code = null)
{
    public bool Deleted => Status == 204;
}
