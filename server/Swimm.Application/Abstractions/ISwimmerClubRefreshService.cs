using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Текущий клуб в карточке пловца (<c>Swimmers.ClubId</c>) — Р94, Ф1
/// docs/plans/swimmer-club-change-plan.md. Правило — <c>SwimmerCurrentClubRule</c> (клуб последнего
/// соревнования). Каждая смена пишется в журнал <c>Sys_SwimmerClubChanges</c>; закреплённого
/// (<c>Swimmers.ClubLocked</c>) пересчёт не трогает.
///
/// Права здесь не проверяются: вызывающий — импорт, CLI или админ-контроллер (Admin).
/// </summary>
public interface ISwimmerClubRefreshService
{
    /// <summary>
    /// Пересчитать клуб у <paramref name="swimmerIds"/> (null — у всех). <paramref name="apply"/> =
    /// false — только план (dry-run), ничего не пишет. <paramref name="source"/> — import | cli
    /// (<c>SwimmerClubChangeSources</c>), уходит в журнал.
    /// </summary>
    Task<SwimmerClubRefreshReport> RefreshAsync(
        IReadOnlyCollection<int>? swimmerIds, string source, bool apply, CancellationToken ct = default);

    /// <summary>Блок «Клуб пловца» админки; null — пловца нет.</summary>
    Task<SwimmerClubAdminDto?> GetForAdminAsync(int swimmerId, CancellationToken ct = default);

    /// <summary>
    /// Ручная правка: клуб (null — очистить) и закрепление. Склеенный клуб подменяется приёмником,
    /// псевдоклуб отклоняется. Смена клуба пишет журнал с источником admin; аудит пишет контроллер.
    /// </summary>
    Task<(SwimmerClubSetResult? Result, SwimmerClubSetError Error)> SetByAdminAsync(
        int swimmerId, int? clubId, bool locked, CancellationToken ct = default);

    /// <summary>Смены из журнала за <paramref name="days"/> дней, новые сверху, не больше <paramref name="take"/>.</summary>
    Task<IReadOnlyList<SwimmerClubChangeDto>> RecentChangesAsync(int days, int take, CancellationToken ct = default);
}
