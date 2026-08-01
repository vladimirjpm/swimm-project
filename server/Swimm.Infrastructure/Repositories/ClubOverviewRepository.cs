using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Сборный ответ страницы клуба (<see cref="IClubOverviewRepository"/>).
///
/// Всё, что касается мест клубов, берётся из материализованного зачёта
/// <see cref="ClubCompetitionStanding"/> — считать его на лету нельзя (страница поднимает
/// зачёты за несколько сезонов × ~7 групп сразу, а сезонов 20+ и число растёт).
/// Роль зачёта (❄/☀) выводится из соревнования (<see cref="StandingKinds.Resolve"/>),
/// зачётная группа — из членства в <see cref="Category"/> возрастной лестницы.
/// </summary>
public class ClubOverviewRepository : IClubOverviewRepository
{
    private readonly SwimmReadDbContext _read;

    public ClubOverviewRepository(SwimmReadDbContext read) => _read = read;

    /// <summary>Строка зачёта клуба вместе с контекстом соревнования.</summary>
    private sealed record StandingRow(
        int CompetitionId,
        string CompetitionName,
        string Date,
        DateTime SortDate,
        int Season,
        string? Kind,
        string? GroupKey,
        string? GroupName,
        string? GroupBadge,
        int GroupOrder,
        int Rank,
        int Points,
        int Gold,
        int Silver,
        int Bronze,
        int SwimmerCount,
        int ScoringSwims);

