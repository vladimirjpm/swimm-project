using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

public interface IResultRepository
{
    /// <summary>Страница результатов под фильтром. Total — общее число строк под тем же фильтром (без пагинации).</summary>
    Task<(List<ResultDto> Items, bool HasMore, int Total)> GetPagedAsync(ResultFilter filter, int page, int pageSize);
    Task<ResultDto?> GetByIdAsync(long id);
    Task<string[]> GetFilterHintsAsync(string field, string? q, int limit);
    /// <summary>Список источников для DDL: события (свёрнуты в одну запись) + однодневные соревнования.
    /// country — alpha-3 страны соревнования (опционально); для события — совпадение хотя бы одного дня.</summary>
    Task<IReadOnlyList<CompetitionSourceDto>> GetSourcesAsync(string? country = null);
    /// <summary>Карьерные (all-time) данные спортсмена по полному имени; null — пловец не найден.</summary>
    Task<AthleteCareerDto?> GetAthleteCareerAsync(string name);
}
