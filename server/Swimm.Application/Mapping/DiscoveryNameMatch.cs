namespace Swimm.Application.Mapping;

/// <summary>
/// «Строка Входящих = этот день справочника» по имени и дате — правило одно на двоих:
/// <c>DiscoveryCompetitionMatcher</c> (Infrastructure, /Admin/Competitions, дашборд) и календарь
/// главной (<see cref="CompetitionCalendarBuilder"/>: затянутое не дублируется будущим стартом).
/// OrgCompId у PDF-импортов не заполняется — имя+дата единственный шов.
///
/// Матч: дата дня в <c>[start..end]</c> строки и имя совпадает после нормализации ЛИБО имя
/// строки начинается с имени дня — сайт дописывает суффикс района («…- מחוז צפון»), которого в
/// протоколе/БД нет.
/// </summary>
public static class DiscoveryNameMatch
{
    /// <summary>Trim/lower, схлопнуть пробелы, выкинуть кавычки/бэкслеши/гереш-гершаим —
    /// они непоследовательны между сайтом и импортированными именами (в БД встречаются «ארנה»,
    /// «"ארנה"» и «\"ארנה\"» — артефакт импорта).</summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var cleaned = new string(name.Where(c => c is not ('"' or '\\' or '\'' or '׳' or '״' or '`' or '’' or '“' or '”')).ToArray());
        return string.Join(' ', cleaned.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    }

    /// <summary>Сравнение уже нормализованных имён: равны или имя строки начинается с имени дня.
    /// Пустое имя дня не матчится ни с чем.</summary>
    public static bool KeysMatch(string discoveredKey, string competitionKey) =>
        competitionKey.Length > 0
        && (discoveredKey == competitionKey || discoveredKey.StartsWith(competitionKey, StringComparison.Ordinal));

    /// <summary>Полное правило: дата дня в промежутке строки + имя.</summary>
    public static bool Matches(
        string? discoveredName, DateOnly discoveredStart, DateOnly discoveredEnd,
        string? competitionName, DateOnly competitionDate) =>
        competitionDate >= discoveredStart && competitionDate <= discoveredEnd
        && KeysMatch(Normalize(discoveredName), Normalize(competitionName));
}
