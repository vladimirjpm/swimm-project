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
