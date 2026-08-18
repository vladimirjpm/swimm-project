namespace Swimm.Application.Mapping;

/// <summary>
/// Ключ, которым претензия из реестра (<c>Sys_RecordIssues</c>) сопоставляется со строкой
/// справочника рекордов: 8 осей рекорда ПЛЮС время.
///
/// Время в ключе обязательно и это главное решение реестра: метка висит на конкретном
/// ЗНАЧЕНИИ, а не на клетке лестницы. Когда рекорд побьют, время в <c>Records</c> сменится,
/// и старая претензия сама перестанет относиться к текущей записи — иначе метка «спорно»
/// осталась бы висеть на уже другом, честном достижении.
///
/// Одно место на два потребителя (Record wall клуба и публичный API рекордов) — иначе
/// ключ разъехался бы, и метка показывалась бы на одной странице и пропадала на другой.
/// </summary>
public static class RecordIssueKey
{
    public static string Of(
        string regionType, string regionCode, string category, string ageKey,
        string gender, string poolType, string style, string distance, string time)
        => string.Join('|',
            regionType.Trim().ToLowerInvariant(),
            regionCode.Trim().ToLowerInvariant(),
            category.Trim().ToLowerInvariant(),
            ageKey.Trim().ToLowerInvariant(),
            gender.Trim().ToLowerInvariant(),
            poolType.Trim().ToLowerInvariant(),
            style.Trim().ToLowerInvariant(),
            // Records хранит дистанцию с суффиксом ("100m"), реестр может нести и без него.
            distance.Trim().ToLowerInvariant().TrimEnd('m'),
            time.Trim());
}

/// <summary>Строка справочника рекордов в объёме, нужном для разноса меток по лестнице.</summary>
public sealed record RecordAxes(
    int Index,
    string RegionType, string RegionCode, string Category, string AgeKey,
    string Gender, string PoolType, string Style, string Distance,
    string Time, string? HolderName, string? RecordDate);

/// <summary>
/// Разнос претензии по КУМУЛЯТИВНОЙ лестнице федерации.
///
/// Одно достижение живёт в нескольких строках `Records`: рекорд переносится вверх по
/// возрастам, пока его не побьют (62 записи из 688 растянуты на 2–4 ступени). Претензия
/// заводится на ОДНУ ступень — ту, где достижение реально установлено (см. RecordIssue).
/// Если показывать метку только на ней, спорный рекорд на соседних ступенях выглядит
/// нормальным: живой случай — RQ-1 заведена на ступень 10, а карточка клуба показывает
/// ступень 11, и она была без значка.
///
/// Разносим не «по одному времени», а по совпадению ВСЕГО достижения: те же оси кроме
/// возраста, то же время, тот же держатель и та же дата рекорда. Иначе чужой результат
/// с таким же временем на другой ступени схватил бы чужую метку.
/// </summary>
public static class RecordIssueSpreader
{
    public static Dictionary<int, string> Resolve(
        IReadOnlyList<RecordAxes> records, IReadOnlyDictionary<string, string> issuesByKey)
    {
        var result = new Dictionary<int, string>();
        if (records.Count == 0 || issuesByKey.Count == 0) return result;

        // 1. Прямые попадания — строка, на которую претензия и заведена.
        var anchors = new List<(RecordAxes Row, string Reason)>();
        foreach (var r in records)
        {
            var key = RecordIssueKey.Of(r.RegionType, r.RegionCode, r.Category, r.AgeKey,
                r.Gender, r.PoolType, r.Style, r.Distance, r.Time);
            if (!issuesByKey.TryGetValue(key, out var reason)) continue;
            result[r.Index] = reason;
            anchors.Add((r, reason));
        }
        if (anchors.Count == 0) return result;

        // 2. Перенос по лестнице: то же достижение на других ступенях возраста.
        foreach (var (anchor, reason) in anchors)
        foreach (var r in records)
        {
            if (result.ContainsKey(r.Index)) continue;
            if (!SameAchievement(anchor, r)) continue;
            result[r.Index] = reason;
        }

        return result;
    }

    private static bool SameAchievement(RecordAxes a, RecordAxes b) =>
        Eq(a.RegionType, b.RegionType) && Eq(a.RegionCode, b.RegionCode)
        && Eq(a.Category, b.Category) && Eq(a.Gender, b.Gender) && Eq(a.PoolType, b.PoolType)
        && Eq(a.Style, b.Style)
        && Eq(a.Distance.TrimEnd('m', 'M'), b.Distance.TrimEnd('m', 'M'))
        && a.Time.Trim() == b.Time.Trim()
        // Держатель и дата обязательны: без них «то же время» ловило бы чужие достижения.
        && Eq(a.HolderName ?? "", b.HolderName ?? "")
        && Eq(a.RecordDate ?? "", b.RecordDate ?? "")
        && !string.IsNullOrWhiteSpace(a.HolderName);

    private static bool Eq(string x, string y) =>
        string.Equals(x.Trim(), y.Trim(), StringComparison.OrdinalIgnoreCase);
}
