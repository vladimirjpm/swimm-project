using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Один прогон напоминаний (джоб <c>TrainingReminderBackgroundService</c>, §7 плана): собрать
/// данные, спросить правило <c>TrainingReminderRules.Plan</c>, застолбить строку в журнале доставки
/// (UNIQUE — защита от дубля) и разослать.
/// </summary>
public interface ITrainingReminderService
{
    Task<TrainingReminderReport> RunAsync(DateTime nowUtc, CancellationToken ct = default);
}
