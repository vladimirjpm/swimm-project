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
    RecordQualitySummary RecordQuality,
    DashboardMediaStatus Media,
    DashboardUsersGroupsStatus UsersGroups,
    DashboardSystemStatus System,
    DashboardChecksStatus Checks,
    DashboardClubPointsStatus ClubPoints);

/// <summary>
/// Блок «Клубные очки»: где наш зачёт разошёлся с официальным и признано, что верны мы
/// (<c>ClubPointsVerifiedKind = mismatch</c>, бейдж «★ расхождение» на витрине).
///
/// Зачем отдельный блок. Такое соревнование — единственное место, где сайт ПУБЛИЧНО спорит
/// с федерацией, и цена ошибки тут выше обычной: если разбор неверен, мы обвиняем
/// организатора зря. Поэтому их видно на дашборде поимённо, а не одним счётчиком.
///
/// Счёт идёт по СОБЫТИЯМ, а не по дням: у многодневки отметка стоит на каждом дне, и
/// «4 расхождения» вместо одного чемпионата пугали бы на пустом месте.
/// </summary>
/// <param name="MismatchWithoutNote">
/// Из них без пояснения. Бейдж без объяснения — утверждение без доказательства: читатель
/// видит «официальные очки неверны» и не может проверить. Такие чинятся в первую очередь.
/// </param>
public sealed record DashboardClubPointsStatus(
    int MismatchEvents,
    int MismatchWithoutNote,
    /// <summary>Сверено с официальным протоколом, цифры совпали.</summary>
    int VerifiedEvents,
    /// <summary>Официальных очков не публиковали; проверено самостоятельно и принято.</summary>
    int AcceptedEvents,
    /// <summary>Правило привязано, но глазами никто не сверял.</summary>
    int UncheckedEvents,
    /// <summary>Правила клубных очков нет вовсе — зачёт такого соревнования пуст.</summary>
    int NoRuleEvents,
    /// <summary>Сами расхождения, по одной строке на событие — для панели деталей.</summary>
    IReadOnlyList<DashboardClubPointsLine> Mismatches);

/// <summary>Одно соревнование с расхождением: наши очки против официальных.</summary>
/// <param name="OfficialPoints">
/// null — построчного эталона у соревнования нет (он приезжает только из пособытийного
/// источника loglig). Тогда величину расхождения знает лишь пояснение, и сравнивать нечего.
/// </param>
/// <param name="MismatchedRows">Строк, где наш расчёт разошёлся с эталоном; null без эталона.</param>
public sealed record DashboardClubPointsLine(
    int CompetitionId, string Name, string Date,
    int OurPoints, int? OfficialPoints, int? MismatchedRows, bool HasNote);

/// <summary>
/// Блок «Проверки данных» — сводка реестра (docs/data-integrity.md, Д3). Дашборд НЕ гоняет
/// проверки сам: он показывает итог последнего прогона и ведёт на /Admin/Health.
/// </summary>
/// <param name="LastRunAt">null — реестр ни разу не гонялся; тогда нули ничего не значат
/// и страница обязана сказать «прогонов не было», а не «всё чисто».</param>
public sealed record DashboardChecksStatus(
    DateTime? LastRunAt, string? LastRunTrigger,
    int Errors, int Warnings, int Infos,
    /// <summary>Проверок, упавших на последнем прогоне: их числа недостоверны.</summary>
    int FailedChecks,
    /// <summary>Проверки с ненулевым результатом, тяжёлые первыми — для панели деталей.</summary>
    IReadOnlyList<DashboardCheckLine> Lines);

/// <summary>Строка сводки: одна проверка и сколько она нашла на последнем прогоне.</summary>
public sealed record DashboardCheckLine(
    string CheckId, DataCheckSeverity Severity, int Total, bool Failed);

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
