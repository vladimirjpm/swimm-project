using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// «Сколько рекордов побьёт этот файл» — считается на превью импорта, до «Применить»
/// (docs/data-integrity.md §12, Б2).
///
/// Диагноз, а не запрет: импорт не блокируется, рекорды случаются. Но десяток рекордов
/// разом почти всегда значит, что протокол разобрался неверно, и увидеть это до записи
/// в БД дешевле, чем разбирать инцидент потом.
/// </summary>
public interface IImportRecordPreviewService
{
    /// <summary>
    /// Считает по тому же JSON, который потом уйдёт в <c>ImportAsync</c> — то есть по тем
    /// самым строкам, а не по их пересказу. Не бросает: любая ошибка возвращается в
    /// <see cref="ImportRecordPreviewDto.Error"/>, потому что прибор не имеет права
    /// сорвать превью импорта.
    /// </summary>
    Task<ImportRecordPreviewDto> AnalyzeAsync(string resultsJson, CancellationToken ct = default);
}
