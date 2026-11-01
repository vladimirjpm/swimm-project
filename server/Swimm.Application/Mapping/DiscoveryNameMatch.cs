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
///
/// Суффикс района — только для дня без чужого штампа (И-57, 01.11.2026). День, у которого
/// <c>OrgCompId</c> (свой или события) — ДРУГОЙ compID, уже принадлежит другому старту сайта:
/// округа одного турнира делят префикс названия, и «Юг» отборочных лета 2026 склеился с днём
/// «Севера» — строка показывалась затянутой, дыру не видели. Точное совпадение имени по-прежнему
/// матчится и с проштампованным днём: так сайт дублирует один старт под двумя compID (16767/16757).
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

    /// <summary>
    /// День проштампован ДРУГИМ стартом сайта: у него (или его события) есть compID, и это не
    /// compID строки. Нет штампа вовсе (PDF-импорт) — не чужой. <paramref name="sameProtocol"/> —
    /// «этот compID — тот же протокол loglig» (общий <c>LogligId</c>): сайт режет один протокол на
    /// части («חלק א'» 6621 и «חלק ב'» 6622 — loglig 11792, одно событие в базе), и штамп второй
    /// части — свой, а не чужой.
    /// </summary>
    public static bool IsStampedByOther(int discoveredOrgCompId, int? competitionOrgCompId, int? eventOrgCompId,
        Func<int, bool>? sameProtocol = null)
    {
        bool Own(int? stamp) => stamp is int s && (s == discoveredOrgCompId || sameProtocol?.Invoke(s) == true);
        return (competitionOrgCompId is not null || eventOrgCompId is not null)
            && !Own(competitionOrgCompId) && !Own(eventOrgCompId);
    }

    /// <summary>
    /// Предикат «тот же протокол» для <see cref="IsStampedByOther"/> по словарю compID → LogligId.
    /// Без LogligId у строки — только её собственный compID.
    /// </summary>
    public static Func<int, bool> SameProtocol(int? logligId, IReadOnlyDictionary<int, int> logligByOrgCompId) =>
        stamp => logligId is int l && logligByOrgCompId.TryGetValue(stamp, out var other) && other == l;

    /// <summary>Сравнение уже нормализованных имён: равны или имя строки начинается с имени дня
    /// (последнее — только если день не проштампован другим стартом). Пустое имя дня не матчится
    /// ни с чем.</summary>
    public static bool KeysMatch(string discoveredKey, string competitionKey, bool stampedByOther = false) =>
        competitionKey.Length > 0
        && (discoveredKey == competitionKey
            || (!stampedByOther && discoveredKey.StartsWith(competitionKey, StringComparison.Ordinal)));

    /// <summary>Полное правило: дата дня в промежутке строки + имя.</summary>
    public static bool Matches(
        string? discoveredName, DateOnly discoveredStart, DateOnly discoveredEnd,
        string? competitionName, DateOnly competitionDate, bool stampedByOther = false) =>
        competitionDate >= discoveredStart && competitionDate <= discoveredEnd
        && KeysMatch(Normalize(discoveredName), Normalize(competitionName), stampedByOther);
}
