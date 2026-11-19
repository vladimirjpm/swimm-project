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
    /// Прогон «Проверить в loglig» (зовёт фоновая задача очереди <see cref="IClubPairLogligCheckQueue"/>):
    /// открыть карточки loglig общих пловцов <paramref name="pairs"/> с несвежим ответом (нет /
    /// старше суток / другой loglig-id) — последовательно, ~1 запрос в секунду, общего для
    /// нескольких пар — один раз — и сохранить ответы. Прогресс и сводку каждой пройденной пары
    /// пишет в <paramref name="status"/>.
    /// </summary>
    Task RunAsync(ClubPairLogligRunStatus status, IReadOnlyList<(int A, int B)> pairs, CancellationToken ct = default);

    /// <summary>
    /// Сводки по многим парам разом (таблица кандидатов) — из сохранённых ответов, без сети.
    /// Ключ — (A, B) как передан; общие пловцы пары уже посчитаны вызывающим.
    /// </summary>
    Task<Dictionary<(int A, int B), ClubPairLogligSummary>> SummarizeAsync(
        IReadOnlyList<(int A, int B, IReadOnlyCollection<int> SwimmerIds)> pairs, CancellationToken ct = default);
}
