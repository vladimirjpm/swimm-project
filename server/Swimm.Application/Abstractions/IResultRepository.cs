using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

public interface IResultRepository
{
    Task<(List<ResultDto> Items, bool HasMore)> GetPagedAsync(ResultFilter filter, int page, int pageSize);
    Task<ResultDto?> GetByIdAsync(long id);
    Task<string[]> GetFilterHintsAsync(string field, string? q, int limit);
    /// <summary>Список источников для DDL: события (свёрнуты в одну запись) + однодневные соревнования.</summary>
    Task<IReadOnlyList<CompetitionSourceDto>> GetSourcesAsync();
    /// <summary>Карьерные (all-time) данные спортсмена по полному имени; null — пловец не найден.</summary>
    Task<AthleteCareerDto?> GetAthleteCareerAsync(string name);
}
