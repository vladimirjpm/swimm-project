using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Считает и материализует клубный зачёт соревнований в <see cref="ClubCompetitionStanding"/>.
///
/// Алгоритм не свой: очки/медали/пловцы агрегирует общий <see cref="ClubStandingCalculator"/> —
/// тот же, что отдаёт витрину <c>/api/club-summary</c> и карточку Top clubs. Здесь только
/// область (событие целиком), правило очков и запись результата.
/// </summary>
public class ClubStandingService : IClubStandingService
{
    private readonly SwimmDbContext _db;

    public ClubStandingService(SwimmDbContext db) => _db = db;

    public async Task<int> RebuildForCompetitionAsync(int competitionId, CancellationToken ct = default)
    {
        var days = await ScopeOfAsync(competitionId, ct);
        return days.Count == 0 ? 0 : await RebuildScopeAsync(days, ct);
    }

    public async Task<int> RebuildAllAsync(CancellationToken ct = default)
    {
        var scopes = await AllScopesAsync(ct);
        var total = 0;
        foreach (var days in scopes)
            total += await RebuildScopeAsync(days, ct);
        return total;
    }

    public async Task<int> RebuildForClubAsync(int clubId, CancellationToken ct = default)
    {
        // Соревнования, где клуб выступал. Берём по результатам, а не по таблице зачёта:
        // после merge строк зачёта у исчезнувшего клуба может уже не быть, а результаты есть.
        var competitionIds = await _db.Results.AsNoTracking()
            .Where(r => r.ClubId == clubId)
            .Select(r => r.CompetitionId)
            .Distinct()
            .ToListAsync(ct);

        var done = new HashSet<int>();
        var total = 0;
        foreach (var id in competitionIds)
        {
            if (!done.Add(id)) continue;
            var days = await ScopeOfAsync(id, ct);
            foreach (var d in days) done.Add(d.Id);
            if (days.Count > 0) total += await RebuildScopeAsync(days, ct);
        }
        return total;
    }

    /// <summary>Зачётная единица соревнования: сам старт либо все дни его события.</summary>
    private async Task<List<ScopeDay>> ScopeOfAsync(int competitionId, CancellationToken ct)
    {
        var comp = await _db.Competitions.AsNoTracking()
            .Where(c => c.Id == competitionId)
            .Select(c => new { c.Id, c.EventId })
            .FirstOrDefaultAsync(ct);
        if (comp is null) return [];

        if (comp.EventId is not int eventId)
        {
            var single = await _db.Competitions.AsNoTracking()
                .Where(c => c.Id == comp.Id)
                .Select(c => new ScopeDay(c.Id, c.DayNumber))
                .ToListAsync(ct);
            return single;
        }

        return await _db.Competitions.AsNoTracking()
            .Where(c => c.EventId == eventId)
            .Select(c => new ScopeDay(c.Id, c.DayNumber))
            .ToListAsync(ct);
    }

    /// <summary>Все зачётные единицы: однодневные сами по себе, дни события — вместе.</summary>
    private async Task<List<List<ScopeDay>>> AllScopesAsync(CancellationToken ct)
    {
        var all = await _db.Competitions.AsNoTracking()
            .Select(c => new { c.Id, c.EventId, c.DayNumber })
            .ToListAsync(ct);

        return all
            // Ключ: событие, а для одиночного — он сам (отрицательным, чтобы не пересечься с EventId).
            .GroupBy(c => c.EventId ?? -c.Id)
            .Select(g => g.Select(c => new ScopeDay(c.Id, c.DayNumber)).ToList())
            .ToList();
    }

