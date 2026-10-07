namespace Swimm.Application.Mapping;

/// <summary>
/// Ключ дисциплины для поиска её дня: стиль × дистанция × пол в словаре НАШИХ строк
/// (<c>Styles.Name</c>, <c>Results.Distance</c>, <c>Results.Gender</c>). Регистр и пробелы не важны.
/// </summary>
public sealed record DisciplineDayKey
{
    public string Style { get; }
    public string Distance { get; }
    public string Gender { get; }

    public DisciplineDayKey(string? style, string? distance, string? gender)
    {
        Style = (style ?? "").Trim().ToLowerInvariant();
        Distance = (distance ?? "").Trim().ToUpperInvariant();
        Gender = (gender ?? "").Trim().ToLowerInvariant();
    }

    public override string ToString() => $"{Style} {Distance} {Gender}";
}

/// <summary>
/// Даты дисциплин многодневного старта (docs/plans/multiday-split-plan.md, М1).
///
/// Зачем: PDF протокола печатает в шапке только ДИАПАЗОН («03/01/2025 - 04/01/2025»), и разбор
/// кладёт все строки на одну дату — мастерс-зима 2025 (#1645) и 2026 (#64) легли одним днём.
/// Дата каждой дисциплины есть у пособытийного источника (сетка loglig); этот словарь — её
/// единственное представление для деления дней (М2) и для импорта (М3).
///
/// Неоднозначный ключ — одна дисциплина в оба дня (отборы утром, финал завтра): дату ему не
/// даём, а называем (решение Р-сп-3: отказ, а не догадка по раунду).
/// </summary>
public sealed class DisciplineDates
{
    private readonly Dictionary<DisciplineDayKey, DateOnly> _dates;

    public DisciplineDates(IEnumerable<(DisciplineDayKey Key, DateOnly Date)> disciplines)
    {
        var byKey = disciplines
            .GroupBy(d => d.Key)
            .ToDictionary(g => g.Key, g => g.Select(d => d.Date).Distinct().ToList());

        _dates = byKey.Where(kv => kv.Value.Count == 1).ToDictionary(kv => kv.Key, kv => kv.Value[0]);
        Ambiguous = byKey.Where(kv => kv.Value.Count > 1).Select(kv => kv.Key).ToList();
        Days = byKey.Values.SelectMany(d => d).Distinct().Order().ToList();
    }

    /// <summary>Все даты источника по порядку; одна — старт однодневный, делить нечего.</summary>
    public IReadOnlyList<DateOnly> Days { get; }

    /// <summary>Дисциплины, которые источник ставит в РАЗНЫЕ дни.</summary>
    public IReadOnlyList<DisciplineDayKey> Ambiguous { get; }

    /// <summary>
    /// День строки. Сначала точный ключ; не нашёлся (пол строки «none» или пустой — старые
    /// эстафеты без пола) — все ключи с тем же стилем и дистанцией, если они в ОДИН день.
    /// null — дня нет или он неоднозначен.
    /// </summary>
    public DateOnly? DateOf(DisciplineDayKey key)
    {
        if (Ambiguous.Contains(key)) return null;
        if (_dates.TryGetValue(key, out var exact)) return exact;

        var sameEvent = _dates.Where(kv => kv.Key.Style == key.Style && kv.Key.Distance == key.Distance)
            .Select(kv => kv.Value).Distinct().ToList();
        if (Ambiguous.Any(k => k.Style == key.Style && k.Distance == key.Distance)) return null;
        return sameEvent.Count == 1 ? sameEvent[0] : null;
    }
}

/// <summary>Строка соревнования для раскладки по дням.</summary>
public sealed record DayPlanRow(long ResultId, string Style, string Distance, string Gender);

/// <summary>
/// Раскладка строк по дням. Всё или ничего: <see cref="CanApply"/> только когда день есть у
/// КАЖДОЙ строки — полразложенное соревнование хуже схлопнутого.
/// </summary>
/// <param name="ByDay">День → id строк.</param>
/// <param name="Unresolved">Ключ без дня (или с неоднозначным) → сколько строк.</param>
public sealed record DayPlan(
    IReadOnlyDictionary<DateOnly, IReadOnlyList<long>> ByDay,
    IReadOnlyDictionary<DisciplineDayKey, int> Unresolved)
{
    public bool CanApply => Unresolved.Count == 0 && ByDay.Count > 1;
}

public static class DisciplineDayPlanner
{
    public static DayPlan Plan(IEnumerable<DayPlanRow> rows, DisciplineDates dates)
    {
        var byDay = new Dictionary<DateOnly, List<long>>();
        var unresolved = new Dictionary<DisciplineDayKey, int>();

        foreach (var row in rows)
        {
            var key = new DisciplineDayKey(row.Style, row.Distance, row.Gender);
            if (dates.DateOf(key) is not { } day)
            {
                unresolved[key] = unresolved.GetValueOrDefault(key) + 1;
                continue;
            }
            if (!byDay.TryGetValue(day, out var ids)) byDay[day] = ids = new List<long>();
            ids.Add(row.ResultId);
        }

        return new DayPlan(
            byDay.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => (IReadOnlyList<long>)kv.Value),
            unresolved);
    }
}
