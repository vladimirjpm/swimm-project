namespace Swimm.Application.Mapping;

/// <summary>
/// Дубль страницы федерации (решение Влада 26.11.2026): две строки «Входящих» с разными compID
/// ведут на одно соревнование loglig (тот же <c>LogligId</c>) В ТЕ ЖЕ ДНИ. Когда одна из них уже
/// в базе, вторая затянута вместе с ней — тянуть её нечего. Такая строка закрывается сама (серая,
/// «дубль N»), не висит в долге и не попадает в пачки и results-auto.
///
/// Пример: compID 1489 «ליגה צפון והעמקים 2» и compID 1495 «ליגה צפון ועמקים 2», 26.01.2024 —
/// обе с loglig 9933; имена различаются буквой, поэтому матчер имя+дата их не связал.
///
/// ⚠ Даты обязательны. Один loglig бывает и у РАЗНЫХ стартов: «מוקדמות אליפות צעירים» части
/// א' (24.01.2025) и ב' (31.01.2025) — loglig 11792; «ליגה נוער ובוגרים» 1 и 2 в Кирьят-Бялик
/// (18.10.2024 и 14.03.2025) — loglig 11288. Там вторая часть — свой старт, её надо тянуть.
/// Поэтому дубль — только при пересечении диапазонов дат.
/// </summary>
public static class DiscoveryLogligTwinRule
{
    /// <summary>Строка «Входящих» для правила.</summary>
    /// <param name="InDb">Строка уже связана с соревнованием в базе (OrgCompId или матч имя+дата).</param>
    public sealed record Row(int Id, int OrgCompId, int? LogligId, DateTime DateStart, DateTime DateEnd, bool InDb);

    /// <summary>
    /// Id строки не в базе → compID её близнеца, который в базе (тот же <c>LogligId</c>, даты
    /// пересекаются). Близнецов несколько — берётся меньший compID, чтобы подпись не прыгала.
    /// Строки без <c>LogligId</c> и строки в базе в ответ не попадают.
    /// </summary>
    public static IReadOnlyDictionary<int, int> TwinOf(IEnumerable<Row> rows)
    {
        var list = rows as IReadOnlyCollection<Row> ?? rows.ToList();
        var inDbByLoglig = list
            .Where(r => r.InDb && r.LogligId is not null)
            .ToLookup(r => r.LogligId!.Value);

        var twins = new Dictionary<int, int>();
        foreach (var r in list)
        {
            if (r.InDb || r.LogligId is not int lid) continue;
            var twin = inDbByLoglig[lid]
                .Where(t => t.OrgCompId != r.OrgCompId && Overlaps(r, t))
                .Select(t => (int?)t.OrgCompId)
                .Min();
            if (twin is int tw) twins[r.Id] = tw;
        }
        return twins;
    }

    private static bool Overlaps(Row a, Row b)
    {
        var (a0, a1) = Range(a);
        var (b0, b1) = Range(b);
        return a0 <= b1 && b0 <= a1;
    }

    private static (DateTime From, DateTime To) Range(Row r) =>
        r.DateEnd.Date >= r.DateStart.Date ? (r.DateStart.Date, r.DateEnd.Date) : (r.DateStart.Date, r.DateStart.Date);
}
