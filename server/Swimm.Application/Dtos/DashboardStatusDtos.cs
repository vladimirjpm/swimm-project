namespace Swimm.Application.Dtos;

/// <summary>Сводный статус данных для дашборда /Admin (docs/plans/admin-dashboard-health-2-plan.md):
/// блоки пловцы/клубы/соревнования/результаты/рекорды/медиа/юзеры-группы/система. Один запрос
/// вместо кучи с клиента; кэшируется целиком на 2 минуты (см. DashboardStatusService).</summary>
public sealed record DashboardStatusSummary(
    DashboardSwimmerStatus Swimmers,
    DashboardClubStatus Clubs,
    DashboardCompetitionStatus Competitions,
    DashboardResultStatus Results,
    IReadOnlyList<DashboardRecordSetStatus> RecordSets,
    DashboardMediaStatus Media,
    DashboardUsersGroupsStatus UsersGroups,
    DashboardSystemStatus System);

/// <summary>Блок «Пловцы»: происхождение записей, дедуп-кандидаты, привязка Loglig ID,
/// пловцы без результатов/OrgId.</summary>
public sealed record DashboardSwimmerStatus(
    int Total, int OriginIsr, int OriginLocal, int Synthetic,
    int SureCandidates, int UnsureCandidates, int Orphans,
    /// <summary>Origin == "isr" && SwimmerOrgId == null.</summary>
    int NoOrgId,
    /// <summary>Нет ни одной строки в Results, ни в RelayMembers (синтетика исключена).</summary>
    int NoResults,
    DashboardLogligStatus Loglig);

/// <summary>Блок «Клубы»: дедуп-кандидаты, клубы без пловцов/страны, заявки на официальный статус.</summary>
public sealed record DashboardClubStatus(
    /// <summary>Всего клубов; Real = Total - Pseudo, отдельного поля не нужно.</summary>
    int Total, int Pseudo,
    int SureCandidates, int UnsureCandidates,
    int NoSwimmers, int NoCountry,
    /// <summary>HubGroupClubRequests.Status == "pending".</summary>
    int ClubRequestsPending);

/// <summary>Блок «Соревнования»: заполненность результатами, разбор Discovery.</summary>
public sealed record DashboardCompetitionStatus(
    /// <summary>Всего соревнований; Empty = Total - WithResults.</summary>
    int Total, int WithResults,
    int DiscoveryImported, int DiscoveryNew, int DiscoveryIgnored,
    /// <summary>DiscoveredCompetition.LastError != null.</summary>
    int DiscoveryErrors,
    /// <summary>Competition.OrgCompId == null.</summary>
    int NoOrgCompId,
    /// <summary>
    /// Дубли клубного зачёта: два чемпионата одной зачётной группы одного вида (❄/☀)
    /// в одном сезоне. В реальности такого не бывает — если счётчик не ноль, ошибка не в
    /// зачёте, а во флагах самого соревнования (IsChampionship / PoolType).
    /// См. docs/plans/club-page-model.md §2.1.
    /// </summary>
    int DuplicateStandings);

/// <summary>Блок «Результаты»: провалы времени, FK-аномалии (сторож), эстафеты без состава.</summary>
public sealed record DashboardResultStatus(
    int Total, int TimeFail,
    int FkAnomalies, int EmptyRelays);

/// <summary>Один набор рекордов (группировка по территории) — сколько записей и когда последний
/// раз обновляли. Порог «устарело» сервер не считает — это фронт (T2).</summary>
public sealed record DashboardRecordSetStatus(
    string RegionType, string RegionCode, int Count, DateTime LastUpdatedAt);

/// <summary>Здоровье ссылок UserMedia (фаза 7.5) + разбивка по типу и модерация публикаций.</summary>
public sealed record DashboardMediaStatus(
    /// <summary>По MediaType; прочие типы = Total - Video - Photo.</summary>
    int Total, int Video, int Photo,
    int Broken, int Unchecked,
    /// <summary>UserMediaPublications.Status == "pending".</summary>
    int ModerationPending);

/// <summary>Блок «Пользователи/группы».</summary>
public sealed record DashboardUsersGroupsStatus(
    int UsersTotal, int Active7d, int Deactivated,
    int GroupsTotal, int GroupsOfficial,
    /// <summary>HubGroupUserMembers.Status == "pending".</summary>
    int JoinRequestsPending);

/// <summary>Блок «Система»: последний импорт, последняя проверка медиа-ссылок, последний
/// автозабор Discovery, активность в аудите за 7 дней.</summary>
public sealed record DashboardSystemStatus(
    DateTime? LastImportAt, bool? LastImportApproved,
    DateTime? LastMediaCheckAt,
    DateTime? LastDiscoverySeenAt,
    /// <summary>Кол-во строк AdminAudit за последние 7 дней (уровня «ошибка» в аудите нет —
    /// считаются все действия).</summary>
    int AuditActions7d);

/// <summary>Привязка Loglig ID (docs/loglig-id-plan.md) по статусам.</summary>
public sealed record DashboardLogligStatus(int Verified, int Suggested, int Rejected, int Unlinked);
