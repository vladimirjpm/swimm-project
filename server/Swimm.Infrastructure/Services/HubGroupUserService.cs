using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="IHubGroupUserService"/>
public class HubGroupUserService : IHubGroupUserService
{
    private readonly SwimmDbContext _db;
    private readonly HubGroupCrudCore _core;
    private readonly ISettingsService _settings;

    public HubGroupUserService(SwimmDbContext db, HubGroupCrudCore core, ISettingsService settings)
    {
        _db = db;
        _core = core;
        _settings = settings;
    }

    private string Policy => _settings.GetValue("HubGroupCreationPolicy", "admin");
    private int MaxPerUser => _settings.GetValue("HubGroupMaxPerUser", 3);

    public async Task<IReadOnlyList<HubGroupAdminRowDto>> GetMineAsync(int userId)
    {
        return await _db.HubGroups.AsNoTracking()
            .Where(g => g.OwnerUserId == userId || g.Managers.Any(m => m.UserId == userId))
            .OrderByDescending(g => g.UpdatedAt)
            .Select(g => new HubGroupAdminRowDto
            {
                Id = g.Id,
                Name = g.Name,
                Slug = g.Slug,
                IconUrl = g.IconUrl,
                ClubName = g.Club != null ? g.Club.Name : null,
                MemberCount = g.Members.Count,
                IsPublic = g.IsPublic,
                UpdatedAt = g.UpdatedAt
            })
            .ToListAsync();
    }

    public async Task<HubGroupCreateEligibilityDto> GetCreateEligibilityAsync(int userId, bool isAdmin, bool isCoach)
    {
        if (isAdmin) return new HubGroupCreateEligibilityDto { CanCreate = true, Remaining = null };

        var policy = Policy;
        if (policy == "admin")
            return new HubGroupCreateEligibilityDto { CanCreate = false, Reason = "Создание групп сейчас разрешено только администратору." };
        if (policy == "coach" && !isCoach)
            return new HubGroupCreateEligibilityDto { CanCreate = false, Reason = "Создание групп сейчас разрешено только тренерам." };

        var owned = await _db.HubGroups.CountAsync(g => g.OwnerUserId == userId);
        var remaining = MaxPerUser - owned;
        if (remaining <= 0)
            return new HubGroupCreateEligibilityDto { CanCreate = false, Reason = $"Достигнут лимит групп на пользователя ({MaxPerUser}).", Remaining = 0 };

        return new HubGroupCreateEligibilityDto { CanCreate = true, Remaining = remaining };
    }

    public async Task<HubGroupSaveResult> CreateAsync(HubGroupInputDto input, int ownerUserId, bool isAdmin, bool isCoach)
    {
        var eligibility = await GetCreateEligibilityAsync(ownerUserId, isAdmin, isCoach);
        if (!eligibility.CanCreate)
            return HubGroupSaveResult.Fail(eligibility.Reason ?? "Создание группы недоступно.");

        var slug = await _core.ResolveSlugAsync(input, excludeId: null);
        var error = await _core.ValidateAsync(input, slug, excludeId: null);
        if (error != null) return HubGroupSaveResult.Fail(error);

        var group = new HubGroup { OwnerUserId = ownerUserId };
        HubGroupCrudCore.Apply(group, input, slug);
        _db.HubGroups.Add(group);
        return await _core.SaveAsync(group);
    }

    public async Task<IReadOnlyList<HubGroupManagerDto>> GetManagersAsync(int hubGroupId)
    {
        return await _db.HubGroupManagers.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId)
            .Include(m => m.User)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new HubGroupManagerDto
            {
                UserId = m.UserId,
                DisplayName = m.User!.DisplayName,
                Email = m.User.Email,
                CreatedAt = m.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<HubGroupMemberSaveResult> AddManagerAsync(int hubGroupId, string email, int grantedByUserId)
    {
        email = (email ?? "").Trim();
        if (email.Length == 0) return HubGroupMemberSaveResult.Fail("Email обязателен");

        var groupExists = await _db.HubGroups.AnyAsync(g => g.Id == hubGroupId);
        if (!groupExists) return HubGroupMemberSaveResult.Fail($"Группа #{hubGroupId} не найдена");

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null) return HubGroupMemberSaveResult.Fail("Пользователь с таким email не найден");

        var dup = await _db.HubGroupManagers.AnyAsync(m => m.HubGroupId == hubGroupId && m.UserId == user.Id);
        if (dup) return HubGroupMemberSaveResult.Fail("Этот пользователь уже со-тренер группы");

        _db.HubGroupManagers.Add(new HubGroupManager
        {
            HubGroupId = hubGroupId,
            UserId = user.Id,
            GrantedByUserId = grantedByUserId
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return HubGroupMemberSaveResult.Fail("Этот пользователь уже со-тренер группы");
        }

        await _core.InvalidateCacheAsync();
        return HubGroupMemberSaveResult.Ok();
    }

    public async Task<HubGroupMemberSaveResult> RemoveManagerAsync(int hubGroupId, int managerUserId)
    {
        var manager = await _db.HubGroupManagers
            .FirstOrDefaultAsync(m => m.HubGroupId == hubGroupId && m.UserId == managerUserId);
        if (manager == null) return HubGroupMemberSaveResult.Fail("Со-тренер не найден");

        _db.HubGroupManagers.Remove(manager);
        await _db.SaveChangesAsync();
        await _core.InvalidateCacheAsync();
        return HubGroupMemberSaveResult.Ok();
    }
}
