namespace Swimm.Application.Validation;

/// <summary>
/// Сопоставление имён между источниками, где порядок и полнота имени не совпадают.
///
/// Две беды, из-за которых прямое сравнение строк не работает:
/// <list type="number">
/// <item>порядок токенов разный — сайт печатает «имя фамилия», протокол и база держат поля
/// раздельно и часто наоборот;</item>
/// <item>полнота разная — на loglig «אליה מאשה גדול», у нас «אליה גדול»: двойное имя
/// напечатано не везде. Живой случай, из-за которого правило и появилось.</item>
/// </list>
///
/// Поэтому: ключ — НАБОР нормализованных токенов + год рождения, а если точного совпадения
/// нет, годится кандидат, чьи токены являются надмножеством (или подмножеством) наших при том
/// же годе — и только если такой ОДИН. Двое подходящих значит «не знаем»: привязать не тому
/// хуже, чем не привязать (тёзки — известная боль проекта).
///
/// Нормализация приходит извне (у дедупа она своя, с ивритскими финальными буквами и герешем),
/// чтобы в проекте не завелось второй таблицы правил.
/// </summary>
public static class TokenNameMatcher
{
    /// <summary>Ключ точного совпадения: отсортированные токены + год.</summary>
    public static string Key(IEnumerable<string> normalizedTokens, int? birthYear) =>
        string.Join('|', normalizedTokens.OrderBy(t => t, StringComparer.Ordinal))
        + "#" + (birthYear?.ToString() ?? "?");

    /// <summary>
    /// Найти единственного кандидата для имени. <paramref name="candidates"/> — уже
    /// нормализованные наборы токенов с годом и полезной нагрузкой.
    /// </summary>
    /// <returns>Значение кандидата либо default, если совпадения нет или их несколько.</returns>
    public static T? ResolveSingle<T>(
        IReadOnlyList<(IReadOnlyCollection<string> Tokens, int? BirthYear, T Value)> candidates,
        IReadOnlyCollection<string> tokens,
        int? birthYear)
    {
        var exact = candidates
            .Where(c => c.BirthYear == birthYear && c.Tokens.Count == tokens.Count
                        && c.Tokens.All(tokens.Contains))
            .ToList();
        if (exact.Count == 1) return exact[0].Value;
        if (exact.Count > 1) return default;

        // Одно имя полнее другого: «אליה מאשה גדול» ⊃ «אליה גדול». Год обязателен — без него
        // подмножество токенов слишком слабый признак.
        if (birthYear is null) return default;

        var partial = candidates
            .Where(c => c.BirthYear == birthYear
                        && (c.Tokens.All(tokens.Contains) || tokens.All(c.Tokens.Contains)))
            .ToList();

        return partial.Count == 1 ? partial[0].Value : default;
    }
}
