namespace Swimm.Infrastructure.Services;

/// <summary>Пловец соревнования, как он уже лежит в БД, — кандидат на сопоставление.</summary>
public sealed record KnownSwimmerName(string LastName, string FirstName, int BirthYear, string Club);

/// <summary>Итог сопоставления имени из источника с пловцом в БД.</summary>
/// <param name="Matched">
/// false — пары не нашлось, поля разрезаны эвристикой. Импортировать такую строку —
/// значит завести нового пловца, поэтому неопознанные обязаны попадать в отчёт.
/// </param>
public sealed record ResolvedSwimmerName(string LastName, string FirstName, bool Matched);

/// <summary>
/// Сопоставление имени из пособытийного источника loglig с пловцом, уже импортированным по
/// этому соревнованию (шаг 3, docs/data-integrity.md §10).
///
/// Зачем. Сайт печатает имя ОДНОЙ ячейкой в порядке «имя фамилия», у нас поля раздельные,
/// а порядок токенов в источниках не совпадает (PDF отдаёт «фамилия имя»). Разрезать вслепую
/// нельзя: «לי חן עובדיה» — это фамилия «עובדיה» и двойное имя «לי חן», а наивное «первый
/// токен = имя» дало бы нового пловца-двойника вместо существующего.
///
/// Правила, по убыванию надёжности:
/// <list type="number">
/// <item>тот же НАБОР токенов и год рождения — точная пара;</item>
/// <item>токены базы ⊆ токенов источника (или наоборот) при том же годе — у сайта имя бывает
/// полнее, чем в PDF: «אבינעם יצחק גבאי» против «אבינעם גבאי». Берётся только если такой
/// кандидат ОДИН; при нескольких — дополнительно по клубу;</item>
/// <item>не нашлось — режем эвристикой (последний токен = фамилия) и честно помечаем
/// <c>Matched = false</c>.</item>
/// </list>
///
/// Апострофы приводятся к ивритскому гершу: PDF и веб пишут «אנג׳לה» разными символами
/// (U+05F3, ASCII, типографские), и без этого 25 имён из 39 «терялись» на пустом месте.
/// </summary>
public sealed class LogligSwimmerNameResolver
{
    private readonly Dictionary<string, List<KnownSwimmerName>> _byExactKey = [];
    private readonly List<(HashSet<string> Tokens, KnownSwimmerName Swimmer)> _all = [];

    public LogligSwimmerNameResolver(IEnumerable<KnownSwimmerName> known)
    {
        foreach (var s in known)
        {
            var tokens = Tokenize($"{s.LastName} {s.FirstName}");
            var key = ExactKey(tokens, s.BirthYear);
            if (!_byExactKey.TryGetValue(key, out var list))
                _byExactKey[key] = list = [];
            list.Add(s);
            _all.Add((tokens, s));
        }
    }

    /// <summary>Сопоставить имя из источника; club участвует только как тайбрейк.</summary>
    public ResolvedSwimmerName Resolve(string fullName, int? birthYear, string club)
    {
        var tokens = Tokenize(fullName);
        if (tokens.Count == 0) return new ResolvedSwimmerName(fullName.Trim(), string.Empty, false);

        if (birthYear is int year)
        {
            if (_byExactKey.TryGetValue(ExactKey(tokens, year), out var exact) && exact.Count > 0)
                return Take(exact[0]);

            // Имя источника полнее или беднее нашего — сравниваем по вложенности наборов.
            var candidates = _all
                .Where(x => x.Swimmer.BirthYear == year
                            && (x.Tokens.IsSubsetOf(tokens) || tokens.IsSubsetOf(x.Tokens)))
                .Select(x => x.Swimmer)
                .ToList();

            if (candidates.Count == 1) return Take(candidates[0]);
            if (candidates.Count > 1)
            {
                var sameClub = candidates.Where(c => Normalize(c.Club) == Normalize(club)).ToList();
                if (sameClub.Count == 1) return Take(sameClub[0]);
            }
        }

        // Пары нет: последний токен — фамилия (порядок источника «имя … фамилия»).
        var ordered = Tokenize(fullName, keepOrder: true);
        return new ResolvedSwimmerName(
            ordered[^1],
            string.Join(' ', ordered[..^1]),
            false);
    }

    private static ResolvedSwimmerName Take(KnownSwimmerName s) => new(s.LastName, s.FirstName, true);

    private static string ExactKey(IEnumerable<string> tokens, int birthYear) =>
        $"{string.Join(' ', tokens.OrderBy(t => t, StringComparer.Ordinal))}|{birthYear}";

    private static HashSet<string> Tokenize(string name) =>
        [.. Tokenize(name, keepOrder: true)];

    private static List<string> Tokenize(string name, bool keepOrder) =>
        [.. Normalize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>Апострофы всех видов → ивритский герш; лишние пробелы убраны.</summary>
    private static string Normalize(string text) => text
        .Replace('\'', '׳')
        .Replace('’', '׳')
        .Replace('‘', '׳')
        .Replace('`', '׳')
        .Trim();
}
