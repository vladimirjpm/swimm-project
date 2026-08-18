using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Сборный ответ страницы клуба (K4.1): Hero, фильтры, грид «сезон × группа», таблица
/// выбранного зачёта, история и топ пловцов — одним запросом, чтобы первая отрисовка
/// не била в API семь раз. Ростер и рекорды догружаются отдельно
/// (<see cref="IClubPublicRepository"/>): они пагинируемые и у рекордов свой фильтр бассейна.
/// </summary>
public interface IClubOverviewRepository
{
    /// <param name="resolvedClubId">Клуб после резолва merge/псевдоклуба.</param>
    /// <param name="requestedId">Id из URL — чтобы клиент знал, что его переадресовали.</param>
    /// <param name="season">Год начала сезона; null — все сезоны.</param>
    /// <param name="groupKey">Ключ зачётной группы (<c>Category.Key</c>); null — все группы.</param>
    /// <param name="gridSeasons">Сколько сезонов показывает грид при «все сезоны».</param>
    /// <param name="standingCompetitionId">Какой зачёт раскрыть таблицей; null — самый свежий в скоупе.</param>
    Task<ClubOverviewDto?> GetOverviewAsync(
        int resolvedClubId,
        int requestedId,
        int? season,
        string? groupKey,
        int gridSeasons,
        int? standingCompetitionId);
}
