using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Публичный read-путь стартового протокола (docs/plans/start-list-plan.md, шаг С6).
/// Три уровня приближения одного и того же таба: программа дня → заплыв → карточка пловца.
///
/// Логина не требует: стартовый протокол публичен так же, как результаты. Ходит через
/// <c>SwimmReadDbContext</c> (роль swimm_ro), поэтому журнал заборов
/// (<c>Sys_StartListPulls</c>) ему недоступен — «когда обновлено» берётся из самих заявок.
/// </summary>
public interface IStartListPublicRepository
{
    /// <summary>
    /// Есть ли вообще заявки под этот срез. Дешёвая проверка ДО кэшируемой загрузки —
    /// иначе ради 404 пришлось бы тянуть весь payload мимо кэша (приём
    /// <c>ClubsPublicController</c>).
    /// </summary>
    Task<bool> ExistsAsync(
        int orgCompId, int? orgDisciplineId = null, int? swimmerId = null, CancellationToken ct = default);

    /// <summary>
    /// Предстоящие соревнования для общего списка `/competitions` (решение В9).
    /// Строится по заявкам: у будущего старта своей строки в <c>Competitions</c> ещё нет,
    /// а «Входящие» публичному пути недоступны.
    /// </summary>
    Task<IReadOnlyList<UpcomingCompetitionDto>> GetUpcomingCompetitionsAsync(
        DateTime from, int days, CancellationToken ct = default);

    /// <summary>Программа соревнования по времени (зум 1). null — заявок нет вовсе.</summary>
    Task<StartListProgrammeDto?> GetProgrammeAsync(int orgCompId, CancellationToken ct = default);

    /// <summary>Дисциплина с разбивкой по заплывам и дорожкам (зум 2).</summary>
    Task<StartListEventHeatsDto?> GetEventAsync(
        int orgCompId, int orgDisciplineId, CancellationToken ct = default);

    /// <summary>Все заплывы пловца на этом соревновании (зум 3).</summary>
    Task<StartListSwimmerDto?> GetSwimmerAsync(
        int orgCompId, int swimmerId, CancellationToken ct = default);

    /// <summary>Все заплывы клуба на этом соревновании — срез «кто из наших плывёт».</summary>
    Task<IReadOnlyList<StartListSwimDto>> GetClubSwimsAsync(
        int orgCompId, int clubId, CancellationToken ct = default);

    /// <summary>
    /// Ближайшие старты нескольких пловцов — основа блока «мои избранные» (шаг С8).
    /// Отдаёт только то, что ещё впереди относительно <paramref name="from"/>.
    /// </summary>
    Task<IReadOnlyList<StartListSwimDto>> GetUpcomingAsync(
        IReadOnlyCollection<int> swimmerIds, DateTime from, int days, CancellationToken ct = default);
}
