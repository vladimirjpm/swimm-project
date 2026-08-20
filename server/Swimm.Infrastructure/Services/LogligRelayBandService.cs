using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="ILogligRelayBandService"/>
public sealed class LogligRelayBandService(
    SwimmDbContext db,
    ICompetitionDiscoveryProvider provider,
    ICompetitionRecalculationService recalc,
    ICacheService cache,
    ILogger<LogligRelayBandService> logger) : ILogligRelayBandService
{
    public async Task<LogligRelayBandReport> RepairAsync(
        int discoveredId, bool apply, CancellationToken ct = default)
    {
        var discovered = await db.DiscoveredCompetitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == discoveredId, ct)
            ?? throw new InvalidOperationException($"Запись автозабора #{discoveredId} не найдена");

        if (discovered.LogligId is not int logligId)
            throw new InvalidOperationException(
                $"У записи #{discoveredId} нет LogligId — пособытийного источника у неё нет");

        // Соревнование ищем по OrgCompId и по всем дням: эстафеты многодневки могут лежать
        // в разных днях, а идентичность события — это OrgCompId (Д2), не название.
        var competitions = await db.Competitions.AsNoTracking()
            .Where(c => c.OrgCompId == discovered.OrgCompId)
            .Select(c => new { c.Id, c.Date, c.IsMasters, c.PointRuleClubsId })
            .ToListAsync(ct);
        if (competitions.Count == 0)
            throw new InvalidOperationException(
                $"Соревнование с OrgCompId {discovered.OrgCompId} ещё не импортировано — " +
                "ремонт полос только ПРАВИТ уже существующие эстафетные строки.");

        var competitionIds = competitions.Select(c => c.Id).ToList();

        var eventIds = await provider.FetchEventIdsAsync(logligId, ct);
        logger.LogInformation("loglig {LogligId}: событий {Count}, ищу эстафетные", logligId, eventIds.Count);

        var competitionName = string.Empty;
        var source = new List<RelayRowFromSource>();
        var relayEvents = 0;

        foreach (var eventId in eventIds)
        {
            var ev = await provider.FetchEventResultsAsync(eventId, ct);
            if (competitionName.Length == 0) competitionName = ev.CompetitionName;
            if (!ev.IsRelay) continue;

            relayEvents++;
            foreach (var r in ev.Rows)
                source.Add(new RelayRowFromSource(
                    ev.StyleName, ev.Distance, r.Club, SwimTime.ParseToMs(r.Time), r.Position,
                    // Пол и полоса — из шапки события: подзаголовок секции у эстафет 1581
                    // подписан «גמר ישיר - נשים 19-99» при плывущих детях 14-15.
                    ev.Gender, ev.AgeBand,
                    // Пустая ячейка «ניקוד קבוצתי» — «организатор не заплатил», а не «нет
                    // данных»: колонка есть всегда (то же решение, что у личных строк).
                    r.ClubPoints ?? 0));
        }

        var ours = await db.Results.AsNoTracking()
            .Where(r => competitionIds.Contains(r.CompetitionId) && r.RelayId != null)
            .Select(r => new RelayRowInDb(
                r.Id, r.Style!.Name, r.Distance, r.Club!.Name, r.TimeMillisecond, r.Position,
                r.Gender, r.EventStyleAge, r.AgeGroup, r.OfficialClubPoints))
            .ToListAsync(ct);

        var plan = LogligRelayBandMatcher.Build(source, ours);

        // Очки «до/после» считаем тем же движком, что и витрина, — иначе отчёт обещал бы
        // одно, а зачёт показал другое. Правило берём соревнования, а не подбираем заново.
        var rules = await db.PointRulesClubs.AsNoTracking().Include(r => r.Entries).ToListAsync(ct);
        var head = competitions[0];
        var rule = CompetitionRuleResolver.Resolve(
            rules, head.PointRuleClubsId, head.IsMasters, ParseDate(head.Date));

        var byId = ours.ToDictionary(r => r.ResultId);
        var pointsBefore = ours.Sum(r => PointRulesClubsScoring.RelayPointsFor(
            rule, r.Position, r.TimeMs is null, isRelay: true));
        var pointsAfter = plan.Changes.Sum(c => PointRulesClubsScoring.RelayPointsFor(
            rule, c.PositionAfter, byId[c.ResultId].TimeMs is null, isRelay: true));

        var applied = 0;
        if (apply && plan.CanApply)
            applied = await ApplyAsync(plan, competitionIds, ct);

        return new LogligRelayBandReport(
            competitionName, relayEvents, source.Count, ours.Count,
            source.Sum(r => r.OfficialClubPoints), pointsBefore, pointsAfter, applied, plan);
    }

    /// <summary>
    /// Записать план. AgeGroup ставится равной полосе — так же, как у восстановленных полос
    /// Маккаби (<c>RelayBandReconstructor.BandEvent</c>): у эстафеты нет «возраста пловца»,
    /// а сетка возрастных групп к её полосе («14-15») не применима.
    /// <c>RelayId</c>, состав, ноги, время, заплыв и дорожка не трогаются — они правильные.
    /// </summary>
    private async Task<int> ApplyAsync(
        RelayBandPlan plan, IReadOnlyList<int> competitionIds, CancellationToken ct)
    {
        var changed = plan.Changes.Where(c => c.HasChanges).ToList();
        if (changed.Count == 0) return 0;

        var ids = changed.Select(c => c.ResultId).ToHashSet();
        var rows = await db.Results.Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);

        foreach (var change in changed)
        {
            var row = rows[change.ResultId];
            row.Gender = change.GenderAfter;
            row.EventStyleAge = change.BandAfter;
            row.AgeGroup = change.BandAfter;
            row.Position = change.PositionAfter;
            row.OfficialClubPoints = change.OfficialAfter;
        }

        await db.SaveChangesAsync(ct);

        // Клубный зачёт и объединённые места материализованы — тот же шов, что у импорта
        // и ручной правки результата. Без него правка мест не доедет до витрины.
        foreach (var competitionId in competitionIds)
            await recalc.RecalculateCompetitionAsync(competitionId, ct);
        await cache.InvalidateAllAsync();

        logger.LogInformation("Полосы эстафет: обновлено строк {Count}", changed.Count);
        return changed.Count;
    }

    private static DateOnly ParseDate(string date) =>
        DateOnly.TryParseExact(date, "dd/MM/yyyy", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed
            : DateOnly.MinValue;
}
