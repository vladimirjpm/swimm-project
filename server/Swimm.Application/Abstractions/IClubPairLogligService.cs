using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Что знает loglig про общих пловцов пары клубов-кандидатов в дубли (Р112, /Admin/Clubs).
/// Общие пловцы — те же, что считает эвристика «общие пловцы» (<see cref="IClubDedupService"/>):
/// клуб карточки и клубы строк результатов.
/// </summary>
public interface IClubPairLogligService
{
    /// <summary>Общие пловцы и сохранённые ответы loglig — без сети. null — клуба нет.</summary>
    Task<ClubPairLogligReport?> GetAsync(int clubA, int clubB, CancellationToken ct = default);

    /// <summary>
    /// Открыть карточки loglig общих пловцов с несвежим ответом (нет / старше суток / другой
    /// loglig-id) — последовательно, ~1 запрос в секунду, не больше <paramref name="maxRequests"/>
    /// за вызов — и сохранить ответы. null — клуба нет.
    /// </summary>
    Task<ClubPairLogligReport?> CheckAsync(int clubA, int clubB, int maxRequests, CancellationToken ct = default);

    /// <summary>
    /// Сводки по многим парам разом (таблица кандидатов) — из сохранённых ответов, без сети.
    /// Ключ — (A, B) как передан; общие пловцы пары уже посчитаны вызывающим.
    /// </summary>
    Task<Dictionary<(int A, int B), ClubPairLogligSummary>> SummarizeAsync(
        IReadOnlyList<(int A, int B, IReadOnlyCollection<int> SwimmerIds)> pairs, CancellationToken ct = default);
}
