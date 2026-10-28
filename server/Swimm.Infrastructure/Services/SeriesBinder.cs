using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Узнавание выпуска серии (см. <see cref="ISeriesBinder"/>, правило — <see cref="SeriesMatch"/>).
/// Названий соревнований — тысячи строк, их сверяем в памяти; возраста пловцов грузим только у
/// подошедших дней.
/// </summary>
public class SeriesBinder(SwimmDbContext db) : ISeriesBinder
{
    public async Task<IReadOnlyList<SeriesBindingDto>> EvaluateAsync(
        IReadOnlyCollection<int>? competitionIds = null, CancellationToken ct = default) =>
        await EvaluateCoreAsync(competitionIds, ct);

    public async Task<IReadOnlyList<SeriesBindingDto>> BindAsync(
        IReadOnlyCollection<int> competitionIds, CancellationToken ct = default)
    {
        if (competitionIds.Count == 0) return [];
        var found = await EvaluateCoreAsync(competitionIds, ct);

        var toBind = found
            .Where(f => f.CurrentPartId is null && f.SeriesId is not null && f.PickedPartId is not null)
            .ToList();
        if (toBind.Count == 0) return found;

        var ids = toBind.Select(f => f.CompetitionId).ToList();
        var days = await db.Competitions.Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        foreach (var day in days)
            day.SeriesPartId = toBind.First(f => f.CompetitionId == day.Id).PickedPartId;
        await db.SaveChangesAsync(ct);

        return found
            .Select(f => ids.Contains(f.CompetitionId)
                ? f with { CurrentPartId = f.PickedPartId, CurrentPartName = f.PickedPartName, CurrentPartShare = f.Share, BoundNow = true }
                : f)
            .ToList();
    }

    private async Task<List<SeriesBindingDto>> EvaluateCoreAsync(IReadOnlyCollection<int>? competitionIds, CancellationToken ct)
    {
        var series = await db.CompetitionSeries.AsNoTracking()
            .Include(s => s.Parts)
            .Include(s => s.Regulations)
            .ToListAsync(ct);
        if (series.Count == 0) return [];

        var compQuery = db.Competitions.AsNoTracking();
        if (competitionIds is not null) compQuery = compQuery.Where(c => competitionIds.Contains(c.Id));
        var comps = await compQuery
            .Select(c => new
            {
                c.Id, c.Name, c.SubName, EventName = c.Event != null ? c.Event.Name : null, c.Date, c.DayNumber,
                c.SeriesPartId, c.SeriesRegulationCheckedAt
            })
            .ToListAsync(ct);

        var partOwner = series.SelectMany(s => s.Parts.Select(p => (p, s))).ToDictionary(x => x.p.Id, x => x.s);
        var candidates = comps
            .Select(c =>
            {
                // Фразу ищем во всех именах дня: у дня события своё имя — копия имени события,
                // SubName — заголовок файла.
                var names = new[] { c.EventName, c.Name, c.SubName };
                var matched = series
                    .Where(s => names.Any(n => SeriesMatch.NameMatches(n, s.MatchPhraseList())))
                    .ToList();
                return (Comp: c, Matched: matched);
            })
            .Where(x => x.Matched.Count > 0 || x.Comp.SeriesPartId is not null)
            .ToList();
        if (candidates.Count == 0) return [];

        var candidateIds = candidates.Select(x => x.Comp.Id).ToList();
        var swimmerYears = await db.Results.AsNoTracking()
            .Where(r => candidateIds.Contains(r.CompetitionId) && r.RelayId == null && r.Swimmer.BirthYear > 0)
            .Select(r => new { r.CompetitionId, r.SwimmerId, r.Swimmer.BirthYear })
            .Distinct()
            .ToListAsync(ct);
        var yearsByDay = swimmerYears.GroupBy(x => x.CompetitionId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.BirthYear).ToList());

        var result = new List<SeriesBindingDto>();
        foreach (var (c, matched) in candidates)
        {
            var date = ParseDate(c.Date);
            var ages = date is DateTime d
                ? yearsByDay.GetValueOrDefault(c.Id, [])
                    .Select(y => SeasonMath.AgeInSeason(SeasonMath.StartYearOf(d), y))
                    .OfType<int>().ToList()
                : [];

            // Привязанный день оцениваем в его серии; непривязанный — в единственной подошедшей.
            var current = c.SeriesPartId is int pid ? partOwner.GetValueOrDefault(pid) : null;
            var target = current ?? (matched.Count == 1 ? matched[0] : null);
            var name = c.EventName ?? c.Name;

            if (target is null)
            {
                result.Add(new SeriesBindingDto(c.Id, name, c.Date, c.DayNumber, null, null, null, null, null, null, 0, null,
                    $"название подходит под несколько серий: {string.Join(", ", matched.Select(s => s.Name))}",
                    false, c.SeriesRegulationCheckedAt));
                continue;
            }

            var parts = target.Parts
                .Select(p => new SeriesMatch.PartAges(p.Id, p.SortOrder, p.MinSeasonAge, p.MaxSeasonAge))
                .ToList();
            var pick = SeriesMatch.PickPart(parts, ages, c.DayNumber);
            var currentPart = c.SeriesPartId is int cp ? target.Parts.FirstOrDefault(p => p.Id == cp) : null;
            double? currentShare = currentPart is null ? null
                : ages.Count == 0 ? 0
                : ages.Count(a => (currentPart.MinSeasonAge is not int min || a >= min)
                                  && (currentPart.MaxSeasonAge is not int max || a <= max)) / (double)ages.Count;
            if (currentPart is not null && target.Parts.Count == 1) currentShare = 1;

            var hasRegulation = date is DateTime dd
                && target.Regulations.Any(r => r.EffectiveFrom <= DateOnly.FromDateTime(dd));

            result.Add(new SeriesBindingDto(
                c.Id, name, c.Date, c.DayNumber,
                currentPart?.Id, currentPart?.Name,
                target.Id, target.Name,
                pick.PartId, target.Parts.FirstOrDefault(p => p.Id == pick.PartId)?.Name,
                pick.Share, currentShare, pick.Reason,
                hasRegulation, c.SeriesRegulationCheckedAt));
        }
        return result;
    }

    private static DateTime? ParseDate(string date) =>
        DateTime.TryParseExact(date, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
}
