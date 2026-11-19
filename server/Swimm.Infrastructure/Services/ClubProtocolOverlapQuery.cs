using Microsoft.EntityFrameworkCore;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Выборка для <see cref="ClubPairOverlapRule"/> (Р113): где клубы пар встречаются в протоколах.
/// Три запроса на весь набор пар — строки результатов клубов (без синтетики их немного), клубные
/// зачёты и названия соревнований-примеров. Читает, ничего не пишет.
/// </summary>
internal static class ClubProtocolOverlapQuery
{
    public static async Task<Dictionary<(int A, int B), ClubPairOverlap>> LoadAsync(
        SwimmDbContext db, IReadOnlyCollection<(int A, int B)> pairs, CancellationToken ct)
    {
        var result = new Dictionary<(int A, int B), ClubPairOverlap>();
        if (pairs.Count == 0) return result;
        var clubIds = pairs.SelectMany(p => new[] { p.A, p.B }).Distinct().ToArray();

        var rows = await db.Results.AsNoTracking()
            .Where(r => clubIds.Contains(r.ClubId))
            .GroupBy(r => new { r.ClubId, r.CompetitionId, r.Competition.EventId, r.SwimmerId, Relay = r.RelayId != null })
            .Select(g => new { g.Key.ClubId, g.Key.CompetitionId, g.Key.EventId, g.Key.SwimmerId, g.Key.Relay, Date = g.Max(r => r.CompetitionDate) })
            .ToListAsync(ct);
        var standings = await db.ClubCompetitionStandings.AsNoTracking()
            .Where(s => clubIds.Contains(s.ClubId))
            .Select(s => new { s.ClubId, s.CompetitionId, s.Competition.EventId })
            .Distinct()
            .ToListAsync(ct);

        var presence = clubIds.ToDictionary(id => id, _ => new ClubProtocolPresence());
        foreach (var r in rows)
            presence[r.ClubId].AddResult(ClubPairOverlapRule.MeetKey(r.CompetitionId, r.EventId), r.CompetitionId, r.Date, r.SwimmerId, r.Relay);
        foreach (var s in standings)
            presence[s.ClubId].StandingMeets.Add(ClubPairOverlapRule.MeetKey(s.CompetitionId, s.EventId));

        // Сначала без примеров — узнать, какие соревнования показывать, и взять их названия одним запросом.
        var bare = pairs.ToDictionary(p => p, p => ClubPairOverlapRule.Build(presence[p.A], presence[p.B]));
        var exampleIds = pairs.Where(p => bare[p].Meets > 0)
            .SelectMany(p => presence[p.A].Meets.Where(m => presence[p.B].Meets.ContainsKey(m.Key))
                .OrderByDescending(m => m.Value.Date).Take(ClubPairOverlapRule.ExampleCount).Select(m => m.Value.CompetitionId))
            .Distinct().ToArray();
        var names = exampleIds.Length == 0 ? [] : await db.Competitions.AsNoTracking()
            .Where(c => exampleIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new ClubPairOverlapMeet(c.Id, c.Name, c.Date), ct);

        foreach (var p in pairs)
            result[p] = bare[p].Meets == 0 ? bare[p]
                : ClubPairOverlapRule.Build(presence[p.A], presence[p.B], id => names.GetValueOrDefault(id));
        return result;
    }
}
