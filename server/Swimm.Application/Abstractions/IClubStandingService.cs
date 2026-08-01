namespace Swimm.Application.Abstractions;

/// <summary>
/// Пересчёт материализованного клубного зачёта (<c>ClubCompetitionStandings</c>) —
/// места клубов, очки и медали по соревнованиям.
///
/// Таблица производная: её обязан пересчитывать каждый, кто меняет исходные данные —
/// импорт, правка результата, смена привязанного правила очков, merge клубов. Иначе она
/// расходится с результатами молча, а расхождение в местах клубов заметить почти
/// невозможно. Поэтому вызовы висят на том же шве, что и
/// <see cref="ICompetitionRecalculationService"/>.
/// </summary>
public interface IClubStandingService
{
    /// <summary>
    /// Пересчитывает зачёт соревнования. Для дня многодневного события пересчитывается
    /// ВСЁ событие: зачётная единица — событие целиком, строка в таблице одна.
    /// Возвращает число строк зачёта (клубов).
    /// </summary>
    Task<int> RebuildForCompetitionAsync(int competitionId, CancellationToken ct = default);

    /// <summary>Пересчитывает зачёты всех соревнований — бэкфилл и разовые прогоны.</summary>
    Task<int> RebuildAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Пересчитывает зачёты всех соревнований, где выступал клуб. Нужен после merge:
    /// склеенный клуб исчезает из выборок, и его места обязаны уйти вместе с ним.
    /// </summary>
    Task<int> RebuildForClubAsync(int clubId, CancellationToken ct = default);
}
