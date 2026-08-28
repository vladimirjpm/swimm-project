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
    /// Поиск пловца по имени внутри соревнования — «когда плывёт мой ребёнок», если его
    /// нет в избранных. Ищет по НЕСКОЛЬКИМ compID сразу: один наш старт бывает собран из
    /// нескольких протоколов федерации (окружные чемпионаты), и родителю всё равно, в
    /// каком из них его пловец.
    ///
    /// Ищет и по английскому имени, и по ивритскому: у пловцов, заведённых стартовым
    /// протоколом, английского имени ещё нет, а ищут их именно те, кто читает иврит.
    /// </summary>
    Task<IReadOnlyList<StartListSwimmerHitDto>> SearchSwimmersAsync(
        IReadOnlyCollection<int> orgCompIds, string query, int limit, CancellationToken ct = default);

    /// <summary>
    /// Все заплывы пловца по НЕСКОЛЬКИМ источникам сразу (карточка «когда плывёт мой» на
    /// соревновании из нескольких протоколов). null — заявок нет ни в одном.
    /// </summary>
    Task<StartListSwimmerDto?> GetSwimmerAcrossAsync(
        IReadOnlyCollection<int> orgCompIds, int swimmerId, CancellationToken ct = default);

    /// <summary>
    /// Ближайшие старты нескольких пловцов — основа блока «мои избранные» (шаг С8).
    /// Отдаёт только то, что ещё впереди относительно <paramref name="from"/>.
    /// </summary>
    Task<IReadOnlyList<StartListSwimDto>> GetUpcomingAsync(
        IReadOnlyCollection<int> swimmerIds, DateTime from, int days, CancellationToken ct = default);
}
