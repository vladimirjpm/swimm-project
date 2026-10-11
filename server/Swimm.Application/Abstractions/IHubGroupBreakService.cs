using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Флаг «On break» людей группы (Ш3.1, <c>HubGroupBreakRules</c>). Права решает контроллер:
/// смотреть — управляющий или активный участник-аккаунт; ставить за другого — только управляющий.
/// </summary>
public interface IHubGroupBreakService
{
    Task<HubGroupBreaksDto> GetAsync(int hubGroupId, int viewerUserId, bool isManager, DateTime? nowUtc = null);

    Task<HubGroupBreakSaveResult> SetAsync(int hubGroupId, int actorUserId, bool isManager,
        HubGroupBreakInputDto input, DateTime? nowUtc = null);
}
