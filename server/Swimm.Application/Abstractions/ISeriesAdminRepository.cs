using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Порт админки серий турниров (/Admin/Series): серия, части, версии регламента с наградами и
/// привязка дней соревнований к частям (docs/plans/official-awards-plan.md §5.7).
/// </summary>
public interface ISeriesAdminRepository
{
    Task<IReadOnlyList<SeriesRowDto>> GetAllAsync();

    /// <summary>Серия с частями, регламентами, наградами и выпусками. null — не найдена.</summary>
    Task<SeriesEditDto?> GetByIdAsync(int id);

    Task<SeriesSaveResult> CreateSeriesAsync(SeriesInputDto input);
    Task<SeriesSaveResult> UpdateSeriesAsync(int id, SeriesInputDto input);

    /// <summary>Удалить серию целиком (части, регламенты, награды); дни отвязываются.</summary>
    Task<SeriesSaveResult> DeleteSeriesAsync(int id);

    Task<SeriesSaveResult> AddPartAsync(int seriesId, SeriesPartInputDto input);
    Task<SeriesSaveResult> UpdatePartAsync(int partId, SeriesPartInputDto input);

    /// <summary>Удалить часть: её награды уходят, дни отвязываются.</summary>
    Task<SeriesSaveResult> DeletePartAsync(int partId);

    Task<SeriesSaveResult> AddRegulationAsync(int seriesId, SeriesRegulationInputDto input);
    Task<SeriesSaveResult> UpdateRegulationAsync(int regulationId, SeriesRegulationInputDto input);

    /// <summary>Новая версия регламента — копия наград указанной, с другой датой вступления.</summary>
    Task<SeriesSaveResult> CloneRegulationAsync(int regulationId, SeriesRegulationInputDto input);

    Task<SeriesSaveResult> DeleteRegulationAsync(int regulationId);

    Task<SeriesSaveResult> AddAwardAsync(int regulationId, SeriesAwardInputDto input);

    /// <summary>Обновить награду; полосы и призы перезаписываются целиком.</summary>
    Task<SeriesSaveResult> UpdateAwardAsync(int awardId, SeriesAwardInputDto input);

    Task<SeriesSaveResult> DeleteAwardAsync(int awardId);

    /// <summary>
    /// Привязать день к части серии (null — отвязать). Смена части снимает отметку
    /// «регламент подтверждён»: подтверждали другое.
    /// </summary>
    Task<SeriesSaveResult> SetCompetitionPartAsync(int competitionId, int? partId);

    /// <summary>
    /// Поставить/снять отметку «регламент этого выпуска подтверждён» (§5.8, шаг 4). У дня из
    /// события — всем его дням той же серии. В Id результата — 1, если отметка теперь стоит.
    /// </summary>
    Task<SeriesSaveResult> ToggleRegulationCheckedAsync(int competitionId, string? user);

    /// <summary>Все части всех серий — для выпадающего списка на /Admin/Competitions.</summary>
    Task<IReadOnlyList<SeriesPartOptionDto>> GetPartOptionsAsync();

    /// <summary>Часть серии, к которой привязан день; null — не из серии.</summary>
    Task<int?> GetCompetitionPartIdAsync(int competitionId);
}
