using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Админский CRUD групп (см. <see cref="IHubGroupAdminService"/>). Пишет через owner-контекст
/// <see cref="SwimmDbContext"/>. Публичного кэша для групп пока нет (фаза 3) — инвалидировать нечего.
/// </summary>
public partial class HubGroupAdminService : IHubGroupAdminService
{
    private readonly SwimmDbContext _db;

    public HubGroupAdminService(SwimmDbContext db) => _db = db;

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
                UpdatedAt = g.UpdatedAt
            })
            .ToListAsync();
    }

    public async Task<HubGroupEditDto?> GetByIdAsync(int id)
    {
        var g = await _db.HubGroups.AsNoTracking()
            .Include(x => x.Owner)
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
            ClubId = g.ClubId,
            OwnerUserId = g.OwnerUserId,
            OwnerDisplayName = g.Owner?.DisplayName ?? $"#{g.OwnerUserId}",
            IsPublic = g.IsPublic,
            Links = ParseLinks(g.Links),
            Members = members
        };
    }

    public async Task<IReadOnlyList<ClubOptionDto>> GetClubOptionsAsync()
    {
        return await _db.Clubs.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new ClubOptionDto { Id = c.Id, Name = c.Name })
            .ToListAsync();
    }

    public async Task<HubGroupSaveResult> CreateAsync(HubGroupInputDto input, int ownerUserId)
    {
        var resolvedOwnerId = await ResolveOwnerIdAsync(ownerUserId);
        if (resolvedOwnerId == null)
            return HubGroupSaveResult.Fail("Не найден пользователь с ролью Admin для назначения владельцем группы.");

        var slug = await ResolveSlugAsync(input, excludeId: null);
        var error = await ValidateAsync(input, slug, excludeId: null);
        if (error != null) return HubGroupSaveResult.Fail(error);

        var group = new HubGroup { OwnerUserId = resolvedOwnerId.Value };
        Apply(group, input, slug);
        _db.HubGroups.Add(group);
        return await SaveAsync(group);
    }

    public async Task<HubGroupSaveResult> UpdateAsync(int id, HubGroupInputDto input)
    {
        var group = await _db.HubGroups.FindAsync(id);
        if (group == null) return HubGroupSaveResult.Fail($"Группа #{id} не найдена");

        var slug = await ResolveSlugAsync(input, excludeId: id);
        var error = await ValidateAsync(input, slug, excludeId: id);
        if (error != null) return HubGroupSaveResult.Fail(error);

        Apply(group, input, slug);
        return await SaveAsync(group);
    }

    public async Task<HubGroupSaveResult> DeleteAsync(int id)
    {
        var group = await _db.HubGroups.FindAsync(id);
        if (group == null) return HubGroupSaveResult.Fail($"Группа #{id} не найдена");

        _db.HubGroups.Remove(group);
        await _db.SaveChangesAsync();
        return HubGroupSaveResult.Ok(id);
    }

    public async Task<IReadOnlyList<SwimmerSearchResultDto>> SearchSwimmersAsync(string query)
    {
        query = (query ?? "").Trim();
        if (query.Length == 0) return [];

        return await _db.Swimmers.AsNoTracking()
            .Include(s => s.Club)
            .Where(s =>
                EF.Functions.ILike(s.LastName, $"%{query}%") ||
                EF.Functions.ILike(s.FirstName, $"%{query}%") ||
                EF.Functions.ILike(s.LastNameEn, $"%{query}%") ||
                EF.Functions.ILike(s.FirstNameEn, $"%{query}%"))
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

    public async Task<HubGroupMemberSaveResult> AddMemberAsync(int hubGroupId, int swimmerId, string role)
    {
        var groupExists = await _db.HubGroups.AnyAsync(g => g.Id == hubGroupId);
        if (!groupExists) return HubGroupMemberSaveResult.Fail($"Группа #{hubGroupId} не найдена");

        var swimmerExists = await _db.Swimmers.AnyAsync(s => s.Id == swimmerId);
        if (!swimmerExists) return HubGroupMemberSaveResult.Fail($"Пловец #{swimmerId} не найден");

        var dup = await _db.HubGroupMembers.AnyAsync(m => m.HubGroupId == hubGroupId && m.SwimmerId == swimmerId);
        if (dup) return HubGroupMemberSaveResult.Fail("Этот пловец уже состоит в группе");

        if (!HubGroupMember.Roles.Contains(role)) role = "member";

        var maxOrder = await _db.HubGroupMembers.Where(m => m.HubGroupId == hubGroupId)
            .Select(m => (int?)m.SortOrder).MaxAsync() ?? 0;

        _db.HubGroupMembers.Add(new HubGroupMember
        {
            HubGroupId = hubGroupId,
            SwimmerId = swimmerId,
            Role = role,
            SortOrder = maxOrder + 1
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return HubGroupMemberSaveResult.Fail("Этот пловец уже состоит в группе");
        }

        // Обновляем UpdatedAt группы отдельным простым запросом — без загрузки навигации.
        await TouchGroupAsync(hubGroupId);
        return HubGroupMemberSaveResult.Ok();
    }

    public async Task<HubGroupMemberSaveResult> UpdateMemberAsync(int hubGroupId, int memberId, string role, int sortOrder)
    {
        var member = await _db.HubGroupMembers.FindAsync(memberId);
        if (member == null || member.HubGroupId != hubGroupId)
            return HubGroupMemberSaveResult.Fail($"Участник #{memberId} не найден");

        member.Role = HubGroupMember.Roles.Contains(role) ? role : "member";
        member.SortOrder = sortOrder;
        await _db.SaveChangesAsync();
        await TouchGroupAsync(hubGroupId);
        return HubGroupMemberSaveResult.Ok();
    }

    public async Task<HubGroupMemberSaveResult> RemoveMemberAsync(int hubGroupId, int memberId)
    {
        var member = await _db.HubGroupMembers.FindAsync(memberId);
        if (member == null || member.HubGroupId != hubGroupId)
            return HubGroupMemberSaveResult.Fail($"Участник #{memberId} не найден");

        _db.HubGroupMembers.Remove(member);
        await _db.SaveChangesAsync();
        await TouchGroupAsync(hubGroupId);
        return HubGroupMemberSaveResult.Ok();
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// DevAdminBypass (Program.cs) выдаёт неаутентифицированному запросу синтетический
    /// NameIdentifier="0", которого нет в Sys_AppUsers, — прямая вставка такого OwnerUserId
    /// падает на FK. В этом случае (и вообще если запрошенный id не существует) подставляем
    /// первого пользователя с ролью Admin.
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

    private async Task TouchGroupAsync(int groupId)
    {
        var group = await _db.HubGroups.FindAsync(groupId);
        if (group == null) return;
        group.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    private static void Apply(HubGroup group, HubGroupInputDto input, string slug)
    {
        group.Name = input.Name.Trim();
        group.NameEn = string.IsNullOrWhiteSpace(input.NameEn) ? null : input.NameEn.Trim();
        group.Slug = slug;
        group.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        group.IconUrl = string.IsNullOrWhiteSpace(input.IconUrl) ? null : input.IconUrl.Trim();
        group.CoverImageUrl = string.IsNullOrWhiteSpace(input.CoverImageUrl) ? null : input.CoverImageUrl.Trim();
        group.Location = string.IsNullOrWhiteSpace(input.Location) ? null : input.Location.Trim();
        group.ClubId = input.ClubId;
        group.IsPublic = input.IsPublic;
        group.Links = SerializeLinks(input.Links);
        group.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<HubGroupSaveResult> SaveAsync(HubGroup group)
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // 23505 = unique_violation (slug), 23503 = foreign_key_violation (владелец/клуб) —
            // ловить их одинаково как «slug занят» было бы враньём, вводящим в заблуждение.
            if (ex.InnerException is PostgresException { SqlState: "23505" })
                return HubGroupSaveResult.Fail($"Не удалось сохранить: slug «{group.Slug}» уже занят другой группой.");
            if (ex.InnerException is PostgresException { SqlState: "23503" })
                return HubGroupSaveResult.Fail("Не удалось сохранить: владелец или клуб не найден.");
            return HubGroupSaveResult.Fail("Не удалось сохранить группу.");
        }
        return HubGroupSaveResult.Ok(group.Id);
    }

    private async Task<string> ResolveSlugAsync(HubGroupInputDto input, int? excludeId)
    {
        var raw = input.Slug;
        if (string.IsNullOrWhiteSpace(raw))
            raw = !string.IsNullOrWhiteSpace(input.NameEn) ? input.NameEn : input.Name;

        var slug = Slugify(raw);
        if (slug.Length == 0) slug = "group";

        // Автодедупликация только для авто-сгенерированного (пустого исходного) слага;
        // если пользователь ввёл slug вручную и он занят — это ошибка валидации (ValidateAsync).
        if (!string.IsNullOrWhiteSpace(input.Slug)) return Slugify(input.Slug);

        var candidate = slug;
        var suffix = 2;
        while (await _db.HubGroups.AnyAsync(g => g.Slug == candidate && g.Id != (excludeId ?? 0)))
        {
            var suffixText = $"-{suffix++}";
            // Slug — MaxLength(120); суффикс дедупликации не должен вытолкнуть строку за лимит.
            var baseLength = Math.Min(slug.Length, SlugMaxLength - suffixText.Length);
            candidate = slug[..baseLength].TrimEnd('-') + suffixText;
        }
        return candidate;
    }

    private async Task<string?> ValidateAsync(HubGroupInputDto input, string slug, int? excludeId)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) return "Название обязательно";
        if (slug.Length == 0) return "Slug обязателен";

        var dup = await _db.HubGroups.AnyAsync(g => g.Id != (excludeId ?? 0) && g.Slug == slug);
        if (dup) return $"Slug «{slug}» уже занят другой группой";

        return null;
    }

    private const int SlugMaxLength = 120; // HubGroup.Slug — MaxLength(120)

    private static string Slugify(string source)
    {
        var lower = (source ?? "").Trim().ToLowerInvariant();
        var dashed = NonSlugCharsRegex().Replace(lower, "-");
        var collapsed = MultiDashRegex().Replace(dashed, "-");
        var trimmed = collapsed.Trim('-');
        if (trimmed.Length > SlugMaxLength)
            trimmed = trimmed[..SlugMaxLength].TrimEnd('-');
        return trimmed;
    }

    private static string SerializeLinks(List<HubGroupLinkDto> links)
    {
        var clean = links
            .Where(l => !string.IsNullOrWhiteSpace(l.Kind) && !string.IsNullOrWhiteSpace(l.Url))
            .Select(l => new HubGroupLinkDto { Kind = l.Kind.Trim(), Url = l.Url.Trim() })
            .ToList();
        return clean.Count == 0 ? "[]" : JsonSerializer.Serialize(clean);
    }

    private static List<HubGroupLinkDto> ParseLinks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<HubGroupLinkDto>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugCharsRegex();

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultiDashRegex();
}
