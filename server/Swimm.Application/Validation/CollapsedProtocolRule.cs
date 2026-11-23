namespace Swimm.Application.Validation;

/// <summary>День этого compID в базе: дата и сколько в нём строк результатов.</summary>
public sealed record CollapsedProtocolDay(int CompetitionId, string Date, int Rows);

/// <summary>
/// Находка <see cref="CollapsedProtocolRule"/>: файл одной датой, а дни этого compID в базе — другими.
/// </summary>
/// <param name="FileDate">Единственная дата файла.</param>
/// <param name="FileRows">Строк в файле.</param>
/// <param name="Days">Дни compID в базе с результатами (в т.ч. день с датой файла, если он есть).</param>
public sealed record CollapsedProtocolFinding(string FileDate, int FileRows, IReadOnlyList<CollapsedProtocolDay> Days)
{
    /// <summary>Строк в днях базы с ДРУГОЙ датой — то, что файл задвоит.</summary>
    public int OtherDaysRows => Days.Where(d => d.Date != FileDate).Sum(d => d.Rows);

    /// <summary>Текст для превью, журнала импорта и причин пачки/results-auto.</summary>
    public string Message
    {
        get
        {
            var days = string.Join(", ", Days.OrderBy(d => d.Date.Length == 10 ? d.Date[6..] + d.Date[3..5] + d.Date[..2] : d.Date)
                .Select(d => $"{d.Date} — #{d.CompetitionId}, {d.Rows} строк"));
            var total = Days.Sum(d => d.Rows);
            var same = Math.Abs(total - FileRows) <= Math.Max(5, FileRows / 50)
                ? " — те же результаты"
                : "";
            return $"{CollapsedProtocolRule.Marker}: в файле одна дата {FileDate} ({FileRows} строк), а у этого " +
                   $"compID в базе уже есть дни {days} (всего {total}{same}). Импорт положил бы эти результаты " +
                   "ещё раз — дублем. Импорт заблокирован; раскладка такого протокола по дням — решение Влада " +
                   "(docs/data-integrity.md, Р119).";
        }
    }
}

/// <summary>
/// Р119: «протокол свёрнут в одну дату, а у этого compID в базе уже есть дни события с другими
/// датами». loglig у многодневки печатает ВСЕМ строкам последнюю дату диапазона (И-60); такой
/// протокол затягивают по дням (путь Б, <c>day-json.mjs</c>), и потом «Затянуть»/«Перезатянуть»
/// всего файла положили бы те же строки третьим днём (e62, compID 16891: 1579 = 766 + 813).
///
/// Чистая функция: решает по датам и числу строк, без базы. Срабатывает, когда:
/// <list type="bullet">
/// <item>в файле ровно одна дата D;</item>
/// <item>у compID в базе есть день с результатами и датой ≠ D;</item>
/// <item>строк в файле не меньше <see cref="MinCoverage"/> от строк этих дней — файл достаточно велик,
/// чтобы их содержать. Маленький файл одной датой (эстафеты одного дня со страниц событий loglig)
/// — законный перезабор одного дня, не дубль.</item>
/// </list>
/// </summary>
public static class CollapsedProtocolRule
{
    /// <summary>Начало сообщения — по нему его узнают в журнале и тестах.</summary>
    public const string Marker = "⛔ Протокол свёрнут в одну дату";

    /// <summary>
    /// Какую долю строк дней с другой датой должен покрывать файл. 0,9 — «почти то же число»: у e62
    /// файл 1579 против 1579, у идущего старта (весь протокол против дня 1) — 1556 против 657.
    /// </summary>
    public const double MinCoverage = 0.9;

    public static CollapsedProtocolFinding? Detect(
        IEnumerable<string?> fileDates, int fileRows, IEnumerable<CollapsedProtocolDay> daysInDb)
    {
        var dates = fileDates.Select(d => d?.Trim() ?? "").Where(d => d.Length > 0).Distinct().ToList();
        if (dates is not [var fileDate]) return null;

        var days = daysInDb.Where(d => d.Rows > 0).ToList();
        var otherRows = days.Where(d => d.Date.Trim() != fileDate).Sum(d => d.Rows);
        if (otherRows == 0) return null;
        if (fileRows < otherRows * MinCoverage) return null;

        return new CollapsedProtocolFinding(fileDate, fileRows, days);
    }
}
