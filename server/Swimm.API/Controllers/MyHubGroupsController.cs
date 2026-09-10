using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Controllers;

/// <summary>
/// Пользовательское самообслуживание групп (HubGroups, 8.6): создание своей группы
/// (по настройкам HubGroupCreationPolicy/HubGroupMaxPerUser), правка/удаление своей
/// группы и участников, назначение админов группы. Права — единая проверка через
/// <see cref="IHubGroupPermissionService"/> (владелец/админ группы/site-админ), не размазана
/// по хендлерам. CRUD самой группы/участников переиспользует <see cref="IHubGroupAdminService"/> —
/// логика идентична админской, разница только в авторизации на входе.
/// </summary>
[ApiController]
[Route("api/me/hub-groups")]
[Authorize]
[AutoValidateAntiforgeryToken]
public class MyHubGroupsController : ControllerBase
{
    private readonly IHubGroupAdminService _admin;
    private readonly IHubGroupUserService _mine;
    private readonly IHubGroupPermissionService _permissions;
    private readonly IHubGroupClubSubscriptionService _clubSubscriptions;

    public MyHubGroupsController(
        IHubGroupAdminService admin, IHubGroupUserService mine, IHubGroupPermissionService permissions,
        IHubGroupClubSubscriptionService clubSubscriptions)
    {
        _admin = admin;
        _mine = mine;
        _permissions = permissions;
        _clubSubscriptions = clubSubscriptions;
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    private bool IsAdmin => User.IsInRole("Admin");
    private bool IsCoach => User.IsInRole("Coach");

    private async Task<HubGroupPermissions?> RequirePermissionsAsync(int hubGroupId)
    {
        var userId = CurrentUserId();
        if (userId == null) return null;
        return await _permissions.GetPermissionsAsync(hubGroupId, userId.Value, IsAdmin);
    }

    /// <summary>Группы, которыми текущий пользователь владеет или админит.</summary>
    [HttpGet]
    public async Task<IActionResult> GetMine()
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();
        return Ok(await _mine.GetMineAsync(userId.Value));
    }