    public async Task<ClubOverviewDto?> GetOverviewAsync(
        int resolvedClubId, int requestedId, int? season, string? groupKey,
        int gridSeasons, int? standingCompetitionId)
    {
        var club = await _read.Clubs.AsNoTracking()
            .Where(c => c.Id == resolvedClubId)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.NameEn,
                CountryCode = c.Country != null ? c.Country.CountryCode : null,
                CountryName = c.Country != null ? c.Country.CountryName : null,
            })
            .FirstOrDefaultAsync();
        if (club is null) return null;

        var rows = await LoadStandingsAsync(resolvedClubId);

        var dto = new ClubOverviewDto
        {
            Club = new ClubProfileDto
            {
                Id = club.Id,
                RequestedId = requestedId,
                Name = club.Name,
                NameEn = club.NameEn,
                CountryCode = club.CountryCode,
                CountryName = club.CountryName,
                SwimmerCount = await _read.Swimmers.AsNoTracking()
                    .CountAsync(s => s.ClubId == resolvedClubId),
                FirstSeason = rows.Count > 0 ? rows.Min(r => r.Season) : null,
                LastSeason = rows.Count > 0 ? rows.Max(r => r.Season) : null,
            },
            Seasons = rows
                .Select(r => r.Season).Distinct().OrderByDescending(s => s)
                .Select(s => new ClubSeasonOptionDto { Season = s, Label = SeasonMath.Label(s) })
                .ToList(),
        };

        var official = await _read.HubGroups.AsNoTracking()
            .Where(g => g.ClubId == resolvedClubId && g.IsOfficial)
            .Select(g => new { g.Slug, g.Name })
            .FirstOrDefaultAsync();
        if (official is not null)
        {
            dto.Club.OfficialGroupSlug = official.Slug;
            dto.Club.OfficialGroupName = official.Name;
        }

        // Скоуп страницы: сезон и/или зачётная группа. Дальше все карточки читают его.
        var scoped = rows
            .Where(r => season is null || r.Season == season)
            .Where(r => groupKey is null || r.GroupKey == groupKey)
            .ToList();

        // ⚠ В scoped у соревнования, висящего в ДВУХ группах лестницы, ДВЕ строки — по одной
        // на группу (нужно гриду и плиткам). Для KPI и истории это один и тот же старт с теми
        // же очками, поэтому там схлопываем по CompetitionId: иначе Хадера-2026 и Лига-3
        // (реальные двухкатегорийные старты) удваивали бы клубу очки и медали.
        var perCompetition = scoped
            .GroupBy(r => r.CompetitionId)
            .Select(g => g.OrderBy(r => r.GroupOrder).First())
            .ToList();

        dto.Kpi = BuildKpi(perCompetition);
        dto.Groups = BuildGroupTiles(rows, season);
        dto.Grid = BuildGrid(rows, season, groupKey, gridSeasons);
        dto.Timeline = perCompetition
            .OrderByDescending(r => r.SortDate)
            .Select(ToTimelineItem)
            .ToList();

        // Таблица зачёта: явно выбранная клетка либо самый свежий зачёт скоупа.
        var target = standingCompetitionId is int id
            ? rows.FirstOrDefault(r => r.CompetitionId == id)
            : scoped.Where(r => r.Kind is not null).OrderByDescending(r => r.SortDate).FirstOrDefault()
              ?? scoped.OrderByDescending(r => r.SortDate).FirstOrDefault();
        if (target is not null)
            dto.Standings = await BuildStandingsTableAsync(target, resolvedClubId);

        dto.TopSwimmers = await BuildTopSwimmersAsync(resolvedClubId, scoped, season);

        return dto;
    }

    /// <summary>Зачёты клуба + контекст соревнования (роль ❄/☀ и зачётная группа).</summary>
    private async Task<List<StandingRow>> LoadStandingsAsync(int clubId)
    {
        var raw = await _read.ClubCompetitionStandings.AsNoTracking()
            .Where(s => s.ClubId == clubId)
            .Select(s => new
            {
                s.CompetitionId,
                s.Competition.Name,
                s.Competition.Date,
                s.Competition.IsChampionship,
                s.Competition.PoolType,
                s.Competition.StandingKindOverride,
                s.Rank,
                s.Points,
                s.Gold,
                s.Silver,
                s.Bronze,
                s.SwimmerCount,
                s.ScoringSwims,
            })
            .ToListAsync();
        if (raw.Count == 0) return [];

        var competitionIds = raw.Select(r => r.CompetitionId).ToList();

        // Зачётную группу дают ТОЛЬКО категории возрастной лестницы: кастомные
        // (result-maccabiah и будущие) — это «соревнование само по себе», клубного зачёта
        // по ним нет. См. docs/plans/club-page-model.md §3.
        var ladder = Category.ReservedKeys;
        var categories = await _read.CategoryCompetitions.AsNoTracking()
            .Where(cc => competitionIds.Contains(cc.CompetitionId))
            .Select(cc => new
            {
                cc.CompetitionId,
                cc.Category.Key,
                cc.Category.Name,
                cc.Category.Badge,
                cc.Category.DisplayOrder,
            })
            .ToListAsync();

        var byCompetition = categories
            .Where(c => ladder.Contains(c.Key))
            .GroupBy(c => c.CompetitionId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.DisplayOrder).ToList());

        var result = new List<StandingRow>();
        foreach (var r in raw)
        {
            var kind = StandingKinds.Resolve(r.IsChampionship, r.PoolType, r.StandingKindOverride);
            var sortDate = ParseDate(r.Date);
            var seasonYear = SeasonMath.StartYearOf(sortDate);

            // Соревнование может висеть в ДВУХ группах лестницы (Kids + Young) — тогда это
            // два зачёта с одного старта, по строке на группу. Проверено на данных: Хадера-2026
            // и Лига-3 именно такие.
            var groups = byCompetition.TryGetValue(r.CompetitionId, out var list) ? list : null;
            if (groups is null || groups.Count == 0)
            {
                result.Add(new StandingRow(
                    r.CompetitionId, r.Name, r.Date, sortDate, seasonYear, kind,
                    null, null, null, int.MaxValue,
                    r.Rank, r.Points, r.Gold, r.Silver, r.Bronze, r.SwimmerCount, r.ScoringSwims));
                continue;
            }

            foreach (var g in groups)
            {
                result.Add(new StandingRow(
                    r.CompetitionId, r.Name, r.Date, sortDate, seasonYear, kind,
                    g.Key, g.Name, g.Badge, g.DisplayOrder,
                    r.Rank, r.Points, r.Gold, r.Silver, r.Bronze, r.SwimmerCount, r.ScoringSwims));
            }
        }
        return result;
    }

    /// <summary>KPI Hero. На вход — по ОДНОЙ строке на соревнование (см. схлопывание выше).</summary>
    private static ClubKpiDto BuildKpi(IReadOnlyList<StandingRow> scoped) => new()
    {
        // ⚠ Сумма очков поверх разных соревнований — сумма по РАЗНЫМ правилам. Сравнимая
        // величина только одна: ранг. Клиент обязан подписывать это, а не выдавать за рейтинг.
        Points = scoped.Sum(r => r.Points),
        Gold = scoped.Sum(r => r.Gold),
        Silver = scoped.Sum(r => r.Silver),
        Bronze = scoped.Sum(r => r.Bronze),
        Competitions = scoped.Select(r => r.CompetitionId).Distinct().Count(),
        BestRank = scoped.Count > 0 ? scoped.Min(r => r.Rank) : null,
    };

    /// <summary>Плитки групп: ранги ❄/☀ в выбранном сезоне (при «все сезоны» — лучшие).</summary>
    private static List<ClubGroupTileDto> BuildGroupTiles(IReadOnlyList<StandingRow> rows, int? season)
    {
        return rows
            .Where(r => r.GroupKey is not null)
            .Where(r => season is null || r.Season == season)
            .GroupBy(r => r.GroupKey!)
            .OrderBy(g => g.Min(r => r.GroupOrder))
            .Select(g => new ClubGroupTileDto
            {
                Key = g.Key,
                Name = g.First().GroupName ?? g.Key,
                Badge = g.First().GroupBadge,
                WinterRank = BestRank(g, StandingKinds.Winter),
                SummerRank = BestRank(g, StandingKinds.Summer),
                OpenWaterRank = BestRank(g, StandingKinds.OpenWater),
            })
            .ToList();

        static int? BestRank(IEnumerable<StandingRow> g, string kind)
        {
            var ranks = g.Where(r => r.Kind == kind).Select(r => r.Rank).ToList();
            return ranks.Count > 0 ? ranks.Min() : null;
        }
    }

    /// <summary>
    /// Грид «сезон × группа». При «все сезоны» отдаём только <paramref name="gridSeasons"/>
    /// свежих: сезонов 20+ и их число растёт, отдавать все — линейно раздувать ответ.
    /// </summary>
    private static List<ClubGridYearDto> BuildGrid(
        IReadOnlyList<StandingRow> rows, int? season, string? groupKey, int gridSeasons)
    {
        var withGroup = rows.Where(r => r.GroupKey is not null && r.Kind is not null).ToList();
        if (groupKey is not null) withGroup = withGroup.Where(r => r.GroupKey == groupKey).ToList();

        var seasons = withGroup
            .Select(r => r.Season).Distinct().OrderByDescending(s => s)
            .Where(s => season is null || s == season)
            .Take(season is null ? Math.Max(1, gridSeasons) : 1)
            .ToList();

        return seasons.Select(s => new ClubGridYearDto
        {
            Season = s,
            Label = SeasonMath.Label(s),
            Rows = withGroup
                .Where(r => r.Season == s)
                .GroupBy(r => r.GroupKey!)
                .OrderBy(g => g.Min(r => r.GroupOrder))
                .Select(g => new ClubGridRowDto
                {
                    GroupKey = g.Key,
                    GroupName = g.First().GroupName ?? g.Key,
                    Badge = g.First().GroupBadge,
                    Winter = Cell(g, StandingKinds.Winter),
                    Summer = Cell(g, StandingKinds.Summer),
                    OpenWater = Cell(g, StandingKinds.OpenWater),
                })
                .ToList(),
        }).ToList();

        static ClubGridCellDto? Cell(IEnumerable<StandingRow> g, string kind)
        {
            // Двух зимних чемпионатов одной группы за сезон быть не может (правило Влада);
            // если данные всё же дали два — берём лучший ранг, а не молча первый попавшийся.
            var best = g.Where(r => r.Kind == kind).OrderBy(r => r.Rank).FirstOrDefault();
            return best is null ? null : new ClubGridCellDto
            {
                Rank = best.Rank,
                Points = best.Points,
                Clubs = 0,   // заполняется в BuildStandingsTableAsync только для раскрытой клетки
                CompetitionId = best.CompetitionId,
            };
        }
    }

    private static ClubTimelineItemDto ToTimelineItem(StandingRow r) => new()
    {
        CompetitionId = r.CompetitionId,
        Name = r.CompetitionName,
        Date = r.Date,
        Season = r.Season,
        Kind = r.Kind,
        GroupKey = r.GroupKey,
        GroupName = r.GroupName,
        Rank = r.Rank,
        Points = r.Points,
        Gold = r.Gold,
        Silver = r.Silver,
        Bronze = r.Bronze,
        SwimmerCount = r.SwimmerCount,
    };

    /// <summary>
    /// Таблица зачёта: лидеры + окно вокруг нашего клуба (макет: если мы ниже #4 —
    /// показываем 1, 2 и наш ± 1).
    /// </summary>
    private async Task<ClubStandingsTableDto> BuildStandingsTableAsync(StandingRow target, int ourClubId)
    {
        var all = await _read.ClubCompetitionStandings.AsNoTracking()
            .Where(s => s.CompetitionId == target.CompetitionId)
            .OrderBy(s => s.Rank)
            .Select(s => new ClubStandingRowDto
            {
                Rank = s.Rank,
                ClubId = s.ClubId,
                ClubName = s.Club.Name,
                ClubNameEn = s.Club.NameEn,
                Points = s.Points,
                Gold = s.Gold,
                Silver = s.Silver,
                Bronze = s.Bronze,
                SwimmerCount = s.SwimmerCount,
                ScoringSwims = s.ScoringSwims,
                IsUs = s.ClubId == ourClubId,
            })
            .ToListAsync();

        var ourIndex = all.FindIndex(r => r.IsUs);
        var visible = new List<ClubStandingRowDto>();
        if (ourIndex < 0 || ourIndex <= 4)
        {
            visible.AddRange(all.Take(5));
        }
        else
        {
            visible.AddRange(all.Take(2));
            visible.AddRange(all.Skip(ourIndex - 1).Take(3));
        }

        return new ClubStandingsTableDto
        {
            CompetitionId = target.CompetitionId,
            CompetitionName = target.CompetitionName,
            Date = target.Date,
            Season = target.Season,
            Kind = target.Kind,
            GroupKey = target.GroupKey,
            GroupName = target.GroupName,
            ClubCount = all.Count,
            Rows = visible,
        };
    }

    /// <summary>
    /// Топ пловцов по очкам, ПРИНЕСЁННЫМ клубу: те же правила, что в клубном зачёте
    /// (<c>ClubStandingService</c>) — иначе сумма по пловцам не сойдётся с очками клуба.
    /// Место — протокольное (см. docs/competition-overview-cards.md).
    /// </summary>
    private async Task<List<ClubTopSwimmerDto>> BuildTopSwimmersAsync(
        int clubId, IReadOnlyList<StandingRow> scoped, int? season)
    {
        if (scoped.Count == 0) return [];

        var query = _read.Results.AsNoTracking().Where(r => r.ClubId == clubId);
        if (season is int s)
        {
            var (start, endExclusive) = SeasonMath.RangeOf(s);
            query = query.Where(r => r.CompetitionDate >= start && r.CompetitionDate < endExclusive);
        }

        var rows = await query
            .Select(r => new
            {
                r.SwimmerId,
                r.Position,
                r.TimeFail,
                IsRelay = r.RelayId != null,
                IsMasters = r.Competition.IsMasters,
                RuleId = r.Competition.PointRuleClubsId,
                r.CompetitionDate,
                r.Swimmer.LastName,
                r.Swimmer.FirstName,
                r.Swimmer.LastNameEn,
                r.Swimmer.FirstNameEn,
                r.Swimmer.Gender,
                r.Swimmer.BirthYear,
            })
            .ToListAsync();
        if (rows.Count == 0) return [];

        var rules = await _read.PointRulesClubs.AsNoTracking().Include(r => r.Entries).ToListAsync();
        var seasonForAge = season ?? SeasonMath.CurrentStartYear();

        var acc = new Dictionary<int, ClubTopSwimmerDto>();
        foreach (var r in rows)
        {
            var rule = CompetitionRuleResolver.Resolve(
                rules, r.RuleId, r.IsMasters, DateOnly.FromDateTime(r.CompetitionDate));
            var points = PointRulesClubsScoring.RelayPointsFor(rule, r.Position, r.TimeFail, r.IsRelay);

            if (!acc.TryGetValue(r.SwimmerId, out var dto))
            {
                acc[r.SwimmerId] = dto = new ClubTopSwimmerDto
                {
                    SwimmerId = r.SwimmerId,
                    LastName = r.LastName,
                    FirstName = r.FirstName,
                    LastNameEn = r.LastNameEn,
                    FirstNameEn = r.FirstNameEn,
                    Gender = NormalizeGender(r.Gender),
                    Age = r.BirthYear > 0 ? seasonForAge - r.BirthYear : null,
                };
            }

            dto.Points += points;
            switch (r.Position)
            {
                case 1: dto.Gold += 1; break;
                case 2: dto.Silver += 1; break;
                case 3: dto.Bronze += 1; break;
            }
        }

        return acc.Values
            .OrderByDescending(x => x.Points)
            .ThenByDescending(x => x.Gold)
            .ThenBy(x => x.LastNameEn)
            .Take(5)
            .ToList();
    }

    /// <summary>Swimmer.Gender живёт в двух форматах ("male"/"female" и "M"/"F") — наружу
    /// отдаём один, словесный.</summary>
    private static string? NormalizeGender(string? g) => g switch
    {
        "M" or "male" => "male",
        "F" or "female" => "female",
        _ => null,
    };

    /// <summary>Competition.Date хранится строкой «дд/ММ/гггг»; непарсибельное — в самый низ.</summary>
    private static DateTime ParseDate(string date) =>
        DateTime.TryParseExact(date, "dd/MM/yyyy",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var dt)
            ? dt
            : DateTime.MinValue;
}
