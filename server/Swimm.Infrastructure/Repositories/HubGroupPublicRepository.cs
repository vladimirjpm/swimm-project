using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;

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
                Country = g.Country != null ? g.Country.CountryCode : null,
                ClubName = g.Club != null ? g.Club.Name : null,
                IsOfficial = g.IsOfficial,
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
            .Include(g => g.Country)
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
            Id = group.Id,
            Slug = group.Slug,
            Name = group.Name,
            NameEn = group.NameEn,
            Description = group.Description,
            IconUrl = group.IconUrl,
            CoverImageUrl = group.CoverImageUrl,
            Location = group.Location,
            Country = group.Country?.CountryCode,
            ClubName = group.Club?.Name,
            IsOfficial = group.IsOfficial,
            JoinPolicy = group.JoinPolicy,
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

    public async Task<List<int>?> GetRosterSwimmerIdsAsync(string slug)
    {
        var visibility = Visibility;
        if (visibility == "private") return null;

        var group = await _read.HubGroups.AsNoTracking()
            .Where(g => g.Slug == slug)
            .Select(g => new { g.Id, g.IsPublic })
            .FirstOrDefaultAsync();
        if (group == null) return null;
        if (visibility == "perGroup" && !group.IsPublic) return null;

        return await _read.HubGroupMembers.AsNoTracking()
            .Where(m => m.HubGroupId == group.Id)
            .Select(m => m.SwimmerId)
            .ToListAsync();
    }

    /// <summary>Общие агрегаты страницы: последние заплывы, рекорды группы и сезонный зачёт.</summary>
    private static async Task FillAggregatesAsync(SwimmDbContext db, HubGroupDetailsDto dto, List<int> swimmerIds)
    {
        // Сезон = израильский плавательный: с 1 сентября последнего прошедшего сентября по сегодня.
        var today = DateTime.Today;
        var seasonStartYear = today.Month >= 9 ? today.Year : today.Year - 1;
        var seasonStart = new DateTime(seasonStartYear, 9, 1);
        dto.SeasonLabel = $"{seasonStartYear}/{(seasonStartYear + 1) % 100:D2}";

        if (swimmerIds.Count == 0) return;

        // Двухшаговый fetch «последних заплывов» — намеренно. Одним запросом (фильтр по
        // пловцам + ORDER BY дата DESC + LIMIT + широкая проекция ToDto с джойнами)
        // планировщик на большой таблице попадает в LIMIT-ловушку: пятится по индексу даты
        // через ВСЮ таблицу в надежде быстро набрать 25 строк (16 с на 3 млн синтетики).
        // Узкий id-запрос покрывается index-only сканом IX_Results_SwimmerId_CompetitionDate_Id
        // (миграция AddResultsSwimmerRecentIndex), а полные DTO добираются по 25 конкретным id.
        var recentIds = await db.Results.AsNoTracking()
            .Where(r => swimmerIds.Contains(r.SwimmerId))
            .OrderByDescending(r => r.CompetitionDate)
            .ThenByDescending(r => r.Id)
            .Take(RecentResultsLimit)
            .Select(r => r.Id)
            .ToListAsync();

        dto.RecentResults = await db.Results.AsNoTracking()
            .Where(r => recentIds.Contains(r.Id))
            .OrderByDescending(r => r.CompetitionDate)
            .ThenByDescending(r => r.Id)
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

        await FillStandingsAsync(db, dto, swimmerIds, seasonStart);
    }

    /// <summary>Сезонный зачёт: очки за место по правилам ClubPointsRule + медали за сезон.</summary>
    private static async Task FillStandingsAsync(
        SwimmDbContext db, HubGroupDetailsDto dto, List<int> swimmerIds, DateTime seasonStart)
    {
        // Сезонные заплывы участников (без эстафет) — минимальная проекция для зачёта.
        var season = await db.Results.AsNoTracking()
            .Where(r => swimmerIds.Contains(r.SwimmerId)
                        && r.RelayId == null
                        && r.CompetitionDate >= seasonStart)
            .Select(r => new SeasonResultRow
            {
                SwimmerId = r.SwimmerId,
                Position = r.Position,
                TimeFail = r.TimeFail,
                InternationalPoints = r.InternationalPoints,
                CompetitionDate = r.CompetitionDate,
                IsMasters = r.Competition.IsMasters
            })
            .ToListAsync();

        // Правила очков грузим целиком (их единицы) и применяем в памяти.
        var rules = await db.ClubPointsRules.AsNoTracking()
            .Include(r => r.Entries)
            .ToListAsync();

        var bySwimmer = season.GroupBy(r => r.SwimmerId).ToDictionary(g => g.Key, g => g.ToList());

        // Участники без заплывов за сезон (в т.ч. тренер) — в зачёте с нулями.
        dto.Standings = dto.Members
            .Select(m =>
            {
                bySwimmer.TryGetValue(m.SwimmerId, out var rows);
                rows ??= [];
                return new HubGroupStandingDto
                {
                    SwimmerId = m.SwimmerId,
                    Name = m.Name,
                    NameEn = m.NameEn,
                    Role = m.Role,
                    Swims = rows.Count,
                    // Медали — только по зачтённым заплывам: DSQ/незачтённое время (TimeFail)
                    // не медаль (согласовано с очками, которые тоже исключают TimeFail).
                    Golds = rows.Count(r => !r.TimeFail && r.Position == 1),
                    Silvers = rows.Count(r => !r.TimeFail && r.Position == 2),
                    Bronzes = rows.Count(r => !r.TimeFail && r.Position == 3),
                    ClubPoints = rows.Sum(r =>
                        ClubPointsScoring.PointsFor(
                            rules, r.Position, r.TimeFail, r.IsMasters, DateOnly.FromDateTime(r.CompetitionDate))),
                    BestFina = rows.Count > 0 ? rows.Max(r => r.InternationalPoints) : 0
                };
            })
            .OrderByDescending(s => s.ClubPoints)
            .ThenByDescending(s => s.Swims)
            .ToList();
    }

    /// <summary>Проекция сезонного результата для зачёта (in-memory).</summary>
    private sealed class SeasonResultRow
    {
        public int SwimmerId { get; init; }
        public int? Position { get; init; }
        public bool TimeFail { get; init; }
        public int InternationalPoints { get; init; }
        public DateTime CompetitionDate { get; init; }
        public bool IsMasters { get; init; }
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
