namespace Swimm.Application.Constants;

/// <summary>
/// Канонические ключи стилей — то, чем стиль называется у нас, независимо от языка протокола.
///
/// Справочник <c>Styles</c> наполняет ИМПОРТ, и туда попадает ровно то, что отдал парсер.
/// Пока стиль искался точным совпадением по ивритскому словарю, заголовок с лишним словом
/// («3000 מטר חופשי» — «3000 МЕТРОВ вольным») заводил отдельный ключ `מטר_חופשי`, и целое
/// соревнование выпадало из витрин, которые показывают только канонические стили
/// (docs/data-integrity.md §9, решения 2026-08-26).
///
/// Порядок в <see cref="All"/> — порядок показа в селекторе дисциплины, не алфавит.
/// </summary>
public static class Strokes
{
    public const string Freestyle = "freestyle";
    public const string Backstroke = "backstroke";
    public const string Breaststroke = "breaststroke";
    public const string Butterfly = "butterfly";
    public const string IndividualMedley = "individual_medley";

    /// <summary>Массив (а не список) намеренно: используется внутри EF-запросов как IN (...).</summary>
    public static readonly string[] All =
        [Freestyle, Backstroke, Breaststroke, Butterfly, IndividualMedley];

    public static bool IsCanonical(string? name) => name is not null && All.Contains(name);
}
