using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Чтение приватных тренировок группы (Sys_TrainingSessions/Sys_TrainingResults) для вкладки
/// «Тренировки». Доступ проверяет контроллер через <see cref="IHubGroupPermissionService"/> —
/// репозиторий данные приватности не решает, только отдаёт.
/// </summary>
public interface IHubGroupTrainingRepository
{
    /// <summary>Id группы по slug (для проверки прав до отдачи данных); null — группы нет.</summary>
    Task<int?> ResolveGroupIdBySlugAsync(string slug);

    /// <summary>
    /// Активный участник-аккаунт группы (Sys_HubGroupUserMembers, Status='active') — тренировки
    /// видят ВСЕ участники группы, не только владелец/админ. См. hubgroups-architecture.md §4/§7.
    /// </summary>
    Task<bool> IsActiveAccountMemberAsync(int hubGroupId, int userId);

    /// <summary>Все тренировки группы в форме клиентского ResultWrap (готова для TrainingTable).</summary>
    Task<TrainingSourceDto> GetTrainingsAsync(int hubGroupId);

    /// <summary>
    /// Повторы этих сессий группы — для ленты тренировок (показатели, «You», «Sets & times»;
    /// docs/plans/trainings-tab-redesign-plan.md, Т1а). Сессии чужой группы молча не попадают.
    /// Аудиторию проверяет вызывающий до вызова.
    /// </summary>
    Task<List<TrainingFeed.Repeat>> GetRepeatsAsync(int hubGroupId, IReadOnlyCollection<int> sessionIds);
}
