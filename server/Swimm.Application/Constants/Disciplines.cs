namespace Swimm.Application.Constants;

/// <summary>
/// Вид спорта соревнования. Федерация проводит под одной крышей не только плавание:
/// во входящих с isr.org.il ~6% — артистическое плавание (שחייה אומנותית), где нет ни
/// времён, ни дистанций, и нам они не нужны.
///
/// Признак ХРАНИТСЯ, а не выводится из названия при каждом показе (решение Влада
/// 2026-08-09): иначе правило живёт во вьюхе, новая дисциплина требует ещё одной
/// подстроки в разметке, а промах распознавания нельзя поправить на одной строке.
/// Скрываем фильтром с дефолтом, но не удаляем — понадобится, поменяем дефолт.
/// </summary>
public static class Disciplines
{
    /// <summary>Обычное плавание — дефолт: всё, что не распознано иначе.</summary>
    public const string Swimming = "swimming";

    /// <summary>Артистическое (синхронное) плавание.</summary>
    public const string Artistic = "artistic";

    /// <summary>Прочее (водное поло, прыжки в воду…) — заводим руками, эвристики нет.</summary>
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All = new[] { Swimming, Artistic, Other };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);

    /// <summary>
    /// Догадка по названию — одна на импорт и автозабор, чтобы правило не разъехалось.
    ///
    /// Ивритское «אומנותית» пишут и через алеф-вав, и без него (אמנותית), плюс встречается
    /// огласованная форма; поэтому сравниваем по корню «מנותית» вместе со словом «שחי…».
    /// Английские названия тоже ловим: в двуязычных выгрузках это «artistic swimming».
    /// </summary>
    public static string GuessFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Swimming;

        if (name.Contains("מנותית", StringComparison.Ordinal)
            || name.Contains("artistic", StringComparison.OrdinalIgnoreCase)
            || name.Contains("synchro", StringComparison.OrdinalIgnoreCase))
        {
            return Artistic;
        }

        return Swimming;
    }
}
