using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// «Синхронизация языков»: по двуязычной паре PDF (HE+EN, уже распарсенной в results-JSON
/// существующим пайплайном) дозаполняет имена пловцов в БД без переимпорта результатов:
/// пустые LastNameEn/FirstNameEn — из EN-протокола; пловцам, созданным из EN-протокола
/// (английское имя в основных полях), канонизирует имя — HE в основные поля, EN в *En.
/// </summary>
public interface ISwimmerNameSyncService
{
    /// <summary>Применить имена из results-JSON (формат ResultWrap импорта) к таблице Swimmers.</summary>
    Task<SwimmerNameSyncResult> SyncFromResultsJsonAsync(string resultsJson, CancellationToken ct = default);
}
