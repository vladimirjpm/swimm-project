using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Dtos;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Services;

namespace Swimm.Infrastructure.Repositories;

// Карточка «Official awards» (Overview): награждение по регламенту серии турнира, рядом со
// стандартным High Point (docs/plans/official-awards-plan.md §5.3–§5.4).
public partial class ResultRepository
{
    /// <summary>
    /// Блок на каждый день выборки, привязанный к части серии и с действующим на его дату
    /// регламентом. Считается по наградам СВОЕЙ части и строкам СВОЕГО дня — не одно правило на
    /// всё событие, как у High Point в режиме события (§4 плана): у дней разные регламенты.
    /// </summary>
    private async Task<List<OverviewOfficialAwardsDayDto>> LoadOfficialAwardsAsync(
        IQueryable<ResultRecord> query, ResultFilter filter, IReadOnlyList<OverviewDayDto> days)
    {
        var dayIds = days.Select(d => d.CompetitionId).ToList();
        var seriesDays = await _db.Competitions.AsNoTracking()
            .Where(c => dayIds.Contains(c.Id) && c.SeriesPartId != null)
            .Select(c => new
            {
                c.Id, c.Date, c.DayNumber, PartId = c.SeriesPartId!.Value, c.SeriesRegulationCheckedAt,
                PartName = c.SeriesPart!.Name, PartNameEn = c.SeriesPart.NameEn,
                c.SeriesPart.SeriesId, SeriesName = c.SeriesPart.Series.Name, SeriesNameEn = c.SeriesPart.Series.NameEn
            })
            .ToListAsync();
        if (seriesDays.Count == 0) return [];

        var seriesIds = seriesDays.Select(d => d.SeriesId).Distinct().ToList();
        var regulations = await _db.SeriesRegulations.AsNoTracking()
            .Where(r => seriesIds.Contains(r.SeriesId))
            .Include(r => r.Awards).ThenInclude(a => a.Bands)
            .Include(r => r.Awards).ThenInclude(a => a.Prizes)
            .ToListAsync();

        // Блоки — в порядке дней Overview (по номеру дня, затем дате).
        var dayOrder = days.Select((d, i) => (d.CompetitionId, i)).ToDictionary(x => x.CompetitionId, x => x.i);
        var result = new List<OverviewOfficialAwardsDayDto>();
        foreach (var day in seriesDays.OrderBy(d => dayOrder[d.Id]))
        {
            if (!DateTime.TryParseExact(day.Date, "dd/MM/yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
                continue;

            var regulation = regulations
                .Where(r => r.SeriesId == day.SeriesId && r.EffectiveFrom <= DateOnly.FromDateTime(date))
                .MaxBy(r => r.EffectiveFrom);
            var awards = regulation?.Awards
                .Where(a => a.PartId == day.PartId)
                .OrderBy(a => a.SortOrder).ThenBy(a => a.Id)
                .ToList();
            if (regulation is null || awards is not { Count: > 0 }) continue;

            var rows = await LoadOfficialAwardRowsAsync(query.Where(r => r.CompetitionId == day.Id), filter);
            var seasonStart = SeasonMath.StartYearOf(date);
            var scoringRows = rows
                .Select(r => new SeriesAwardRow(r.SwimmerId, r.Gender,
                    SeasonMath.AgeInSeason(seasonStart, r.BirthYear), r.InternationalPoints))
                .ToList();
            var bySwimmer = rows.GroupBy(r => r.SwimmerId).ToDictionary(g => g.Key, g => g.First());

            var awardDtos = awards
                .Select(a => new OverviewOfficialAwardDto
                {
                    Title = a.Title,
                    Kind = a.Kind,
                    CountBest = a.CountBest,
                    Groups = SeriesAwardScoring.Compute(a, scoringRows)
                        .Select(g => new OverviewOfficialAwardGroupDto
                        {
                            Gender = g.Gender,
                            MinAge = g.MinSeasonAge,
                            MaxAge = g.MaxSeasonAge,
                            Places = g.Places.Select(p => OfficialPlaceDto(p, bySwimmer[p.SwimmerId])).ToList()
                        })
                        .ToList()
                })
                .Where(a => a.Groups.Any(g => g.Places.Count > 0))
                .ToList();
            if (awardDtos.Count == 0) continue;

            result.Add(new OverviewOfficialAwardsDayDto
            {
                CompetitionId = day.Id,
                DayNumber = day.DayNumber,
                Date = day.Date,
                SeriesName = day.SeriesName,
                SeriesNameEn = day.SeriesNameEn,
                PartName = day.PartName,
                PartNameEn = day.PartNameEn,
                Regulation = new OverviewSeriesRegulationDto
                {
                    EffectiveFrom = regulation.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    SourceUrl = regulation.SourceUrl,
                    Checked = day.SeriesRegulationCheckedAt != null
                },
                Awards = awardDtos
            });
        }
        return result;
    }

    private static OverviewOfficialAwardPlaceDto OfficialPlaceDto(SeriesAwardPlace p, OfficialAwardSourceRow src) => new()
    {
        Place = p.Place,
        SwimmerId = p.SwimmerId,
        FirstName = src.FirstName,
        LastName = src.LastName,
        FirstNameEn = src.FirstNameEn,
        LastNameEn = src.LastNameEn,
        Club = src.Club,
        ClubId = src.ClubId,
        Points = p.Points,
        Parts = p.Parts,
        Prize = p.Prize,
        IsTie = p.IsTie
    };

    /// <summary>
    /// Строки официального зачёта дня: личные старты, без TimeFail, с очками FINA. Помеченные
    /// ошибки протокола (SuspectReason) не участвуют — награда это наш вывод (Р76, §8 В5 плана).
    /// Пара-очки с FINA не сравнимы (Р67). Combine — дисциплина один раз, как у High Point.
    /// </summary>
    private static Task<List<OfficialAwardSourceRow>> LoadOfficialAwardRowsAsync(
        IQueryable<ResultRecord> dayQuery, ResultFilter filter) =>
        dayQuery
            .Where(r => r.RelayId == null && !r.TimeFail && r.SuspectReason == null
                        && !r.IsParaPoints && r.InternationalPoints > 0
                        && (r.Gender == "male" || r.Gender == "female")
                        && (!filter.Combined || !r.Competition.ShowCombineAllResults || r.IsBestResult != false))
            .Select(r => new OfficialAwardSourceRow
            {
                SwimmerId = r.SwimmerId,
                FirstName = r.Swimmer.FirstName,
                LastName = r.Swimmer.LastName,
                FirstNameEn = r.Swimmer.FirstNameEn,
                LastNameEn = r.Swimmer.LastNameEn,
                Club = r.Club.Name,
                ClubId = r.ClubId,
                Gender = r.Gender,
                BirthYear = r.Swimmer.BirthYear,
                InternationalPoints = r.InternationalPoints
            })
            .ToListAsync();

    private sealed class OfficialAwardSourceRow
    {
        public int SwimmerId { get; init; }
        public string FirstName { get; init; } = "";
        public string LastName { get; init; } = "";
        public string FirstNameEn { get; init; } = "";
        public string LastNameEn { get; init; } = "";
        public string Club { get; init; } = "";
        public int ClubId { get; init; }
        public string Gender { get; init; } = "";
        public int BirthYear { get; init; }
        public int InternationalPoints { get; init; }
    }
}
