namespace Swimm.Application.Mapping;

/// <summary>
/// Держатель рекорда латиницей — для международных экранов (`/records`, `/records/compare`).
///
/// ⚠ **Это ИСКЛЮЧЕНИЕ из правила «имена на витрине ивритские по умолчанию»**
/// (docs/important.md), а не его отмена. Причина в том, что экран международный: рекорды
/// двух сотен стран приходят из World Aquatics латиницей, и одна ивритская строка посреди
/// рейтинга читается не как «имя на родном языке», а как сбой кодировки. Остальная витрина —
/// страница пловца, протоколы, группы — правилу подчиняется как прежде, и `/api/records`
/// (её источник) эту замену НЕ делает.
///
/// **Перевода здесь нет и быть не может:** латинское имя берётся из карточки нашего же
/// пловца, найденной по ивритскому имени. Не нашли — оставляем как есть. Придумывать
/// транслитерацию нельзя: имя человека не угадывают.
/// </summary>
public static class HolderLatinName
{
    /// <summary>
    /// Есть ли в строке ивритские буквы. Только такие строки и пытаемся заменить: у
    /// остальных стран держатели и так латиницей, и трогать их незачем.
    /// </summary>
    public static bool HasHebrew(string? text)
        => text != null && text.Any(c => c is >= '֐' and <= '׿');

    /// <summary>
    /// Держатель на латинице, если получилось.
    /// </summary>
    /// <param name="holder">Как записано в справочнике; у эстафет — несколько имён через запятую.</param>
    /// <param name="latin">Ивритское имя (обе перестановки слов) → латинское.</param>
    /// <returns>
    /// Строка для показа. Ничего не нашлось — исходная: «наполовину переведённый» состав
    /// эстафеты честнее пустого места, а половина четвёрки, которую мы знаем, читается.
    /// </returns>
    public static string? Resolve(string? holder, IReadOnlyDictionary<string, string> latin)
    {
        if (!HasHebrew(holder)) return holder;

        var parts = holder!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return holder;

        var translated = parts.Select(p => latin.TryGetValue(Normalize(p), out var en) ? en : p);
        return string.Join(", ", translated);
    }

    /// <summary>
    /// Английское имя для строки, которую СЕЙЧАС перезаписывают (`Records.HolderNameEn`,
    /// решение Влада 16.09.2026). Одно правило на все пути записи: импорт (`RecordDiffService`)
    /// и обе админские формы.
    ///
    /// Правило не про источники, а про ТОЖДЕСТВО РЕКОРДА:
    /// 1. новое имя латиницей — оно и есть английское имя ЭТОЙ строки;
    /// 2. иврит поверх, но **время не изменилось** — рекорд тот же, сменился только язык
    ///    записи: прежнее английское имя остаётся;
    /// 3. время изменилось (или строка новая) — это ДРУГОЙ рекорд, прежнее имя к нему не
    ///    относится: сбрасываем.
    ///
    /// Без пункта 3 получилось бы худшее, что может сделать справочник: новый рекорд,
    /// подписанный именем предыдущего держателя. Порядок прогона (И-13) как раз и кладёт
    /// иврит федерации поверх латиницы World Aquatics, так что оба случая встречаются на
    /// каждом `--records-refresh`.
    ///
    /// ⚠ Остаточный риск: если у нового держателя ТО ЖЕ время (ничья), пункт 2 подпишет
    /// строку соавтором рекорда. Случай редкий, а цена ошибки — один из двух равных
    /// держателей вместо другого, не посторонний человек.
    /// </summary>
    /// <param name="existingTime">Время строки ДО перезаписи; null — строка новая.</param>
    public static string? CarryOver(
        string? incomingHolder, string incomingTime, string? existingTime, string? existingHolderNameEn)
    {
        var incoming = (incomingHolder ?? "").Trim();
        if (incoming.Length > 0 && !HasHebrew(incoming)) return incoming;

        return existingTime != null && string.Equals(existingTime, incomingTime, StringComparison.Ordinal)
            ? existingHolderNameEn
            : null;
    }

    /// <summary>
    /// Схлопывает пробелы: в справочнике их бывает по нескольку подряд, а ключ словаря
    /// обязан совпадать буква в букву.
    /// </summary>
    public static string Normalize(string name) =>
        string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries
                                       | StringSplitOptions.TrimEntries));
}
