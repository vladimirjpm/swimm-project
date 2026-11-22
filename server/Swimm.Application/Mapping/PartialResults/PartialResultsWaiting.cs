using System.Globalization;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>День соревнования с видимой отметкой «результаты неполные» — проекция запроса.</summary>
public sealed record PartialResultsDay(
    int CompetitionId, string Name, string Date, int? DayNumber, int? OrgCompId, string Note, DateTime? MarkedAt);

/// <summary>
/// «Ждёт тебя» от отметки «результаты неполные» (Р114) — строка блока «Требует внимания» на
/// /Admin/Jobs и единица счётчика «ждёт тебя». Встраивается в тот же список, что строки
/// results-auto (<see cref="ResultsAutoWaitingRow"/>), с вердиктом <see cref="Verdict"/>.
/// </summary>
public static class PartialResultsWaiting
{
    /// <summary>Вердикт строки «ждёт тебя» от затянутого неполным соревнования.</summary>
    public const string Verdict = "partial";

    public static ResultsAutoWaitingRow ToRow(PartialResultsDay d)
    {
        var date = DateTime.TryParseExact(d.Date, "dd/MM/yyyy", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed) ? parsed : DateTime.MinValue;
        var name = d.DayNumber is int n ? $"{d.Name} (день {n})" : d.Name;
        return new ResultsAutoWaitingRow(d.CompetitionId, d.OrgCompId ?? 0, name, date, date, d.Note, d.MarkedAt, Verdict)
        {
            CompetitionId = d.CompetitionId,
        };
    }

    /// <summary>
    /// Строки results-auto плюс неполные соревнования. Старт, который во «Входящих» застрял, а
    /// потом всё же затянут, — одна строка, не две: строку «Входящих» с тем же compID, что у
    /// неполного соревнования, отбрасываем (обычно её уже убрал матч «уже в базе»; здесь — на
    /// случай, когда матч имени промахнулся, а compID совпал).
    /// </summary>
    public static IReadOnlyList<ResultsAutoWaitingRow> Merge(
        IReadOnlyList<ResultsAutoWaitingRow> autoRows, IReadOnlyList<PartialResultsDay> partial)
    {
        if (partial.Count == 0) return autoRows;
        var pulled = partial.Where(p => p.OrgCompId is not null).Select(p => p.OrgCompId!.Value).ToHashSet();
        return
        [
            .. autoRows.Where(r => !pulled.Contains(r.OrgCompId)),
            .. partial.OrderBy(p => p.MarkedAt).Select(ToRow),
        ];
    }

    /// <summary>Текст строки «Требует внимания» (по-русски, админка).</summary>
    public static string Text(ResultsAutoWaitingRow w)
    {
        var org = w.OrgCompId > 0 ? $" #{w.OrgCompId}" : "";
        var date = w.DateEnd == DateTime.MinValue ? "" : $" ({w.DateEnd:dd.MM.yyyy})";
        return $"Соревнование «{w.Name}»{org}{date} затянуто с неполными результатами: {w.Reason ?? "чего не хватает — не записано"}. " +
               "Полный протокол снимет отметку сам; остального не будет — «Принять как есть»";
    }

    public static string Href(int competitionId) => $"/Admin/Competitions/Edit?id={competitionId}#partial-results";
}
