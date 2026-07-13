using Microsoft.EntityFrameworkCore;
using Npgsql;
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
            .Where(g => g.OwnerUserId == userId || g.Admins.Any(m => m.UserId == userId))
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
                IsOfficial = g.IsOfficial,
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

        // allowClubChange:false — пользователь создаёт только свободную группу (как favorites);
        // привязка к клубу — через заявку и одобрение админа (8.7), не через ввод.
        var group = new HubGroup { OwnerUserId = ownerUserId };
        HubGroupCrudCore.Apply(group, input, slug, allowClubChange: false);
        await _core.ApplyCountryAsync(group, input.Country);
        _db.HubGroups.Add(group);
        return await _core.SaveAsync(group);
    }

    public async Task<HubGroupSaveResult> UpdateAsync(int id, HubGroupInputDto input)
    {
        var group = await _db.HubGroups.FindAsync(id);
        if (group == null) return HubGroupSaveResult.Fail($"Группа #{id} не найдена");

        var slug = await _core.ResolveSlugAsync(input, excludeId: id);
        var error = await _core.ValidateAsync(input, slug, excludeId: id);
        if (error != null) return HubGroupSaveResult.Fail(error);

        // allowClubChange:false — сохраняем текущий ClubId (в т.ч. у официальной группы),
        // ввод клуба игнорируем: сменить/снять клуб может только админ.
        HubGroupCrudCore.Apply(group, input, slug, allowClubChange: false);
        await _core.ApplyCountryAsync(group, input.Country);
        return await _core.SaveAsync(group);
    }

    public async Task<IReadOnlyList<HubGroupAdminMemberDto>> GetAdminsAsync(int hubGroupId)
    {
        return await _db.HubGroupAdmins.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId)
            .Include(m => m.User)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new HubGroupAdminMemberDto
            {
                UserId = m.UserId,
                DisplayName = m.User!.DisplayName,
                Email = m.User.Email,
                CreatedAt = m.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<HubGroupMemberSaveResult> AddAdminAsync(int hubGroupId, string email, int grantedByUserId)
    {
        email = (email ?? "").Trim();
        if (email.Length == 0) return HubGroupMemberSaveResult.Fail("Email обязателен");

        var groupExists = await _db.HubGroups.AnyAsync(g => g.Id == hubGroupId);
        if (!groupExists) return HubGroupMemberSaveResult.Fail($"Группа #{hubGroupId} не найдена");

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null) return HubGroupMemberSaveResult.Fail("Пользователь с таким email не найден");

        var dup = await _db.HubGroupAdmins.AnyAsync(m => m.HubGroupId == hubGroupId && m.UserId == user.Id);
        if (dup) return HubGroupMemberSaveResult.Fail("Этот пользователь уже админ группы");

        _db.HubGroupAdmins.Add(new HubGroupAdmin
        {
            HubGroupId = hubGroupId,
            UserId = user.Id,
            GrantedByUserId = grantedByUserId
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // 23505 = unique_violation по (HubGroupId, UserId) — гонка добавления того же юзера.
            return HubGroupMemberSaveResult.Fail("Этот пользователь уже админ группы");
        }
        catch (DbUpdateException)
        {
            // Прочие ошибки записи (напр. 23503 FK на невалидный GrantedByUserId) — не выдавать
            // за дубликат, это вводит в заблуждение (см. dev-bypass GrantedByUserId=0).
            return HubGroupMemberSaveResult.Fail("Не удалось назначить админа группы");
        }

        await _core.InvalidateCacheAsync();
        return HubGroupMemberSaveResult.Ok();
    }

    public async Task<HubGroupMemberSaveResult> RemoveAdminAsync(int hubGroupId, int adminUserId)
    {
        var admin = await _db.HubGroupAdmins
            .FirstOrDefaultAsync(m => m.HubGroupId == hubGroupId && m.UserId == adminUserId);
        if (admin == null) return HubGroupMemberSaveResult.Fail("Админ группы не найден");

        _db.HubGroupAdmins.Remove(admin);
        await _db.SaveChangesAsync();
        await _core.InvalidateCacheAsync();
        return HubGroupMemberSaveResult.Ok();
    }

    // ── Участники-аккаунты ───────────────────────────────────────────────────
    // Приватный список (Sys_-таблица) — публичная страница его не видит, кэш не трогаем.

    public async Task<HubGroupMemberSaveResult> AddUserMemberAsync(int hubGroupId, string email, int addedByUserId)
    {
        email = (email ?? "").Trim();
        if (email.Length == 0) return HubGroupMemberSaveResult.Fail("Email обязателен");

        var groupExists = await _db.HubGroups.AnyAsync(g => g.Id == hubGroupId);
        if (!groupExists) return HubGroupMemberSaveResult.Fail($"Группа #{hubGroupId} не найдена");

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null) return HubGroupMemberSaveResult.Fail("Пользователь с таким email не найден");

        return await InsertUserMemberAsync(hubGroupId, user.Id, addedByUserId);
    }

    public async Task<HubGroupMemberSaveResult> RemoveUserMemberAsync(int hubGroupId, int userId)
    {
        var member = await _db.HubGroupUserMembers
            .FirstOrDefaultAsync(m => m.HubGroupId == hubGroupId && m.UserId == userId);
        if (member == null) return HubGroupMemberSaveResult.Fail("Участник не найден");

        _db.HubGroupUserMembers.Remove(member);
        await _db.SaveChangesAsync();
        return HubGroupMemberSaveResult.Ok();
    }

    public async Task<HubGroupMemberSaveResult> JoinAsync(int hubGroupId, int userId)
    {
        var group = await _db.HubGroups.AsNoTracking()
            .Where(g => g.Id == hubGroupId)
            .Select(g => new { g.Id, g.IsPublic })
            .FirstOrDefaultAsync();
        if (group == null) return HubGroupMemberSaveResult.Fail($"Группа #{hubGroupId} не найдена");

        // Вступить можно только в публично видимую группу — тот же критерий, что у публичного
        // списка (HubGroupVisibility: private → никуда, perGroup → только IsPublic, public → любые).
        var visibility = _settings.GetValue("HubGroupVisibility", "public");
        var joinable = visibility switch
        {
            "private" => false,
            "perGroup" => group.IsPublic,
            _ => true,
        };
        if (!joinable) return HubGroupMemberSaveResult.Fail("В эту группу нельзя вступить");

        return await InsertUserMemberAsync(hubGroupId, userId, addedByUserId: null);
    }

    public async Task<HubGroupMemberSaveResult> LeaveAsync(int hubGroupId, int userId) =>
        await RemoveUserMemberAsync(hubGroupId, userId);

    public async Task<IReadOnlyList<JoinedHubGroupDto>> GetJoinedAsync(int userId)
    {
        return await _db.HubGroupUserMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.JoinedAt)
            .Select(m => new JoinedHubGroupDto
            {
                Id = m.HubGroup!.Id,
                Name = m.HubGroup.Name,
                Slug = m.HubGroup.Slug,
                IconUrl = m.HubGroup.IconUrl,
                Status = m.Status,
                JoinedAt = m.JoinedAt
            })
            .ToListAsync();
    }

    /// <summary>Общая вставка участника-аккаунта: dedup + обработка гонки (23505).</summary>
    private async Task<HubGroupMemberSaveResult> InsertUserMemberAsync(int hubGroupId, int userId, int? addedByUserId)
    {
        var dup = await _db.HubGroupUserMembers.AnyAsync(m => m.HubGroupId == hubGroupId && m.UserId == userId);
        if (dup) return HubGroupMemberSaveResult.Fail("Этот пользователь уже участник группы");

        _db.HubGroupUserMembers.Add(new HubGroupUserMember
        {
            HubGroupId = hubGroupId,
            UserId = userId,
            AddedByUserId = addedByUserId,
            Status = HubGroupUserMemberStatus.Active
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            return HubGroupMemberSaveResult.Fail("Этот пользователь уже участник группы");
        }
        catch (DbUpdateException)
        {
            return HubGroupMemberSaveResult.Fail("Не удалось добавить участника");
        }

        return HubGroupMemberSaveResult.Ok();
    }

    public async Task<MyHubGroupClubRequestDto?> GetClubRequestAsync(int hubGroupId)
    {
        return await _db.HubGroupClubRequests.AsNoTracking()
            .Where(r => r.HubGroupId == hubGroupId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new MyHubGroupClubRequestDto
            {
                Id = r.Id,
                ClubId = r.ClubId,
                ClubName = r.Club!.Name,
                Message = r.Message,
                Status = r.Status,
                CreatedAt = r.CreatedAt,
                DecidedAt = r.DecidedAt
            })
            .FirstOrDefaultAsync();
    }

    public async Task<HubGroupMemberSaveResult> SubmitClubRequestAsync(int hubGroupId, int userId, HubGroupClubRequestInputDto input)
    {
        var group = await _db.HubGroups.FindAsync(hubGroupId);
        if (group == null) return HubGroupMemberSaveResult.Fail($"Группа #{hubGroupId} не найдена");
        if (group.IsOfficial) return HubGroupMemberSaveResult.Fail("Группа уже официальная");

        var clubExists = await _db.Clubs.AnyAsync(c => c.Id == input.ClubId);
        if (!clubExists) return HubGroupMemberSaveResult.Fail("Клуб не найден");

        var hasPending = await _db.HubGroupClubRequests
            .AnyAsync(r => r.HubGroupId == hubGroupId && r.Status == HubGroupClubRequestStatus.Pending);
        if (hasPending) return HubGroupMemberSaveResult.Fail("Заявка на эту группу уже подана и ожидает решения");

        var clubTaken = await _db.HubGroups.AnyAsync(g => g.ClubId == input.ClubId && g.IsOfficial);
        if (clubTaken) return HubGroupMemberSaveResult.Fail("У этого клуба уже есть официальная группа");

        _db.HubGroupClubRequests.Add(new HubGroupClubRequest
        {
            HubGroupId = hubGroupId,
            UserId = userId,
            ClubId = input.ClubId,
            Message = string.IsNullOrWhiteSpace(input.Message) ? null : input.Message.Trim(),
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return HubGroupMemberSaveResult.Fail("Заявка на эту группу уже подана и ожидает решения");
        }

        return HubGroupMemberSaveResult.Ok();
    }
}
