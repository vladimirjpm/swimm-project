namespace Swimm.Application.Abstractions;

/// <summary>
/// Push участникам группы «дорожки готовы» после публикации плана с галочкой «Notify members»
/// (docs/plans/trainings-coach-flow-plan.md, CF3, §8). Тот же отправитель, что у напоминаний
/// (docs/plans/web-push-reminders-plan.md): получатели — активные участники-аккаунты с подпиской,
/// кроме того, кто публикует, выключивших эту группу (<c>Sys_HubGroupReminderMutes</c>) и
/// выключивших напоминания совсем. Журнала нет: это разовое действие тренера, а не расписание.
/// Push не настроен — молча ничего. Ошибки доставки не бросает: план уже опубликован.
/// </summary>
public interface ILanePlanNotifier
{
    /// <returns>Сколько человек получили push хотя бы на одно устройство.</returns>
    Task<int> NotifyPublishedAsync(int hubGroupId, DateOnly date, int publisherUserId, CancellationToken ct = default);
}