    /// <summary>Можно ли создать ещё одну группу — для показа кнопки «Создать группу».</summary>
    [HttpGet("create-eligibility")]
    public async Task<IActionResult> GetCreateEligibility()
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();
        return Ok(await _mine.GetCreateEligibilityAsync(userId.Value, IsAdmin, IsCoach));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] HubGroupInputDto input)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var result = await _mine.CreateAsync(input, userId.Value, IsAdmin, IsCoach);
        if (!result.Success) return BadRequest(new { error = result.Error });
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, new { id = result.Id });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();

        var dto = await _admin.GetByIdAsync(id);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] HubGroupInputDto input)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();

        // Пользовательский Update (не _admin): не даёт менять ClubId в обход одобрения (8.7).
        var result = await _mine.UpdateAsync(id, input);
        return result.Success ? Ok(new { id = result.Id }) : BadRequest(new { error = result.Error });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanDelete) return Forbid();

        // Аудит hubgroup.delete пишет сам DeleteAsync — он общий для всех путей удаления.
        var result = await _admin.DeleteAsync(id);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
    }

    /// <summary>
    /// Что уйдёт вместе с группой — для подтверждения удаления. Права те же, что у DELETE:
    /// перечень нужен только тому, кто может удалить. Этим же пользуются страницы
    /// /Admin/HubGroups — у админа сайта CanDelete на любую группу.
    /// </summary>
    [HttpGet("{id:int}/delete-impact")]
    public async Task<IActionResult> GetDeleteImpact(int id)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanDelete) return Forbid();

        var impact = await _admin.GetDeleteImpactAsync(id);
        return impact == null ? NotFound() : Ok(impact);
    }

    /// <summary>Поиск пловцов для добавления участника — та же выборка, что в админке.</summary>
    [HttpGet("search-swimmers")]
    public async Task<IActionResult> SearchSwimmers([FromQuery] string q)
        => Ok(await _admin.SearchSwimmersAsync(q));

    /// <summary>Справочник клубов — для select в форме заявки на официальный статус.</summary>
    [HttpGet("clubs")]
    public async Task<IActionResult> GetClubs() => Ok(await _admin.GetClubOptionsAsync());

    /// <summary>
    /// Пловцы клуба — доп. фильтр комплектования официальной группы (в дополнение к обычному
    /// поиску). Не право доступа: пловцы публичны, доступно любому авторизованному.
    /// </summary>
    [HttpGet("club-swimmers")]
    public async Task<IActionResult> GetClubSwimmers([FromQuery] int clubId)
        => Ok(await _admin.GetClubSwimmersAsync(clubId));

    [HttpPost("{id:int}/members")]
    public async Task<IActionResult> AddMember(int id, [FromBody] AddMemberRequest request)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();

        var result = await _admin.AddMemberAsync(id, request.SwimmerId, request.Role);
        return result.Success ? Ok() : BadRequest(new { error = result.Error });
    }

    [HttpPut("{id:int}/members/{memberId:int}")]
    public async Task<IActionResult> UpdateMember(int id, int memberId, [FromBody] UpdateMemberRequest request)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();

        var result = await _admin.UpdateMemberAsync(id, memberId, request.Role, request.SortOrder);
        return result.Success ? Ok() : BadRequest(new { error = result.Error });
    }

    [HttpDelete("{id:int}/members/{memberId:int}")]
    public async Task<IActionResult> RemoveMember(int id, int memberId)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();

        // Клубного пловца (и ручного, который есть в клубе подписки) это не удаляет, а
        // скрывает — иначе следующая пересборка вернула бы его. См. HubGroupCrudCore.RemoveMemberAsync.
        var result = await _admin.RemoveMemberAsync(id, memberId);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
    }

    // ── Подписка на клуб (docs/plans/hubgroup-club-subscription-plan.md П2) ──────

    /// <summary>Подписка группы на клуб; 204 — группа ни на кого не подписана.</summary>
    [HttpGet("{id:int}/club-subscription")]
    public async Task<IActionResult> GetClubSubscription(int id)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();

        var subscription = await _clubSubscriptions.GetAsync(id);
        return subscription == null ? NoContent() : Ok(subscription);
    }

    /// <summary>
    /// Предпросмотр подписки до кнопки Follow: сколько пловцов придёт, предупреждение про
    /// официальную группу клуба и подсказка «вступить в существующую». Ничего не пишет.
    /// </summary>
    [HttpGet("{id:int}/club-subscription-preview")]
    public async Task<IActionResult> PreviewClubSubscription(int id, [FromQuery] int clubId)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();

        var preview = await _clubSubscriptions.PreviewAsync(id, clubId);
        return preview == null ? NotFound(new { error = "Club not found." }) : Ok(preview);
    }

    /// <summary>
    /// Подписать группу на клуб (другой клуб заменяет прежний — подписка одна) и сразу
    /// пересобрать состав. В ответе — подписка и сколько пловцов пришло/ушло.
    /// </summary>
    [HttpPut("{id:int}/club-subscription")]
    public async Task<IActionResult> SubscribeToClub(int id, [FromBody] ClubSubscriptionRequest request)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();

        var result = await _clubSubscriptions.SubscribeAsync(id, request.ClubId, CurrentUserId());
        return result.Success
            ? Ok(new { subscription = result.Subscription, added = result.Sync.Added, removed = result.Sync.Removed })
            : BadRequest(new { error = result.Error });
    }

    /// <summary>Снять подписку: клубные пловцы уходят (скрытые тоже), ручные остаются.</summary>
    [HttpDelete("{id:int}/club-subscription")]
    public async Task<IActionResult> UnsubscribeFromClub(int id)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();

        var result = await _clubSubscriptions.UnsubscribeAsync(id);
        return result == null ? NotFound(new { error = "The group does not follow a club." }) : Ok(new { removed = result.Removed });
    }

    /// <summary>
    /// Скрыть / вернуть клубного пловца (владелец/админ группы). Скрытый не возвращается
    /// пересборкой и не виден ни на одной витрине; ручного так не прячут — его удаляют.
    /// </summary>
    [HttpPut("{id:int}/members/{memberId:int}/excluded")]
    public async Task<IActionResult> SetMemberExcluded(int id, int memberId, [FromBody] SetMemberExcludedRequest request)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();

        var result = await _clubSubscriptions.SetExcludedAsync(id, memberId, request.Excluded);
        return result.Success ? Ok() : BadRequest(new { error = result.Error });
    }

    [HttpGet("{id:int}/admins")]
    public async Task<IActionResult> GetAdmins(int id)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanManageAdmins) return Forbid();

        return Ok(await _mine.GetAdminsAsync(id));
    }

    [HttpPost("{id:int}/admins")]
    public async Task<IActionResult> AddAdmin(int id, [FromBody] AddAdminRequest request)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanManageAdmins) return Forbid();

        var result = await _mine.AddAdminAsync(id, request.Email, CurrentUserId()!.Value);
        return result.Success ? Ok() : BadRequest(new { error = result.Error });
    }

    [HttpDelete("{id:int}/admins/{userId:int}")]
    public async Task<IActionResult> RemoveAdmin(int id, int userId)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanManageAdmins) return Forbid();

        var result = await _mine.RemoveAdminAsync(id, userId);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
    }

    // ── Участники-аккаунты (приватный состав из юзеров) ──────────────────────

    /// <summary>Группы, в которые текущий пользователь вступил как участник (список «участвую»).</summary>
    [HttpGet("joined")]
    public async Task<IActionResult> GetJoined()
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();
        return Ok(await _mine.GetJoinedAsync(userId.Value));
    }

    /// <summary>Самозапись: вступить в публичную группу (мгновенно).</summary>
    [HttpPost("{id:int}/join")]
    public async Task<IActionResult> Join(int id)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();
        var result = await _mine.JoinAsync(id, userId.Value);
        return result.Success ? Ok() : BadRequest(new { error = result.Error });
    }

    /// <summary>Самовыход из группы.</summary>
    [HttpDelete("{id:int}/join")]
    public async Task<IActionResult> Leave(int id)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();
        var result = await _mine.LeaveAsync(id, userId.Value);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
    }

    /// <summary>Добавить участника-аккаунт по email (владелец/админ группы).</summary>
    [HttpPost("{id:int}/user-members")]
    public async Task<IActionResult> AddUserMember(int id, [FromBody] AddUserMemberRequest request)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();

        var result = await _mine.AddUserMemberAsync(id, request.Email, CurrentUserId()!.Value, request.SwimmerId, request.Note);
        return result.Success ? Ok() : BadRequest(new { error = result.Error });
    }

    /// <summary>Проставить/снять ярлык «родитель пловца» у участника-аккаунта (владелец/админ группы).</summary>
    [HttpPut("{id:int}/user-members/{userId:int}/label")]
    public async Task<IActionResult> SetUserMemberLabel(int id, int userId, [FromBody] SetUserMemberLabelRequest request)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();

        var result = await _mine.SetUserMemberLabelAsync(id, userId, request.SwimmerId, request.Note);
        return result.Success ? Ok() : BadRequest(new { error = result.Error });
    }

    /// <summary>Одобрить заявку участника-аккаунта (pending → active). Владелец/админ группы.</summary>
    [HttpPost("{id:int}/user-members/{userId:int}/approve")]
    public async Task<IActionResult> ApproveUserMember(int id, int userId)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();

        var result = await _mine.ApproveUserMemberAsync(id, userId);
        return result.Success ? Ok() : BadRequest(new { error = result.Error });
    }

    /// <summary>
    /// Политика вступления: open (сразу active) | approval (заявка ждёт решения).
    /// Владелец/админ группы. Тумблер живёт в табе Admin страницы группы.
    /// </summary>
    [HttpPut("{id:int}/join-policy")]
    public async Task<IActionResult> SetJoinPolicy(int id, [FromBody] SetJoinPolicyRequest request)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();

        var result = await _mine.SetJoinPolicyAsync(id, request.JoinPolicy);
        return result.Success ? Ok() : BadRequest(new { error = result.Error });
    }

    /// <summary>
    /// Заменить расписание тренировок (владелец/админ группы). Пустой список слотов убирает
    /// расписание — слоты шапки тогда скрываются.
    /// </summary>
    [HttpPut("{id:int}/training-schedule")]
    public async Task<IActionResult> SetTrainingSchedule(int id, [FromBody] GroupTrainingScheduleDto? request)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();

        var result = await _mine.SetTrainingScheduleAsync(id, request);
        return result.Success ? Ok() : BadRequest(new { error = result.Error });
    }

    /// <summary>Убрать участника-аккаунт (владелец/админ группы).</summary>
    [HttpDelete("{id:int}/user-members/{userId:int}")]
    public async Task<IActionResult> RemoveUserMember(int id, int userId)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();

        var result = await _mine.RemoveUserMemberAsync(id, userId);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
    }

    /// <summary>Последняя заявка на официальный статус группы (любого статуса) — для панели «Моя группа».</summary>
    [HttpGet("{id:int}/club-request")]
    public async Task<IActionResult> GetClubRequest(int id)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();

        var request = await _mine.GetClubRequestAsync(id);
        return request == null ? NoContent() : Ok(request);
    }

    /// <summary>
    /// Подать заявку на официальный статус группы. Только владелец (не админ группы) —
    /// связь с клубом и последующая site-роль Coach касаются владельца, не назначенных админов.
    /// </summary>
    [HttpPost("{id:int}/club-request")]
    public async Task<IActionResult> SubmitClubRequest(int id, [FromBody] HubGroupClubRequestInputDto input)
    {
        var perms = await RequirePermissionsAsync(id);
        if (perms == null) return Unauthorized();
        if (!perms.Exists) return NotFound();
        if (!perms.IsOwner && !perms.IsAdmin) return Forbid();

        var result = await _mine.SubmitClubRequestAsync(id, CurrentUserId()!.Value, input);
        return result.Success ? Ok() : BadRequest(new { error = result.Error });
    }
}

public sealed class AddMemberRequest
{
    public int SwimmerId { get; set; }
    public string Role { get; set; } = "member";
}

public sealed class UpdateMemberRequest
{
    public string Role { get; set; } = "member";
    public int SortOrder { get; set; }
}

public sealed class ClubSubscriptionRequest
{
    public int ClubId { get; set; }
}

public sealed class SetMemberExcludedRequest
{
    public bool Excluded { get; set; }
}

public sealed class AddAdminRequest
{
    public string Email { get; set; } = "";
}

public sealed class AddUserMemberRequest
{
    public string Email { get; set; } = "";
    /// <summary>Опциональный ярлык «за какого пловца» (напр. родитель).</summary>
    public int? SwimmerId { get; set; }
    public string? Note { get; set; }
}

public sealed class SetUserMemberLabelRequest
{
    /// <summary>null — снять ярлык.</summary>
    public int? SwimmerId { get; set; }
    public string? Note { get; set; }
}

public sealed class SetJoinPolicyRequest
{
    /// <summary>open | approval (см. HubGroupJoinPolicy); значение валидирует сервис.</summary>
    public string JoinPolicy { get; set; } = "";
}
