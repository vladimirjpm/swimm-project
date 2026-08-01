using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Админский CRUD групп (см. <see cref="IHubGroupAdminService"/>). Пишет через owner-контекст
/// <see cref="SwimmDbContext"/>. После мутаций инвалидирует общий кэш — публичные
/// /api/hub-groups* (фаза 3) кэшируются через ICacheService, как остальные публичные GET.
/// Write-логика (slug/валидация/PostgresException/участники) — общая с пользовательским
/// CRUD (8.6) в <see cref="HubGroupCrudCore"/>.
/// </summary>
public class HubGroupAdminService : IHubGroupAdminService
{
    private readonly SwimmDbContext _db;
    private readonly HubGroupCrudCore _core;

    public HubGroupAdminService(SwimmDbContext db, HubGroupCrudCore core)
    {
        _db = db;
        _core = core;
    }

    public async Task<IReadOnlyList<HubGroupAdminRowDto>> GetAllAsync()
    {
        return await _db.HubGroups.AsNoTracking()
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

    public async Task<HubGroupEditDto?> GetByIdAsync(int id)
    {
        var g = await _db.HubGroups.AsNoTracking()
            .Include(x => x.Owner)
            .Include(x => x.Country)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (g == null) return null;

        var members = await _db.HubGroupMembers.AsNoTracking()
            .Where(m => m.HubGroupId == id)
            .Include(m => m.Swimmer).ThenInclude(s => s!.Club)
            .OrderBy(m => m.SortOrder)
            .Select(m => new HubGroupMemberRowDto
            {
                Id = m.Id,
                SwimmerId = m.SwimmerId,
                SwimmerName = (m.Swimmer!.LastName + " " + m.Swimmer.FirstName).Trim(),
                SwimmerNameEn = (m.Swimmer.LastNameEn + " " + m.Swimmer.FirstNameEn).Trim(),
                BirthYear = m.Swimmer.BirthYear,
                ClubName = m.Swimmer.Club != null ? m.Swimmer.Club.Name : null,
                Role = m.Role,
                SortOrder = m.SortOrder
            })
            .ToListAsync();

        var userMembers = await _db.HubGroupUserMembers.AsNoTracking()
            .Where(m => m.HubGroupId == id)
            .Include(m => m.User)
            .Include(m => m.Swimmer)
            .OrderBy(m => m.JoinedAt)
            .Select(m => new HubGroupUserMemberRowDto
            {
                UserId = m.UserId,
                DisplayName = m.User!.DisplayName,
                Email = m.User.Email,
                Status = m.Status,
                SelfJoined = m.AddedByUserId == null,
                JoinedAt = m.JoinedAt,
                SwimmerId = m.SwimmerId,
                SwimmerName = m.Swimmer != null ? (m.Swimmer.LastName + " " + m.Swimmer.FirstName) : null,
                Note = m.Note
            })
            .ToListAsync();

        return new HubGroupEditDto
        {
            Id = g.Id,
            Name = g.Name,
            NameEn = g.NameEn,
            Slug = g.Slug,
            Description = g.Description,
            IconUrl = g.IconUrl,
            CoverImageUrl = g.CoverImageUrl,
            Location = g.Location,
            Country = g.Country?.CountryCode,
            ClubId = g.ClubId,
            OwnerUserId = g.OwnerUserId,
            OwnerDisplayName = g.Owner?.DisplayName ?? $"#{g.OwnerUserId}",
            IsPublic = g.IsPublic,
            IsOfficial = g.IsOfficial,
            JoinPolicy = g.JoinPolicy,
            Links = HubGroupCrudCore.ParseLinks(g.Links),
            Members = members,
            UserMembers = userMembers
        };
    }

    public async Task<IReadOnlyList<ClubOptionDto>> GetClubOptionsAsync()
    {
        return await _db.Clubs.AsNoTracking()
            .Where(c => c.MergedIntoId == null)   // склеенные в выпадашку не предлагаем
            .OrderBy(c => c.Name)
            .Select(c => new ClubOptionDto { Id = c.Id, Name = c.Name })
            .ToListAsync();
    }

    public async Task<HubGroupSaveResult> CreateAsync(HubGroupInputDto input, int ownerUserId)
    {
        var resolvedOwnerId = await ResolveOwnerIdAsync(ownerUserId);
        if (resolvedOwnerId == null)
            return HubGroupSaveResult.Fail("Не найден пользователь с ролью Admin для назначения владельцем группы.");

        var slug = await _core.ResolveSlugAsync(input, excludeId: null);
        var error = await _core.ValidateAsync(input, slug, excludeId: null);
        if (error != null) return HubGroupSaveResult.Fail(error);

        var group = new HubGroup { OwnerUserId = resolvedOwnerId.Value };
        HubGroupCrudCore.Apply(group, input, slug);
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

        HubGroupCrudCore.Apply(group, input, slug);
        await _core.ApplyCountryAsync(group, input.Country);
        return await _core.SaveAsync(group);
    }

    public async Task<HubGroupSaveResult> DeleteAsync(int id)
    {
        var group = await _db.HubGroups.FindAsync(id);
        if (group == null) return HubGroupSaveResult.Fail($"Группа #{id} не найдена");

        _db.HubGroups.Remove(group);
        await _db.SaveChangesAsync();
        await _core.InvalidateCacheAsync();
        return HubGroupSaveResult.Ok(id);
    }

    public async Task<IReadOnlyList<SwimmerSearchResultDto>> SearchSwimmersAsync(string query)
    {
        query = (query ?? "").Trim();
        if (query.Length == 0) return [];

        // Разбиваем на слова: запрос может быть "фамилия имя" целиком, а совпадение нужно
        // искать по обоим полям вместе (каждое слово запроса — в любом из четырёх полей имени).
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return [];

        var swimmersQuery = _db.Swimmers.AsNoTracking().Include(s => s.Club).AsQueryable();

        foreach (var word in words)
        {
            var pattern = $"%{word}%";
            swimmersQuery = swimmersQuery.Where(s =>
                EF.Functions.ILike(s.LastName, pattern) ||
                EF.Functions.ILike(s.FirstName, pattern) ||
                EF.Functions.ILike(s.LastNameEn, pattern) ||
                EF.Functions.ILike(s.FirstNameEn, pattern));
        }

        return await swimmersQuery
            .Where(s =>
                // Эстафетные заплывы иногда попадали в парсер как "спортсмен" с составным
                // именем — список через запятую (баг парсинга relay-строк). Это не реальные
                // пловцы, добавлять их в группу нельзя — отсекаем по запятой в имени/фамилии.
                !EF.Functions.ILike(s.LastName, "%,%") && !EF.Functions.ILike(s.FirstName, "%,%"))
            .OrderBy(s => s.LastName)
            .Take(20)
            .Select(s => new SwimmerSearchResultDto
            {
                Id = s.Id,
                Name = (s.LastName + " " + s.FirstName).Trim(),
                NameEn = (s.LastNameEn + " " + s.FirstNameEn).Trim(),
                BirthYear = s.BirthYear,
                ClubName = s.Club != null ? s.Club.Name : null
            })
            .ToListAsync();
    }

    public async Task<IReadOnlyList<SwimmerSearchResultDto>> GetClubSwimmersAsync(int clubId)
    {
        return await _db.Swimmers.AsNoTracking()
            .Where(s => s.ClubId == clubId)
            .Include(s => s.Club)
            .OrderBy(s => s.LastName)
            .Take(200)
            .Select(s => new SwimmerSearchResultDto
            {
                Id = s.Id,
                Name = (s.LastName + " " + s.FirstName).Trim(),
                NameEn = (s.LastNameEn + " " + s.FirstNameEn).Trim(),
                BirthYear = s.BirthYear,
                ClubName = s.Club != null ? s.Club.Name : null
            })
            .ToListAsync();
    }

    public Task<HubGroupMemberSaveResult> AddMemberAsync(int hubGroupId, int swimmerId, string role) =>
        _core.AddMemberAsync(hubGroupId, swimmerId, role);

    public Task<HubGroupMemberSaveResult> UpdateMemberAsync(int hubGroupId, int memberId, string role, int sortOrder) =>
        _core.UpdateMemberAsync(hubGroupId, memberId, role, sortOrder);

    public Task<HubGroupMemberSaveResult> RemoveMemberAsync(int hubGroupId, int memberId) =>
        _core.RemoveMemberAsync(hubGroupId, memberId);

    /// <summary>
    /// DevAdminBypass (Program.cs) выдаёт неаутентифицированному запросу синтетический
    /// NameIdentifier="0", которого нет в Sys_AppUsers, — прямая вставка такого OwnerUserId
    /// падает на FK. В этом случае (и вообще если запрошенный id не существует) подставляем
    /// первого пользователя с ролью Admin. Дев-костыль, специфичный для админки — в
    /// пользовательском CRUD (8.6) не переиспользуется: там OwnerUserId — реальный вошедший.
    /// </summary>
    private async Task<int?> ResolveOwnerIdAsync(int requestedUserId)
    {
        if (requestedUserId > 0 && await _db.AppUsers.AnyAsync(u => u.Id == requestedUserId))
            return requestedUserId;

        return await _db.AppUserRoles
            .Where(ur => ur.Role.Name == "Admin")
            .OrderBy(ur => ur.UserId)
            .Select(ur => (int?)ur.UserId)
            .FirstOrDefaultAsync();
    }
}
