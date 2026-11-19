using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Задача results-auto (П5, docs/plans/prod-auto-pull-plan.md §4.4): за тик берёт ОДНУ прошедшую
/// строку «Входящих», разбирает её тем же путём, что кнопка пакетного затягивания, прогоняет гейты
/// и пишет строке вердикт с причиной. В режиме report (и пока в import — импорт это П6) ничего не
/// импортирует: ни одной записи в Competitions / Results.
/// </summary>
public interface IResultsAutoService
{
    /// <summary>Один тик расписания. Режим читает сам (<c>ResultsAutoMode</c>); off — ничего не делает.</summary>
    Task<ResultsAutoTickReport> RunTickAsync(CancellationToken ct = default);

    /// <summary>
    /// Тик с переопределениями — для CLI <c>--results-auto-tick</c>: режим вместо настройки и/или
    /// конкретная строка (тогда окно и шаг повторной проверки не смотрятся — это диагностика).
    /// </summary>
    Task<ResultsAutoTickReport> RunOnceAsync(ResultsAutoRunOptions options, CancellationToken ct = default);
}
