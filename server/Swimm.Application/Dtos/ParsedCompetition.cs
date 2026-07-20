namespace Swimm.Application.Dtos;

/// <summary>
/// Запрос на парсинг для <see cref="Abstractions.IResultSourceProvider"/>.
/// Зеркалит вход PDF-парсеров: основной файл + опциональные вторичный/дополнительные
/// (многодневные протоколы из нескольких PDF).
/// </summary>
public record ResultSourceRequest(
    Stream PrimaryStream,
    string PrimaryFileName,
    string Format = "IsrOrg",
    bool IsAward = false,
    string? PoolType = null,
    Stream? SecondaryStream = null,
    string? SecondaryFileName = null,
    IReadOnlyList<SourceFilePart>? ExtraFiles = null,
    string? Country = null,
    string? Language = null);

/// <summary>Дополнительный файл запроса (третий и далее).</summary>
public record SourceFilePart(Stream Stream, string FileName);

/// <summary>
/// Результат парсинга источника — вход для превью в админке и для импорта.
/// </summary>
public record ParsedCompetition
{
    /// <summary>Формат, которым парсили (IsrOrg и т.п.).</summary>
    public required string Format { get; init; }

    /// <summary>
    /// Результаты в JSON-формате, который принимает <c>IImportService.ImportAsync</c>
    /// (массив result-объектов со snake_case полями — тот же контракт, что у легаси
    /// JSON-загрузки). Совместимость с импортом — по построению, без конвертации.
    /// </summary>
    public required string ResultsJson { get; init; }

    public int ResultCount { get; init; }

    /// <summary>Сводка по соревнованиям внутри файла (для превью перед импортом).</summary>
    public IReadOnlyList<ParsedCompetitionSummary> Competitions { get; init; } = [];

    /// <summary>Строки debug-лога парсера, похожие на предупреждения (для превью).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Полный debug-лог парсера (разворачивается в UI по требованию).</summary>
    public string? DebugLog { get; init; }
}

/// <summary>Одно соревнование из распарсенного файла (файл может содержать несколько дней/дат).</summary>
public record ParsedCompetitionSummary(string Competition, string Date, int ResultCount, string? PoolType = null);
