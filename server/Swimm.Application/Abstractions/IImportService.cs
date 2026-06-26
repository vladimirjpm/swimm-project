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
    Task<ImportResult> ImportAsync(Stream jsonStream, string? fileName = null, IReadOnlyCollection<string>? categoryKeys = null, ImportEventOptions? eventOptions = null);
    Task<int> EnrichSwimmersFromResultsAsync();
    Task<ClearResult> ClearDataAsync();
    Task<DeleteCompetitionResult?> DeleteCompetitionAsync(int competitionId);
    string[] GetClearableTables();
}
