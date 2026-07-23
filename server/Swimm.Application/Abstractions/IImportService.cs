using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

public interface IImportService
{
    /// <param name="categoryKeys">
    /// Ключи категорий, выбранные в UI импорта. Если заданы — имеют приоритет над
    /// <c>categories</c> из JSON-обёртки и применяются ко всем соревнованиям файла.
    /// </param>
    /// <param name="eventOptions">
    /// Привязка к многодневному событию (создать новое / дописать к существующему).
    /// null — обычное однодневное соревнование.
    /// </param>
    /// <param name="orgCompId">compID сайта федерации (только Discovery-импорт) — штампуется в
    /// Competition.OrgCompId «первичного» соревнования импорта для связи Discovery ↔ Competitions.</param>
    Task<ImportResult> ImportAsync(Stream jsonStream, string? fileName = null, IReadOnlyCollection<string>? categoryKeys = null, ImportEventOptions? eventOptions = null, int? orgCompId = null);
    Task<int> EnrichSwimmersFromResultsAsync();
    Task<ClearResult> ClearDataAsync();
    Task<DeleteCompetitionResult?> DeleteCompetitionAsync(int competitionId);

    /// <summary>Удалить многодневное событие целиком: все дни (каскадно) + сам CompetitionEvent.
    /// Возвращает агрегированные счётчики удалённого; null — событие не найдено.</summary>
    Task<DeleteCompetitionResult?> DeleteCompetitionEventAsync(int eventId);
    string[] GetClearableTables();

    /// <summary>
    /// Для превью перед импортом: по каждому дню файла ищет уже существующее в БД соревнование
    /// по ключу Name|Date|PoolType (тот же ключ, что использует ImportAsync). Не требует парсинга —
    /// принимает готовую сводку по дням из ParsedCompetition.Competitions.
    /// </summary>
    Task<List<ExistingCompetitionMatch>> FindExistingCompetitionsAsync(IReadOnlyList<ParsedCompetitionSummary> competitions);
}
