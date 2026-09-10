namespace Swimm.Application.Dtos;

/// <summary>Строка списка Admin/HubGroups.</summary>
public sealed class HubGroupAdminRowDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? IconUrl { get; set; }
    public string? ClubName { get; set; }
    public int MemberCount { get; set; }
    public bool IsPublic { get; set; }
    public bool IsOfficial { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Владелец группы. В панели «My groups» по нему решается, показывать ли Delete: в том же
    /// списке лежат и группы, где пользователь всего лишь админ, а удалять может только владелец.
    /// </summary>
    public int OwnerUserId { get; set; }
}

/// <summary>
/// Что уйдёт вместе с группой при удалении: удаление жёсткое, всё ниже — каскадом. Показывается
/// в подтверждении (панель «My groups», /Admin/HubGroups) и уходит в аудит `hubgroup.delete`.
/// </summary>
public sealed class HubGroupDeleteImpactDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? NameEn { get; set; }
    public bool IsOfficial { get; set; }
    public string? ClubName { get; set; }

    /// <summary>Пловцы в составе. Сами пловцы остаются в справочнике.</summary>
    public int Swimmers { get; set; }

    /// <summary>Участники-аккаунты (active и pending).</summary>
    public int AccountMembers { get; set; }

    /// <summary>Назначенные админы группы (владелец не в счёт).</summary>
    public int Admins { get; set; }

    public int TrainingSessions { get; set; }
    public int TrainingResults { get; set; }

    /// <summary>Медиа группы (Sys_HubGroupMedia): галерея, фото тренировок, разборы.</summary>
    public int Media { get; set; }

    /// <summary>Публикации личных медиа участников в группу. Сами медиа остаются у авторов.</summary>
    public int MediaPublications { get; set; }

    public bool HasPendingClubRequest { get; set; }

    /// <summary>
    /// Есть ли что терять, кроме пустой оболочки. Пустую группу удаляют одной кнопкой, для
    /// остальных подтверждение просит ввести имя группы.
    /// </summary>
    public bool HasContent =>
        Swimmers + AccountMembers + Admins + TrainingSessions + Media + MediaPublications > 0
        || IsOfficial || HasPendingClubRequest;
}

/// <summary>Ссылка группы (WhatsApp/Telegram/Instagram/Site) — хранится JSON-массивом в <see cref="Swimm.Domain.Entities.HubGroup.Links"/>.</summary>
public sealed class HubGroupLinkDto
{
    public string Kind { get; set; } = "";
    public string Url { get; set; } = "";
}

/// <summary>Участник группы для таблицы формы Edit.</summary>
public sealed class HubGroupMemberRowDto
{
    public int Id { get; set; }
    public int SwimmerId { get; set; }
    public string SwimmerName { get; set; } = "";
    public string SwimmerNameEn { get; set; } = "";
    public int BirthYear { get; set; }
    public string? ClubName { get; set; }
    public string Role { get; set; } = "member";
    public int SortOrder { get; set; }

    /// <summary>manual | club (HubGroupMemberSource) — у клубного вместо ✕ «скрыть/вернуть».</summary>
    public string Source { get; set; } = "manual";

    /// <summary>
    /// Владелец скрыл клубного пловца. Приходит ТОЛЬКО в панель управления (там его можно
    /// вернуть); публичные ответы скрытых не содержат вовсе.
    /// </summary>
    public bool IsExcluded { get; set; }
}

/// <summary>Полные данные группы для формы Admin/HubGroups/Edit.</summary>
public sealed class HubGroupEditDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? NameEn { get; set; }
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? Location { get; set; }
    /// <summary>Alpha-3 код страны (ISR…) — форма работает кодами, FK резолвится при сохранении.</summary>
    public string? Country { get; set; }
    public int? ClubId { get; set; }
    public int OwnerUserId { get; set; }
    public string OwnerDisplayName { get; set; } = "";
    public bool IsPublic { get; set; }
    /// <summary>Официальная группа клуба — устанавливается только через одобрение заявки, не через форму.</summary>
    public bool IsOfficial { get; set; }
    /// <summary>open | approval — политика самозаписи (см. HubGroupJoinPolicy).</summary>
    public string JoinPolicy { get; set; } = "open";
    public List<HubGroupLinkDto> Links { get; set; } = [];
    /// <summary>Весь состав, включая скрытых клубных (<see cref="HubGroupMemberRowDto.IsExcluded"/>).</summary>
    public List<HubGroupMemberRowDto> Members { get; set; } = [];
    /// <summary>Участники-аккаунты (приватный список, не пловцы) — только в панели управления.</summary>
    public List<HubGroupUserMemberRowDto> UserMembers { get; set; } = [];
    /// <summary>Подписка на клуб; null — состав ведётся только руками.</summary>
    public HubGroupClubSubscriptionDto? ClubSubscription { get; set; }
}

/// <summary>Входные данные создания/обновления группы.</summary>
public sealed class HubGroupInputDto
{
    public string Name { get; set; } = "";
    public string? NameEn { get; set; }
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? Location { get; set; }
    /// <summary>Alpha-3 код страны (ISR…); пусто — без страны. FK резолвится find-or-create.</summary>
    public string? Country { get; set; }
    public int? ClubId { get; set; }
    public bool IsPublic { get; set; }
    /// <summary>open | approval; null/отсутствует — не менять (старый клиент не сбросит политику).</summary>
    public string? JoinPolicy { get; set; }
    public List<HubGroupLinkDto> Links { get; set; } = [];
}

/// <summary>Опция клуба для select в форме.</summary>
public sealed class ClubOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Результат поиска пловца (типаэд добавления участника).</summary>
public sealed class SwimmerSearchResultDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string NameEn { get; set; } = "";
    public int BirthYear { get; set; }
    public string? ClubName { get; set; }
}

/// <summary>Результат мутации группы: успех + Id + сообщение об ошибке для формы.</summary>
public sealed record HubGroupSaveResult(bool Success, int Id, string? Error)
{
    public static HubGroupSaveResult Ok(int id) => new(true, id, null);
    public static HubGroupSaveResult Fail(string error) => new(false, 0, error);
}

/// <summary>Результат мутации участника: успех + сообщение об ошибке для формы.</summary>
public sealed record HubGroupMemberSaveResult(bool Success, string? Error)
{
    public static HubGroupMemberSaveResult Ok() => new(true, null);
    public static HubGroupMemberSaveResult Fail(string error) => new(false, error);
}
