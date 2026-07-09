using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Источник результатов соревнований: превращает внешние данные (PDF-протокол, HTML со
/// страницы федерации, …) в <see cref="ParsedCompetition"/>. Провайдер только парсит —
/// в БД НИКОГДА не пишет; единственная точка записи — <see cref="IImportService"/>,
/// которому скармливается <see cref="ParsedCompetition.ResultsJson"/>.
/// Новый источник данных = новая реализация + регистрация в DI, импорт и админка не меняются.
/// </summary>
public interface IResultSourceProvider
{
    /// <summary>Имя источника для выбора в UI/DI (например "pdf").</summary>
    string SourceName { get; }

    /// <summary>Поддерживаемые форматы протоколов (IsrOrg, WorldRecords, …).</summary>
    IReadOnlyList<string> AvailableFormats { get; }

    /// <summary>
    /// Распарсить файлы источника. Ошибка формата/битый файл — исключение
    /// <see cref="InvalidOperationException"/> с человекочитаемым сообщением (для UI превью).
    /// </summary>
    Task<ParsedCompetition> ParseAsync(ResultSourceRequest request, CancellationToken ct = default);
}
