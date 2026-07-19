using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>Ключ нашего результата для сверки с карточкой loglig (без EF-сущности).</summary>
public sealed record LocalResultKey(DateTime Date, string Distance, string StyleName, int? TimeMillisecond);

/// <summary>Решение по итогу сверки карточки loglig с нашими результатами.</summary>
public enum LogligMatchDecision
{
    AutoVerify,
    Candidate,
    NoMatch
}

/// <summary>Отчёт сверки: решение + сигналы, на которых оно основано.</summary>
public sealed record LogligMatchReport(
    LogligMatchDecision Decision,
    bool BirthYearMatch,
    bool ClubNameMatch,
    int MatchedResultCount,
    IReadOnlyList<LogligResultRow> MatchedRows);

/// <summary>
/// Чистая логика сверки карточки loglig с нашими результатами (docs/loglig-id-plan.md, шаг 3).
/// Без обращений к БД — загрузку данных делают вызывающие сервисы.
/// </summary>
public interface ILogligMatchService
{
    LogligMatchReport Match(
        LogligPlayerCard card,
        int swimmerBirthYear,
        string? swimmerClubName,
        IReadOnlyList<LocalResultKey> localResults);
}
