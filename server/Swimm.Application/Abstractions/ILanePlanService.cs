using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.Application.Abstractions;

/// <summary>
/// План дорожек занятия группы (docs/plans/lane-plans-plan.md, L2; ключ — занятие с CF5 плана
/// trainings-coach-flow, <see cref="LanePlanKey"/>). Правила формы — <c>LanePlanRules</c>, раскладка —
/// <c>LaneAllocation</c>. Запись — только по ключу занятия (<see cref="LanePlanKey.IsSession"/>):
/// план дня без занятия — архив только для чтения; проверяет контроллер.
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
    /// Доска занятия; null — плана нет (или он черновик, а зритель не управляющий). По голой дате
    /// (старые адреса Lanes) — план дня без занятия, иначе первый по времени план этой даты.
    /// <paramref name="viewerUserId"/> задан — в ответе его пловцы плана (<c>my_swimmers</c>).
    /// </summary>
    Task<LanePlanDto?> GetAsync(int hubGroupId, LanePlanKey key, bool isManager, int? viewerUserId = null);

    /// <summary>
    /// Кто из видимого состава на занятии <paramref name="key"/> «не в воде» (On break / Doesn't
    /// train без «Going» на это занятие, или «Can't come» на него) — для «Not today» нового плана
    /// в редакторе. Только управляющему.
    /// </summary>
    Task<LanePlanAwayDto> GetAwayAsync(int hubGroupId, LanePlanKey key);

    /// <summary>Зритель открыл вид Lanes (Т1б): гаснет золотая метка «тебя перенесли».</summary>
    Task MarkSeenAsync(int hubGroupId, int viewerUserId);

    /// <summary>
    /// Сохранить план целиком (создать черновиком, если его нет — только у существующего занятия, CF5;
    /// статус существующего не меняется). Уровни дорожек — только этой группы; пловцы — видимого состава или уже
    /// стоявшие в этом плане (ушедший из группы остаётся в снимке).
    /// </summary>
    Task<HubGroupMemberSaveResult> SaveAsync(int hubGroupId, LanePlanKey key, LanePlanInputDto input, int userId);

    /// <summary>
    /// Сохранить workout занятия (CF6, §5.5 плана trainings-coach-flow): документ целиком, null —
    /// стереть. План должен быть (workout живёт при нём и публикуется с ним): нет — <c>Found</c>
    /// false. Уровни — только этой группы. Статус плана не меняется.
    /// </summary>
    Task<(bool Found, string? Error)> SaveWorkoutAsync(int hubGroupId, LanePlanKey key, WorkoutDto? workout);

    /// <summary>
    /// «Auto lanes»: поделить дорожки между уровнями по числу пришедших и разложить людей
    /// (<c>LaneAllocation</c>). Ничего не пишет. Ошибка — ни у кого из пришедших нет уровня.
    /// </summary>
    Task<(LanePlanAutoLanesDto? Result, string? Error)> AutoLanesAsync(int hubGroupId, LanePlanAutoLanesInputDto input);

    /// <summary>
    /// Идущие пловцы без уровня, которые не стоят на дорожке (решение 10 плана
    /// trainings-coach-flow): пока они есть, план не публикуется. null — плана нет.
    /// </summary>
    Task<LanePlanPublishCheckDto?> GetPublishCheckAsync(int hubGroupId, LanePlanKey key);

    /// <summary>
    /// Поставить идущего пловца на дорожку его уровня по правилу ответа «Going» (§5.3 плана
    /// trainings-coach-flow, CF4) — после того, как тренер задал ему уровень в карточке человека.
    /// Уже на дорожке — не двигает. null — плана нет или сдвига нет.
    /// </summary>
    Task<LaneChangeDto?> PlaceAsync(int hubGroupId, LanePlanKey key, int swimmerId, int actorUserId);

    /// <summary>
    /// Опубликовать с проверкой решения 10 (<see cref="GetPublishCheckAsync"/>). Лента «Since
    /// publishing» (CF8) начинается заново — первая строка «<paramref name="actorUserId"/> опубликовал».
    /// </summary>
    Task<LanePlanPublishResult> PublishAsync(int hubGroupId, LanePlanKey key, int actorUserId);

    /// <summary>
    /// Сменить статус без проверок: вернуть в черновик (Unpublish). Публикацию из интерфейса ведёт
    /// <see cref="PublishAsync"/>. false — плана нет. Возврат в черновик стирает ленту «Since publishing».
    /// </summary>
    Task<bool> SetStatusAsync(int hubGroupId, LanePlanKey key, string status, int? actorUserId = null);

    /// <summary>
    /// Лента «Since publishing» плана занятия (CF8), новые сверху; только управляющему. Пусто — план
    /// черновик (ленты нет) или ничего не менялось; null — у занятия плана нет.
    /// </summary>
    Task<List<LanePlanEventDto>?> GetEventsAsync(int hubGroupId, LanePlanKey key, int viewerUserId);

    /// <summary>Удалить план. false — плана нет.</summary>
    Task<bool> DeleteAsync(int hubGroupId, LanePlanKey key);
}
