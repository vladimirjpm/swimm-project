namespace Swimm.Application.Dtos;

/// <summary>
/// Общая обёртка «капнутый список» для deep-link выборок дашборда «здоровье данных» (T3b,
/// docs/tasks/dashboard-deeplinks-lists-sonnet.md): топ-200 строк + общий Total (может быть
/// больше Items.Count — «показаны первые 200 из N»).
/// </summary>
public sealed record CappedListDto<T>(int Total, IReadOnlyList<T> Items)
{
    public const int Cap = 200;
}

/// <summary>Строка «пловец без OrgId» / «пловец без результатов» (Admin/Swimmers, секция «Качество данных»).</summary>
public sealed record SwimmerQualityRowDto(int Id, string LastName, string FirstName, int BirthYear, string? ClubName);

/// <summary>Строка «клуб без пловцов» / «клуб без страны» (Admin/Clubs, секция «Качество данных»).</summary>
public sealed record ClubQualityRowDto(int Id, string Name, string NameEn, int? CountryId);

/// <summary>Строка результата с FK-аномалией (несуществующий SwimmerId или ClubId) — сторож,
/// в проде ожидаемо пусто (FK держит целостность), список — на случай ручного вмешательства в БД.</summary>
public sealed record ResultFkAnomalyRowDto(long ResultId, int SwimmerId, int ClubId, int CompetitionId);

/// <summary>Строка эстафеты без единого участника (RelayMember).</summary>
public sealed record EmptyRelayRowDto(int RelayId, int CompetitionId);

/// <summary>Сводка аномалий Results (Admin/Results, секция «Аномалии»): FK + пустые эстафеты, каждая капнута отдельно.</summary>
public sealed record ResultAnomaliesDto(
    CappedListDto<ResultFkAnomalyRowDto> FkAnomalies,
    CappedListDto<EmptyRelayRowDto> EmptyRelays);

/// <summary>Строка заявки на публикацию медиа в ожидании решения админа группы (read-only список,
/// Admin/Media?filter=moderation-pending). Решения принимают админы конкретных групп — здесь только обзор.</summary>
public sealed record ModerationPendingRowDto(
    int PublicationId, string Url, string MediaType, string HubGroupName,
    string? OwnerEmail, DateTime CreatedAt);

/// <summary>Строка заявки на вступление в группу (Admin/HubGroups?tab=requests).</summary>
public sealed record HubGroupJoinRequestRowDto(
    int MemberId, int HubGroupId, string HubGroupName, string? Email, DateTime JoinedAt);
