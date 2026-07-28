using System.Globalization;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;

namespace Swimm.Application.Mapping;

/// <summary>
/// Кандидат-строка результата для детекции рекордов (минимальная проекция заплыва).
/// Личные заплывы: эстафеты вне скоупа v1 (маппинг стилей 'medley'/'free_relay' неоднозначен).
/// </summary>
public sealed record RecordCandidateRow(
    long ResultId,
    int SwimmerId,
    string FirstName,
    string LastName,
    string Club,
    string StyleName,
    string Distance,
    string Gender,
    string PoolType,
    int? BirthYear,
    DateTime CompetitionDate,
    int TimeMilliseconds,
    string TimeOriginal,
    int? DayNumber,
    bool IsMasters,
    string AgeGroup);

/// <summary>
/// Детекция «новых рекордов» соревнования: результат быстрее (или равен) действующему
/// рекорду из таблицы Records. Серверный аналог клиентского HelperNormative.isRecordTime:
/// те же оси (gender/pool/style/distance/age), regione — country/ISR. Чистая функция —
/// I/O (загрузка Records и строк) делает репозиторий.
/// Сверяем по трём категориям: age (AgeKey = возраст в год соревнования, для masters —
/// диапазон "25-29"), open (AgeKey = "") и национальный ключ AgeKey = "ISR".
/// </summary>
public static class CompetitionRecordsDetector
{
    public static List<OverviewRecordDto> Detect(
        IReadOnlyCollection<Record> records, IReadOnlyCollection<RecordCandidateRow> rows)
    {
        if (records.Count == 0 || rows.Count == 0) return [];

        // Ключ дисциплины: gender|pool|style|distance(без 'm')|category|ageKey → время рекорда в мс.
        var index = new Dictionary<string, (int TimeMs, Record Rec)>();
        foreach (var r in records)
        {
            var ms = ParseTimeToMs(r.Time);
            if (ms is null) continue;
            var key = Key(r.Gender, r.PoolType, r.Style, TrimDistance(r.Distance), r.Category, r.AgeKey);
            // Дубли осей (не должны существовать — уникальный индекс) — берём строжайший.
            if (!index.TryGetValue(key, out var cur) || ms.Value < cur.TimeMs)
                index[key] = (ms.Value, r);
        }

        // Лучший побитый рекорд на каждую (ось рекорда) — самый быстрый заплыв.
        var best = new Dictionary<string, (RecordCandidateRow Row, Record Rec)>();
        foreach (var row in rows)
        {
            foreach (var (category, ageKey) in CandidateKeys(row))
            {
                var key = Key(row.Gender, row.PoolType, row.StyleName, row.Distance, category, ageKey);
                if (!index.TryGetValue(key, out var rec)) continue;
                // Семантика клиента (isRecordTime): время ≤ рекорда считается рекордом.
                if (row.TimeMilliseconds > rec.TimeMs) continue;
                if (!best.TryGetValue(key, out var cur) || row.TimeMilliseconds < cur.Row.TimeMilliseconds)
                    best[key] = (row, rec.Rec);
            }
        }

        return best.Values
            .OrderBy(b => b.Row.DayNumber ?? int.MaxValue)
            .ThenBy(b => b.Row.StyleName)
            .ThenBy(b => TrimDistance(b.Row.Distance).Length)
            .Select(b => new OverviewRecordDto
            {
                Kind = KindLabel(b.Rec),
                StyleName = b.Row.StyleName,
                Distance = b.Row.Distance,
                Gender = b.Row.Gender,
                Time = b.Row.TimeOriginal,
                HolderName = $"{b.Row.FirstName} {b.Row.LastName}".Trim(),
                SwimmerId = b.Row.SwimmerId,
                AgeGroup = b.Row.AgeGroup,
                Club = b.Row.Club,
                DayNumber = b.Row.DayNumber,
                ResultId = b.Row.ResultId
            })
            .ToList();
    }

    /// <summary>Категории/AgeKey, по которым заплыв может побить рекорд.</summary>
    private static IEnumerable<(string Category, string AgeKey)> CandidateKeys(RecordCandidateRow row)
    {
        // Национальный рекорд живёт ровно в одном месте — category="open" (туда его кладёт
        // IsrOrgAgeRecordsSourceProvider для строк с Note="National Record").
        // Ось ("age","ISR") была у legacy-RecordsSeeder и импортом НЕ обновлялась: набор
        // протух вместе с ошибками старого парсера и давал ложные рекорды (заплыв 41.93 на
        // 50 спине бил «национальный» 53.60 — время стометровки, заехавшее в полтинник).
        // Данные вычищены, ось убрана, чтобы прогон сида не воскресил ловушку.
        yield return ("open", "");

        if (row.BirthYear is null) yield break;
        var age = row.CompetitionDate.Year - row.BirthYear.Value;
        if (age is < 5 or > 120) yield break;

        if (row.IsMasters)
        {
            // Masters: диапазонный ключ "25-29" — нижняя граница кратна 5, ширина 5.
            var lo = age / 5 * 5;
            yield return ("masters", $"{lo}-{lo + 4}");
        }
        else
        {
            yield return ("age", age.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static string KindLabel(Record rec) => rec.Category switch
    {
        "open" => "Open record",
        "masters" => $"Masters {rec.AgeKey}",
        "age" => $"Age {rec.AgeKey} record",
        _ => rec.Category
    };

    private static string Key(string gender, string pool, string style, string distance, string category, string ageKey)
        => $"{gender}|{pool}|{style}|{distance}|{category}|{ageKey}";

    /// <summary>Records.Distance хранится с суффиксом ("100m"), Results.Distance — без ("100").</summary>
    private static string TrimDistance(string distance)
        => distance.EndsWith('m') ? distance[..^1] : distance;

    /// <summary>"35.64" | "02:45.46" | "1:02:45.46" → миллисекунды; мусор → null.</summary>
    internal static int? ParseTimeToMs(string? time)
    {
        if (string.IsNullOrWhiteSpace(time)) return null;
        var parts = time.Trim().Split(':');
        if (parts.Length > 3) return null;

        double seconds;
        if (!double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
            return null;
        if (parts.Length >= 2)
        {
            if (!int.TryParse(parts[^2], out var minutes)) return null;
            seconds += minutes * 60;
        }
        if (parts.Length == 3)
        {
            if (!int.TryParse(parts[0], out var hours)) return null;
            seconds += hours * 3600;
        }
        return (int)Math.Round(seconds * 1000);
    }
}
