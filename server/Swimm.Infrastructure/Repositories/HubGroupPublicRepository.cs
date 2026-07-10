using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Публичный read-путь групп (см. <see cref="IHubGroupPublicRepository"/>).
///
/// Два контекста намеренно: публичные группы читаются через <see cref="SwimmReadDbContext"/>
/// (роль swimm_ro), а виртуальная группа «Моё избранное» — через <see cref="SwimmDbContext"/>,
/// потому что Sys_UserFavorites роли swimm_ro недоступна (личные данные, см. setup-roles.sql).
/// </summary>
public class HubGroupPublicRepository : IHubGroupPublicRepository
{
    /// <summary>Сколько последних заплывов отдаём на страницу группы.</summary>
    private const int RecentResultsLimit = 25;

    private readonly SwimmReadDbContext _read;
    private readonly SwimmDbContext _rw;
    private readonly ISettingsService _settings;

    public HubGroupPublicRepository(SwimmReadDbContext read, SwimmDbContext rw, ISettingsService settings)
    {
        _read = read;
        _rw = rw;
        _settings = settings;
    }

    private string Visibility => _settings.GetValue("HubGroupVisibility", "public");

    public async Task<IReadOnlyList<HubGroupListItemDto>> GetGroupsAsync()
    {
        var visibility = Visibility;
        if (visibility == "private") return [];

        var query = _read.HubGroups.AsNoTracking();
        if (visibility == "perGroup")
            query = query.Where(g => g.IsPublic);

        return await query
            .OrderBy(g => g.Name)
            .Select(g => new HubGroupListItemDto
            {
                Slug = g.Slug,
                Name = g.Name,
                NameEn = g.NameEn,
                Description = g.Description,
                IconUrl = g.IconUrl,
                Location = g.Location,
                ClubName = g.Club != null ? g.Club.Name : null,
                MemberCount = g.Members.Count
            })
            .ToListAsync();
    }

    public async Task<HubGroupDetailsDto?> GetBySlugAsync(string slug)
    {
        var visibility = Visibility;
        if (visibility == "private") return null;

        var group = await _read.HubGroups.AsNoTracking()
            .Include(g => g.Club)
            .FirstOrDefaultAsync(g => g.Slug == slug);
        if (group == null) return null;
        if (visibility == "perGroup" && !group.IsPublic) return null;

        var members = await _read.HubGroupMembers.AsNoTracking()
            .Where(m => m.HubGroupId == group.Id)
            .OrderBy(m => m.SortOrder)
            .Select(m => new HubGroupPublicMemberDto
            {
                SwimmerId = m.SwimmerId,
                Name = (m.Swimmer!.LastName + " " + m.Swimmer.FirstName).Trim(),
                NameEn = (m.Swimmer.LastNameEn + " " + m.Swimmer.FirstNameEn).Trim(),
                BirthYear = m.Swimmer.BirthYear,
                ClubName = m.Swimmer.Club != null ? m.Swimmer.Club.Name : null,
                Role = m.Role
            })
            .ToListAsync();

        var dto = new HubGroupDetailsDto
        {
            Slug = group.Slug,
            Name = group.Name,
            NameEn = group.NameEn,
            Description = group.Description,
            IconUrl = group.IconUrl,
            CoverImageUrl = group.CoverImageUrl,
            Location = group.Location,
            ClubName = group.Club?.Name,
            Links = ParseLinks(group.Links),
            IsVirtual = false,
            Members = members
        };

        await FillAggregatesAsync(_read, dto, members.Select(m => m.SwimmerId).ToList());
        return dto;
    }

    public async Task<HubGroupDetailsDto> GetFavoritesGroupAsync(int userId)
    {
        // Sys_UserFavorites читается только владельцем через rw-контекст; клубы из избранного
        // здесь не участвуют — «группа» состоит из пловцов.
        var members = await _rw.UserFavorites.AsNoTracking()
            .Where(f => f.UserId == userId && f.SwimmerId != null)
            .OrderBy(f => f.SortOrder)
            .Select(f => new HubGroupPublicMemberDto
            {
                SwimmerId = f.SwimmerId!.Value,
                Name = (f.Swimmer!.LastName + " " + f.Swimmer.FirstName).Trim(),
                NameEn = (f.Swimmer.LastNameEn + " " + f.Swimmer.FirstNameEn).Trim(),
                BirthYear = f.Swimmer.BirthYear,
                ClubName = f.Swimmer.Club != null ? f.Swimmer.Club.Name : null,
                Role = "member"
            })
            .ToListAsync();

        var dto = new HubGroupDetailsDto
        {
            Slug = "favorites",
            Name = "Моё избранное",
            NameEn = "My favorites",
            IsVirtual = true,
            Members = members
        };

        await FillAggregatesAsync(_rw, dto, members.Select(m => m.SwimmerId).ToList());
        return dto;
    }

    /// <summary>Общие агрегаты страницы: последние заплывы и рекорды группы.</summary>
    private static async Task FillAggregatesAsync(SwimmDbContext db, HubGroupDetailsDto dto, List<int> swimmerIds)
    {
        if (swimmerIds.Count == 0) return;

        dto.RecentResults = await db.Results.AsNoTracking()
            .Where(r => swimmerIds.Contains(r.SwimmerId))
            .OrderByDescending(r => r.CompetitionDate)
            .ThenByDescending(r => r.Id)
            .Take(RecentResultsLimit)
            .Select(ResultMapping.ToDto)
            .ToListAsync();

        // «Рекорды группы»: лучшее время по каждой оси стиль+дистанция+бассейн+пол.
        // Эстафеты и незачтённые времена (DSQ/DNS) не участвуют.
        dto.Bests = await db.Results.AsNoTracking()
            .Where(r => swimmerIds.Contains(r.SwimmerId)
                        && r.TimeMillisecond != null
                        && !r.TimeFail
                        && r.RelayId == null)
            .GroupBy(r => new { StyleName = r.Style.Name, r.Distance, r.Competition.PoolType, r.Gender })
            .Select(g => g
                .OrderBy(r => r.TimeMillisecond)
                .ThenBy(r => r.CompetitionDate)
                .Select(r => new HubGroupBestDto
                {
                    StyleName = g.Key.StyleName,
                    Distance = g.Key.Distance,
                    PoolType = g.Key.PoolType,
                    Gender = g.Key.Gender,
                    TimeOriginal = r.TimeOriginal,
                    TimeMillisecond = r.TimeMillisecond,
                    SwimmerId = r.SwimmerId,
                    SwimmerName = (r.Swimmer.LastName + " " + r.Swimmer.FirstName).Trim(),
                    SwimmerNameEn = (r.Swimmer.LastNameEn + " " + r.Swimmer.FirstNameEn).Trim(),
                    CompetitionName = r.Competition.Name,
                    Date = r.Competition.Date,
                    Points = r.InternationalPoints
                })
                .First())
            .ToListAsync();

        dto.Bests = dto.Bests
            .OrderBy(b => b.StyleName)
            .ThenBy(b => b.Distance.Length)
            .ThenBy(b => b.Distance)
            .ThenBy(b => b.Gender)
            .ToList();
    }

    private static List<HubGroupPublicLinkDto> ParseLinks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            // В БД ссылки лежат в формате админского HubGroupLinkDto (PascalCase-ключи) —
            // десериализуем его же, наружу отдаём snake_case-вариант.
            var stored = System.Text.Json.JsonSerializer.Deserialize<List<HubGroupLinkDto>>(json) ?? [];
            return stored.Select(l => new HubGroupPublicLinkDto { Kind = l.Kind, Url = l.Url }).ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
