namespace Swimm.Parsing;

/// <summary>
/// Общие константы для всех парсеров проекта.
/// </summary>
public static class ParserConstants
{
    /// <summary>
    /// Формат даты, используемый во всех парсерах: DD/MM/YYYY.
    /// Литеральные слэши экранированы, чтобы не зависеть от системной культуры.
    /// </summary>
    public const string DateFormat = "dd'/'MM'/'yyyy";
}
