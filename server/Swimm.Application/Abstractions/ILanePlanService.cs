using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// План дорожек группы на дату (docs/plans/lane-plans-plan.md, L2). Правила формы —
/// <c>LanePlanRules</c>, раскладка — <c>LaneDistribution</c>.
///
/// Права здесь НЕ проверяются: контроллер уже решил, управляющий ли зритель (CanEdit) или
/// активный участник. Флаг <c>isManager</c> только выбирает, что отдать: черновики и
/// «Not today» — управляющему. Данные приватные (Sys_), без кэша.
/// </summary>
public interface ILanePlanService
{
    /// <summary>Планы группы, новые сверху; участнику — только опубликованные.</summary>
    Task<List<LanePlanSummaryDto>> ListAsync(int hubGroupId, bool isManager);

    /// <summary>
    /// Доска на дату; null — плана нет (или он черновик, а зритель не управляющий).
    /// <paramref name="viewerUserId"/> задан — в ответе его пловцы плана (<c>my_swimmers</c>).
    /// </summary>
    Task<LanePlanDto?> GetAsync(int hubGroupId, DateOnly date, bool isManager, int? viewerUserId = null);

    /// <summary>
    /// Сохранить план целиком (создать черновиком, если его нет; статус существующего не
    /// меняется). Уровни дорожек — только этой группы; пловцы — видимого состава или уже
    /// стоявшие в этом плане (ушедший из группы остаётся в снимке).
    /// </summary>
    Task<HubGroupMemberSaveResult> SaveAsync(int hubGroupId, DateOnly date, LanePlanInputDto input, int userId);

    /// <summary>Раскладка «Distribute» по текущим уровням и порядку состава. Ничего не пишет.</summary>
    Task<(LanePlanDistributionDto? Result, string? Error)> DistributeAsync(int hubGroupId, LanePlanDistributeInputDto input);

    /// <summary>
    /// «Auto lanes»: поделить дорожки между уровнями по числу пришедших и разложить людей
    /// (<c>LaneAllocation</c>). Ничего не пишет. Ошибка — ни у кого из пришедших нет уровня.
    /// </summary>
    Task<(LanePlanAutoLanesDto? Result, string? Error)> AutoLanesAsync(int hubGroupId, LanePlanAutoLanesInputDto input);

    /// <summary>Опубликовать / вернуть в черновик. false — плана нет.</summary>
    Task<bool> SetStatusAsync(int hubGroupId, DateOnly date, string status);

    /// <summary>Удалить план. false — плана нет.</summary>
    Task<bool> DeleteAsync(int hubGroupId, DateOnly date);
}
