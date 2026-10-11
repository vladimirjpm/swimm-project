using System.Globalization;

namespace Swimm.Application.Mapping;

/// <summary>Рекорд Израиля вольным стилем — сырьё для <see cref="RelayLegFloor"/>.</summary>
/// <param name="Age">Возраст ступени справочника; null — взрослый рекорд («open»).</param>
public sealed record FreestyleRecordRow(int? Age, string Gender, string Pool, int Distance, long TimeMs);

/// <summary>
/// Нижний предел времени этапа эстафеты: быстрее него пловец этого возраста проплыть не может
/// (решение Влада 10.10.2026, docs/relays.md, «Невозможный этап»).
///
/// Зачем. loglig печатает сбои касания хронометража: этап 4×50 за 00:18.06 у одиннадцатилетних,
/// 00:00.69 у мастерсов. Нарастающие при этом ровные и складываются в итог, так что сторожа
/// разборщика (отрицательная ячейка, сумма отрезков) их не видят. Абсолютный порог не годится:
/// детский этап за 19 с невозможен, а взрослый за 21 с — обычное дело; относительный («сильно
/// быстрее среднего по команде») тоже: у мастерсов в одной команде 30 и 80 лет.
///
/// Правило: этап не может быть быстрее <b>рекорда Израиля вольным</b> для возраста и пола
/// пловца на эту дистанцию в этом бассейне, с запасом: <c>рекорд × 0.97 − 1 с</c> (1 с —
/// эстафетный старт, 3% — на устаревший рекорд). Вольный — самый быстрый стиль, поэтому предел
/// годится и для этапов комплекса. Всё в сторону «пропустить», а не «поймать»:
/// <list type="bullet">
///   <item>возраст — год соревнования − год рождения + 1 (верхняя оценка: и календарная ось
///     справочника, и сезонная дают не больше);</item>
///   <item>старше последней ступени (мастерсы) или год рождения неизвестен — взрослый рекорд;
///     младше первой — рекорд первой ступени (он быстрее);</item>
///   <item>ступени нет — ближайшая СТАРШАЯ (быстрее); пол неизвестен — мужской (быстрее);</item>
///   <item>рекорда нет вовсе — предела нет, этап не проверяется.</item>
/// </list>
/// Чистая функция: справочник передаётся готовым списком.
/// </summary>
public sealed class RelayLegFloor
{
    /// <summary>Доля рекорда в процентах: запас на рекорд, который успел устареть.</summary>
    public const int RecordSharePercent = 97;

    /// <summary>Выигрыш эстафетного старта перед стартом с тумбы.</summary>
    public const long RelayStartMs = 1000;

    public static readonly RelayLegFloor Empty = new([]);

    private readonly Dictionary<(string Gender, string Pool, int Distance), SortedDictionary<int, long>> _byAge = [];
    private readonly Dictionary<(string Gender, string Pool, int Distance), long> _open = [];

    public RelayLegFloor(IEnumerable<FreestyleRecordRow> records)
    {
        foreach (var r in records)
        {
            var key = (Gender(r.Gender), Pool(r.Pool), r.Distance);
            if (r.Age is int age)
            {
                if (!_byAge.TryGetValue(key, out var ages)) _byAge[key] = ages = [];
                ages[age] = ages.TryGetValue(age, out var t) ? Math.Min(t, r.TimeMs) : r.TimeMs;
            }
            else
            {
                _open[key] = _open.TryGetValue(key, out var t) ? Math.Min(t, r.TimeMs) : r.TimeMs;
            }
        }
    }

    /// <summary>Предел этапа в мс; null — рекорда нет, этап не проверяется.</summary>
    public long? FloorMs(string? gender, string? pool, int legDistance, int? birthYear, int year)
    {
        var record = Gender(gender) == ""
            ? Min(Record("male", pool, legDistance, birthYear, year), Record("female", pool, legDistance, birthYear, year))
            : Record(Gender(gender), pool, legDistance, birthYear, year);
        return record is long ms ? ms * RecordSharePercent / 100 - RelayStartMs : null;
    }

    /// <summary>
    /// Причина, по которой этап невозможен, или null. Не разобралось время, дистанция не
    /// эстафетная 50/100/200 или предела нет — null: проверять нечего, это не улика.
    /// </summary>
    public string? Implausible(
        string? split, string relayDistance, string? gender, string? pool, int? birthYear, int year)
    {
        if (string.IsNullOrWhiteSpace(split) || LegDistance(relayDistance) is not int dist) return null;
        if (RelayLeadOffMatcher.ToMs(split) is not long ms) return null;
        if (FloorMs(gender, pool, dist, birthYear, year) is not long floor || ms >= floor) return null;
        return $"этап {split} быстрее предела {Format(floor)} ({dist} м, {Gender(gender) switch { "" => "пол ?", var g => g }}, "
            + $"{(birthYear is > 0 ? $"{birthYear} г. р." : "год ?")})";
    }

    /// <summary>
    /// Первый невозможный этап команды, уже лежащей в базе (этапы, пол и годы — из базы), или
    /// null. Нужен чистке записанного раньше: доклейка проверяет этапы источника сама.
    /// </summary>
    public string? TeamImplausible(RelayRow row)
    {
        if (row.Year is not int year) return null;
        foreach (var leg in row.Legs.OrderBy(l => l.LegOrder))
            if (Implausible(leg.SplitTime, row.Distance, leg.Gender, row.Pool, leg.BirthYear, year) is string reason)
                return $"нога {leg.LegOrder}: {reason}";
        return null;
    }

    /// <summary>«4X50» → 50, «4x100m» → 100; остальное (4×1500 открытой воды) — null.</summary>
    public static int? LegDistance(string? relayDistance)
    {
        var d = relayDistance?.Trim().TrimEnd('m', 'M').ToUpperInvariant();
        return d switch { "4X50" => 50, "4X100" => 100, "4X200" => 200, _ => null };
    }

    private long? Record(string gender, string? pool, int distance, int? birthYear, int year)
    {
        var key = (gender, Pool(pool), distance);
        var open = _open.TryGetValue(key, out var o) ? o : (long?)null;
        if (birthYear is not > 0 || !_byAge.TryGetValue(key, out var ages) || ages.Count == 0) return open;

        var age = year - birthYear.Value + 1;
        if (age > ages.Keys.Max()) return open ?? ages[ages.Keys.Max()];

        // Ступени нет — ближайшая старшая: её рекорд быстрее, предел ниже.
        foreach (var (stepAge, ms) in ages)
            if (stepAge >= age) return ms;
        return open;
    }

    private static long? Min(long? a, long? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);

    private static string Gender(string? g) => g?.Trim().ToLowerInvariant() switch
    {
        "male" or "m" => "male",
        "female" or "f" or "w" => "female",
        _ => "",
    };

    /// <summary>«25m» / «25» / «SCM» → «25»; «50m» / «LCM» → «50».</summary>
    private static string Pool(string? p) => p?.Trim().ToUpperInvariant() switch
    {
        "25" or "25M" or "SCM" => "25",
        "50" or "50M" or "LCM" => "50",
        var other => other ?? "",
    };

    private static string Format(long ms) => ms >= 60_000
        ? string.Create(CultureInfo.InvariantCulture, $"{ms / 60_000}:{ms % 60_000 / 1000.0:00.00}")
        : string.Create(CultureInfo.InvariantCulture, $"{ms / 1000.0:0.00}");
}
