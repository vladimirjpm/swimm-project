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

    public MyHubGroupsController(
        IHubGroupAdminService admin, IHubGroupUserService mine, IHubGroupPermissionService permissions)
    {
        _admin = admin;
        _mine = mine;
        _permissions = permissions;
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

        var result = await _admin.DeleteAsync(id);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
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

        var result = await _admin.RemoveMemberAsync(id, memberId);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
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

public sealed class AddAdminRequest
{
    public string Email { get; set; } = "";
}
