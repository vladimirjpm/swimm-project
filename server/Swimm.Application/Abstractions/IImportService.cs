using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

public interface IImportService
{
    Task<ImportResult> ImportAsync(Stream jsonStream, string? fileName = null);
    Task<int> EnrichSwimmersFromResultsAsync();
    Task<ClearResult> ClearDataAsync();
    string[] GetClearableTables();
}
