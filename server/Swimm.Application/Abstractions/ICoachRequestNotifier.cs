namespace Swimm.Application.Abstractions;

/// <summary>
/// Push владельцу группы о новой заявке тренера (docs/plans/coach-access-request-plan.md, К2/Т3).
/// Только владельцу — не админам группы и не админам сайта. Push не настроен или у владельца нет
/// подписок — молча ничего: счётчик на табе Admin он увидит и так. Ошибки доставки не бросает.
/// </summary>
public interface ICoachRequestNotifier
{
    /// <returns>Сколько устройств приняли push (для тестов и лога).</returns>
    Task<int> NotifyOwnerAsync(int hubGroupId, int requesterUserId, CancellationToken ct = default);
}