    private async Task<int> RebuildScopeAsync(IReadOnlyList<ScopeDay> days, CancellationToken ct)
    {
        // Строка зачёта — одна на событие; вешаем её на ПЕРВЫЙ день (DayNumber, затем Id).
        // Иначе место клуба задвоится в истории и в KPI страницы клуба.
        var unitId = days
            .OrderBy(d => d.DayNumber ?? int.MaxValue)
            .ThenBy(d => d.Id)
            .First().Id;
        var dayIds = days.Select(d => d.Id).ToList();

        var rows = await _db.Results.AsNoTracking()
            .Where(r => dayIds.Contains(r.CompetitionId))
            // Псевдоклубы (страна/сборная в графе клуба) в клубный зачёт не входят.
            .Where(r => !r.Club.IsPseudo)
            .Select(r => new
            {
                r.ClubId,
                ClubName = r.Club.Name,
                ClubNameEn = r.Club.NameEn,
                RelayTeamName = r.Relay != null ? r.Relay.TeamName : null,
                r.SwimmerId,
                // Место prelim-заплыва — ранжир сессии, не награда: очков и медалей не даёт.
                Position = r.HeatType == "prelim" ? null : r.Position,
                r.TimeFail,
                IsRelay = r.RelayId != null,
                IsMasters = r.Competition.IsMasters,
                RuleId = r.Competition.PointRuleClubsId,
                r.CompetitionDate
            })
            .ToListAsync(ct);

        var rules = await _db.PointRulesClubs.AsNoTracking()
            .Include(r => r.Entries)
            .ToListAsync(ct);

        var scoring = new List<ClubScoringRow>(rows.Count);
        foreach (var r in rows)
        {
            var clubKey = FirstNonEmpty(r.ClubName, r.RelayTeamName, r.ClubNameEn);
            if (clubKey is null) continue;

            var rule = CompetitionRuleResolver.Resolve(
                rules, r.RuleId, r.IsMasters, DateOnly.FromDateTime(r.CompetitionDate));

            // Место — ПРОТОКОЛЬНОЕ, даже когда у соревнования включён ShowCombineAllResults.
            // «Combine All Results» — аналитический тоггл клиента (по умолчанию выключен,
            // store.filterSelected.is_recalculated = false): он ранжирует дисциплину поверх
            // возрастных полос. Официальный клубный зачёт идёт по протоколу — у каждой полосы
            // свой заплыв и свои места. Проверено на событии 1 (Kids зима 2026): по протоколу
            // у Дольфина 2430 очков и 12 золотых, по объединённым местам — 1743 и 7; витрина
            // Top clubs по умолчанию показывает первое. Если брать здесь второе, страница клуба
            // молча противоречила бы странице соревнования.
            var place = r.Position;
            var points = PointRulesClubsScoring.RelayPointsFor(rule, place, r.TimeFail, r.IsRelay);

            scoring.Add(new ClubScoringRow(
                ClubId: r.ClubId, ClubKey: clubKey, SwimmerId: r.SwimmerId,
                Place: place, IsRelay: r.IsRelay, Points: points));
        }

        var standings = ClubStandingCalculator.Build(scoring, ClubStandingKey.ById);

        // Полная замена зачёта единицы: старые строки могут ссылаться на клубы, которых
        // в новом расчёте уже нет (merge, правка результата) — переписывать по одной опаснее.
        var stale = await _db.ClubCompetitionStandings
            .Where(s => dayIds.Contains(s.CompetitionId))
            .ToListAsync(ct);
        _db.ClubCompetitionStandings.RemoveRange(stale);

        var now = DateTime.UtcNow;
        foreach (var s in standings)
        {
            _db.ClubCompetitionStandings.Add(new ClubCompetitionStanding
            {
                CompetitionId = unitId,
                ClubId = s.ClubId,
                Rank = s.Rank,
                Points = s.Points,
                SwimmerCount = s.SwimmerCount,
                ScoringSwims = s.ScoringSwims,
                SwimCount = s.SwimCount,
                Gold = s.Gold,
                Silver = s.Silver,
                Bronze = s.Bronze,
                ComputedAt = now
            });
        }

        await _db.SaveChangesAsync(ct);
        return standings.Count;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        return null;
    }

    /// <summary>День зачётной единицы: id и номер дня внутри события.</summary>
    private sealed record ScopeDay(int Id, int? DayNumber);
}
