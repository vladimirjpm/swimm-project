using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Сверка карточки loglig с нашими результатами (docs/loglig-id-plan.md, шаг 3). Чистая логика
/// без БД — список наших результатов и данные пловца передаются вызывающим кодом.
/// </summary>
public class LogligMatchService : ILogligMatchService
{
    private const int TimeToleranceMs = 20;

    public LogligMatchReport Match(
        LogligPlayerCard card,
        int swimmerBirthYear,
        string? swimmerClubName,
        IReadOnlyList<LocalResultKey> localResults)
    {
        var birthYearMatch = card.BirthYear.HasValue && card.BirthYear.Value == swimmerBirthYear;
        var clubNameMatch = !string.IsNullOrWhiteSpace(card.ClubName)
            && !string.IsNullOrWhiteSpace(swimmerClubName)
            && string.Equals(card.ClubName.Trim(), swimmerClubName.Trim(), StringComparison.Ordinal);

        var matchedRows = new List<LogligResultRow>();
        foreach (var row in card.Results)
        {
            // Эстафеты и строки без стиля/времени в сверке не участвуют.
            if (row.IsRelay || row.StyleName is null || row.TimeMillisecond is null || row.Distance is null)
                continue;

            var isMatched = localResults.Any(local =>
                local.Date.Date == row.Date.Date
                && string.Equals(local.Distance.Trim(), row.Distance.Trim(), StringComparison.Ordinal)
                && string.Equals(local.StyleName.Trim(), row.StyleName.Trim(), StringComparison.Ordinal)
                && local.TimeMillisecond.HasValue
                && Math.Abs(local.TimeMillisecond.Value - row.TimeMillisecond.Value) <= TimeToleranceMs);

            if (isMatched)
                matchedRows.Add(row);
        }

        var matchedCount = matchedRows.Count;

        LogligMatchDecision decision;
        if (birthYearMatch && matchedCount >= 2)
            decision = LogligMatchDecision.AutoVerify;
        else if (birthYearMatch && matchedCount is 0 or 1)
            decision = LogligMatchDecision.Candidate;
        else if (!birthYearMatch && matchedCount >= 2)
            decision = LogligMatchDecision.Candidate;
        else
            decision = LogligMatchDecision.NoMatch;

        return new LogligMatchReport(decision, birthYearMatch, clubNameMatch, matchedCount, matchedRows);
    }
}
