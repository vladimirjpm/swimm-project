using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Текущий клуб в карточке пловца (<c>Swimmers.ClubId</c>) — Р95, Ф1
/// docs/plans/swimmer-club-change-plan.md. Правило — <c>SwimmerCurrentClubRule</c> (клуб последнего
/// соревнования). С Р98 (18.11.2026) пересчёт карточку САМ НЕ МЕНЯЕТ: он заводит кандидата
/// (<c>Sys_SwimmerClubCandidates</c>), а применяет его только сверка с loglig
/// (<see cref="ISwimmerClubLogligCheckService"/>). Каждая смена пишется в журнал
/// <c>Sys_SwimmerClubChanges</c>; закреплённого (<c>Swimmers.ClubLocked</c>) пересчёт не трогает.
/// Флаг <c>Swimmers.ClubFromMasters</c> (Р108): пересчёт сам ставит его пловцам с мастерс-стартами, и тогда
/// кандидат — клуб последнего мастерс-соревнования.
///
/// Права здесь не проверяются: вызывающий — импорт, CLI или админ-контроллер (Admin).
/// </summary>
public interface ISwimmerClubRefreshService
{
    /// <summary>
    /// Пересчитать клуб у <paramref name="swimmerIds"/> (null — у всех) и завести/обновить кандидатов
    /// на смену. Ждущий кандидат снимается, если правило снова даёт клуб карточки (следующий старт —
    /// за клуб из карточки), пловца закрепили или в последнем соревновании ничья.
    /// <paramref name="apply"/> = false — только план (dry-run), ничего не пишет.
    /// <paramref name="source"/> — import | cli (<c>SwimmerClubChangeSources</c>), уходит в кандидата.
    /// Импорт будит фоновую сверку (<see cref="ISwimmerClubCheckSignal"/>).
    /// </summary>
    Task<SwimmerClubRefreshReport> RefreshAsync(
        IReadOnlyCollection<int>? swimmerIds, string source, bool apply, CancellationToken ct = default);

    /// <summary>Блок «Клуб пловца» админки; null — пловца нет.</summary>
    Task<SwimmerClubAdminDto?> GetForAdminAsync(int swimmerId, CancellationToken ct = default);

    /// <summary>
    /// Ручная правка: клуб (null — очистить) и закрепление. Склеенный клуб подменяется приёмником,
    /// псевдоклуб отклоняется. Смена клуба пишет журнал с источником admin; аудит пишет контроллер.
    /// Ждущий кандидат пловца снимается: человек решил сам. <paramref name="clubFromMasters"/> — флаг
    /// «клуб по мастерс-стартам» (Р108): true / false ставит его явно (снятый автоматика не вернёт),
    /// null — не трогать.
    /// </summary>
    Task<(SwimmerClubSetResult? Result, SwimmerClubSetError Error)> SetByAdminAsync(
        int swimmerId, int? clubId, bool locked, bool? clubFromMasters = null, CancellationToken ct = default);

    /// <summary>
    /// Флаг «клуб по мастерс-стартам» (Р108): у скольких стоит и первые <paramref name="take"/> пловцов,
    /// у которых есть ещё и обычные старты за другой клуб. Для дашборда.
    /// </summary>
    Task<SwimmerClubMastersSummary> MastersAsync(int take, CancellationToken ct = default);

    /// <summary>Смены из журнала за <paramref name="days"/> дней, новые сверху, не больше <paramref name="take"/>.</summary>
    Task<IReadOnlyList<SwimmerClubChangeDto>> RecentChangesAsync(int days, int take, CancellationToken ct = default);

    /// <summary>
    /// Ждущие кандидаты: счёт по вердиктам и до <paramref name="takePerVerdict"/> строк каждого
    /// (новые сверху). Для дашборда и /Admin/Swimmers.
    /// </summary>
    Task<SwimmerClubPendingSummary> PendingAsync(int takePerVerdict, CancellationToken ct = default);
}
