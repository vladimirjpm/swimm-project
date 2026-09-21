namespace Swimm.Application.Mapping;

/// <summary>
/// Полоса мирового юниорского рекорда (WJR) против возрастной ступени израильского рекорда
/// (WJR-план J2, docs/plans/world-junior-records-plan.md §3).
///
/// У мастерс ступени мировые и израильские совпадают один в один, и матч там точный. Здесь —
/// нет: израильский ключ — один возраст (<c>AgeKey="15"</c>), а WJR — полоса (<c>"14-17"</c>
/// у женщин, <c>"15-18"</c> у мужчин, решение 21.09.2026 — полоса лежит в самом ключе). Отсюда
/// следствие, которое витрина обязана признать: одно время WJR встаёт против НЕСКОЛЬКИХ
/// израильских ступеней.
///
/// Вне полосы — пусто навсегда: 10–13 у обоих полов, 18 у девушек, 14 у юношей; и
/// нечисловые ступени (<c>adults</c>) тоже — официального эталона для них нет в принципе.
/// </summary>
public static class WorldJuniorBand
{
    /// <summary>Входит ли ступень <paramref name="ageKey"/> в полосу <paramref name="bandKey"/>.</summary>
    public static bool Covers(string? bandKey, string? ageKey) =>
        TryParse(bandKey, out var min, out var max)
        && int.TryParse((ageKey ?? "").Trim(), out var age)
        && age >= min && age <= max;

    /// <summary>«14-17» → (14, 17). Что-то другое — не полоса.</summary>
    public static bool TryParse(string? bandKey, out int min, out int max)
    {
        min = max = 0;
        var parts = (bandKey ?? "").Split('-', StringSplitOptions.TrimEntries);
        return parts.Length == 2
               && int.TryParse(parts[0], out min)
               && int.TryParse(parts[1], out max)
               && min <= max;
    }
}
