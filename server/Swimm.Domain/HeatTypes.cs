namespace Swimm.Domain;

/// <summary>
/// Значения <c>Results.HeatType</c> — НАШ вывод о роли сессии (в отличие от
/// <see cref="ResultRounds"/>, который — факт источника). null — источник провёл дисциплину
/// один раз (timed final), и место в ней официальное.
/// </summary>
public static class HeatTypes
{
    /// <summary>Предварительные: место в них — не награда (правило Р34).</summary>
    public const string Prelim = "prelim";

    /// <summary>Финал: официальные места дисциплины.</summary>
    public const string Final = "final";

    /// <summary>
    /// Заплыв ПОСЛЕ финала: призовые серии на выбывание (skins), переплывы, показательные.
    ///
    /// Отдельный тип, а не «ещё один prelim»: на אליפות הרצליה (01/11/2025) 50 вольным
    /// плыли трижды — прелимы, финал, skins. Пока skins считался прелимом, он попадал с
    /// утренними заплывами в одну «сессию», и проверка качества помечала законные строки
    /// как «повтор дисциплины за день» (docs/data-integrity.md, И-11).
    ///
    /// Мест не даёт и в зачёт не идёт — ведёт себя как <see cref="Prelim"/>; см.
    /// <see cref="GivesOfficialPlace"/>.
    /// </summary>
    public const string Extra = "extra";

    /// <summary>
    /// Даёт ли сессия официальное место дисциплины. Единственное место правила: копия
    /// предиката = будущее расхождение между страницей, зачётом и проверками.
    ///
    /// ⚠ В EF-запросах вызывать НЕЛЬЗЯ (не переводится в SQL) — там сравнение с
    /// константами пишется явно.
    /// </summary>
    public static bool GivesOfficialPlace(string? heatType) =>
        heatType is not (Prelim or Extra);
}
