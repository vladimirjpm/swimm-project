namespace Swimm.Application.Abstractions;

/// <summary>
/// Пересчёт производных величин соревнования, которые материализованы в строках результатов
/// (объединённые места «Combine All Results», в перспективе — очки).
///
/// Единая точка: денормализованные данные обязаны пересчитываться при импорте, ручной правке
/// результата, смене привязанного правила очков и включении ShowCombineAllResults. Без этого
/// они расходятся молча — главный риск материализации
/// (docs/points-rules-per-competition-plan.md §3.4).
/// </summary>
public interface ICompetitionRecalculationService
{
    /// <summary>Пересчитывает соревнование (для дня многодневки — всё событие целиком,
    /// потому что объединённый зачёт считается по всем дням). Возвращает число обновлённых строк.</summary>
    Task<int> RecalculateCompetitionAsync(int competitionId, CancellationToken ct = default);

    /// <summary>Пересчитывает все соревнования с ShowCombineAllResults — бэкфилл и разовые прогоны.</summary>
    Task<int> RecalculateAllCombinedAsync(CancellationToken ct = default);
}
