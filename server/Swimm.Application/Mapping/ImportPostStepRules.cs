using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Когда после импорта запускать пост-шаги (статус «imported» строке входящих, проба клубного
/// зачёта, доклейка промежуточных, штамповка loglig-id).
///
/// <c>ImportAsync</c> «завершается» и отказом: «Дубль», Р119 (свёрнутый протокол), пустой JSON,
/// откат на ошибках строк — все с нулём записанных строк. Пост-шаги после такого врут
/// («✓ имп.» у строки, которой нет в базе) и зря ходят на loglig (ревью PR #271/#272).
/// </summary>
public static class ImportPostStepRules
{
    /// <summary>Импорт что-то записал: создал строки или обновил их (переимпорт upsert).</summary>
    public static bool Succeeded(ImportResult result) => result.Created > 0 || result.Updated > 0;
}
