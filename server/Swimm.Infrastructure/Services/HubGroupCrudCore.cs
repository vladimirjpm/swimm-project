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
/// Общая write-логика групп (slug/валидация/PostgresException/участники), вынесенная из
/// <see cref="HubGroupAdminService"/>, чтобы её могли переиспользовать и админский, и
/// пользовательский (8.6) CRUD — без копипаста правил вроде "slug занят" (23505) /
/// "владелец или клуб не найден" (23503).
/// </summary>
public partial class HubGroupCrudCore
{
    private readonly SwimmDbContext _db;
    private readonly ICacheService _cache;

    public HubGroupCrudCore(SwimmDbContext db, ICacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<string> ResolveSlugAsync(HubGroupInputDto input, int? excludeId)
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

    /// <summary>
    /// Страна группы из ввода: alpha-3 код (ISR…) → FK на Countries, find-or-create —
    /// как в импорте результатов (JsonImportService). Семантика ввода: null — поле не
    /// прислано, страну НЕ трогаем (старый клиент не сотрёт выставленное админом);
    /// пустая строка — явное «снять страну». Вызывать после <see cref="Apply"/>.
    /// </summary>
    public async Task ApplyCountryAsync(HubGroup group, string? countryCode)
    {
        if (countryCode == null) return;

        var code = countryCode.Trim().ToUpperInvariant();
        if (code.Length == 0)
        {
            group.CountryId = null;
            return;
        }

        var country = await _db.Countries.FirstOrDefaultAsync(c => c.CountryCode == code);
        if (country == null)
        {
            country = new Country { CountryCode = code, CountryName = code };
            _db.Countries.Add(country);
            await _db.SaveChangesAsync();
        }
        group.CountryId = country.Id;
    }

    public async Task<string?> ValidateAsync(HubGroupInputDto input, string slug, int? excludeId)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) return "Название обязательно";
        if (slug.Length == 0) return "Slug обязателен";

        var dup = await _db.HubGroups.AnyAsync(g => g.Id != (excludeId ?? 0) && g.Slug == slug);
        if (dup) return $"Slug «{slug}» уже занят другой группой";

        if (input.JoinPolicy != null &&
            input.JoinPolicy != HubGroupJoinPolicy.Open &&
            input.JoinPolicy != HubGroupJoinPolicy.Approval)
            return "Политика вступления: допустимо open или approval";

        return null;
    }

    /// <param name="allowClubChange">
    /// Разрешить менять привязку к клубу из ввода. Только админский путь: официальная связь
    /// группы с клубом ведётся через одобрение заявки (8.7), поэтому пользовательский CRUD (8.6)
    /// передаёт false — иначе владелец/админ группы мог бы крафтовым запросом проставить/перебить
    /// ClubId в обход админа (спуфинг клуба, перепривязка официальной группы).
    /// </param>
    public static void Apply(HubGroup group, HubGroupInputDto input, string slug, bool allowClubChange = true)
    {
        group.Name = input.Name.Trim();
        group.NameEn = string.IsNullOrWhiteSpace(input.NameEn) ? null : input.NameEn.Trim();
        group.Slug = slug;
        group.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        group.IconUrl = string.IsNullOrWhiteSpace(input.IconUrl) ? null : input.IconUrl.Trim();
        group.CoverImageUrl = string.IsNullOrWhiteSpace(input.CoverImageUrl) ? null : input.CoverImageUrl.Trim();
        group.Location = string.IsNullOrWhiteSpace(input.Location) ? null : input.Location.Trim();
        if (allowClubChange) group.ClubId = input.ClubId;
        group.IsPublic = input.IsPublic;
        // null — поле не прислано (старый клиент), политику не трогаем.
        if (input.JoinPolicy != null) group.JoinPolicy = input.JoinPolicy;
        group.Links = SerializeLinks(input.Links);
        group.UpdatedAt = DateTime.UtcNow;
    }

    public async Task<HubGroupSaveResult> SaveAsync(HubGroup group)
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
        await _cache.InvalidateAllAsync();
        return HubGroupSaveResult.Ok(group.Id);
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

    public Task InvalidateCacheAsync() => _cache.InvalidateAllAsync();

    public async Task TouchGroupAsync(int groupId)
    {
        var group = await _db.HubGroups.FindAsync(groupId);
        if (group != null)
        {
            group.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        // Мутации участников идут только через этот метод — инвалидация кэша здесь.
        await _cache.InvalidateAllAsync();
    }

    private const int SlugMaxLength = 120; // HubGroup.Slug — MaxLength(120)

    public static string Slugify(string source)
    {
        var lower = (source ?? "").Trim().ToLowerInvariant();
        var dashed = NonSlugCharsRegex().Replace(lower, "-");
        var collapsed = MultiDashRegex().Replace(dashed, "-");
        var trimmed = collapsed.Trim('-');
        if (trimmed.Length > SlugMaxLength)
            trimmed = trimmed[..SlugMaxLength].TrimEnd('-');
        return trimmed;
    }

    public static string SerializeLinks(List<HubGroupLinkDto> links)
    {
        var clean = links
            .Where(l => !string.IsNullOrWhiteSpace(l.Kind) && !string.IsNullOrWhiteSpace(l.Url))
            .Select(l => new HubGroupLinkDto { Kind = l.Kind.Trim(), Url = l.Url.Trim() })
            .ToList();
        return clean.Count == 0 ? "[]" : JsonSerializer.Serialize(clean);
    }

    public static List<HubGroupLinkDto> ParseLinks(string? json)
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
