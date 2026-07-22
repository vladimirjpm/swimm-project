using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Бэкфилл RelayMembers для эстафет, импортированных до появления структурных ног.
/// Матчинг ног из текста Relay.SwimmersName сужен до ростера соревнования эстафеты
/// (при неоднозначности — до её клуба); линкуем только при однозначном совпадении.
/// Владелец строки — гарантированный якорь. Fail-safe и идемпотентно.
/// </summary>
public class RelayMemberBackfillService : IRelayMemberBackfillService
{
    private readonly SwimmDbContext _db;

    public RelayMemberBackfillService(SwimmDbContext db)
    {
        _db = db;
    }

    private sealed class Candidate
    {
        public int SwimmerId { get; init; }
        public string Nh { get; init; } = "";      // Normalize("Last First")
        public string NhSwap { get; init; } = "";  // Normalize("First Last")
        public string? Ne { get; init; }           // Normalize("LastEn FirstEn"), если есть EN
        public string? NeSwap { get; init; }
        public HashSet<int> ClubIds { get; } = new();
    }

    public async Task<RelayBackfillReport> BackfillAsync(bool apply)
    {
        var report = new RelayBackfillReport { Applied = apply };

        // 1. Эстафеты без структурного состава (идемпотентность — уже заполненные не трогаем).
        var relays = await _db.Relays
            .AsNoTracking()
            .Where(r => !_db.RelayMembers.Any(m => m.RelayId == r.Id))
            .Select(r => new { r.Id, r.SwimmersName })
            .ToListAsync();
        report.RelaysTotal = relays.Count;
        if (relays.Count == 0) return report;

        var relayIds = relays.Select(r => r.Id).ToList();

        // 2. Соревнование/клуб/владелец по каждой эстафете (эстафета ↔ обычно один результат).
        var relayResult = (await _db.Results
            .AsNoTracking()
            .Where(r => r.RelayId != null && relayIds.Contains(r.RelayId.Value))
            .Select(r => new { RelayId = r.RelayId!.Value, r.CompetitionId, r.ClubId, r.SwimmerId })
            .ToListAsync())
            .GroupBy(x => x.RelayId)
            .ToDictionary(g => g.Key, g => g.First());

        // 3. Ростеры соревнований (кандидаты для матчинга ног) — по одному разу на соревнование.
        var comps = relayResult.Values.Select(v => v.CompetitionId).Distinct().ToList();
        var rosterRows = await _db.Results
            .AsNoTracking()
            .Where(r => comps.Contains(r.CompetitionId))
            .Select(r => new
            {
                r.CompetitionId, r.SwimmerId, r.ClubId,
                r.Swimmer.LastName, r.Swimmer.FirstName, r.Swimmer.LastNameEn, r.Swimmer.FirstNameEn
            })
            .ToListAsync();

        var roster = new Dictionary<int, Dictionary<int, Candidate>>();
        foreach (var row in rosterRows)
        {
            if (!roster.TryGetValue(row.CompetitionId, out var byId))
                roster[row.CompetitionId] = byId = new Dictionary<int, Candidate>();
            if (!byId.TryGetValue(row.SwimmerId, out var cand))
            {
                var hasEn = (row.LastNameEn + row.FirstNameEn).Length > 0;
                byId[row.SwimmerId] = cand = new Candidate
                {
                    SwimmerId = row.SwimmerId,
                    Nh = SwimmerDedupService.Normalize($"{row.LastName} {row.FirstName}"),
                    NhSwap = SwimmerDedupService.Normalize($"{row.FirstName} {row.LastName}"),
                    Ne = hasEn ? SwimmerDedupService.Normalize($"{row.LastNameEn} {row.FirstNameEn}") : null,
                    NeSwap = hasEn ? SwimmerDedupService.Normalize($"{row.FirstNameEn} {row.LastNameEn}") : null,
                };
            }
            cand.ClubIds.Add(row.ClubId);
        }

        var newMembers = new List<RelayMember>();

        // 4. Матчинг ног.
        foreach (var relay in relays)
        {
            if (!relayResult.TryGetValue(relay.Id, out var info)) continue; // эстафета без результата — пропуск
            if (!roster.TryGetValue(info.CompetitionId, out var candidates)) continue;

            var names = (relay.SwimmersName ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var linkedInThisRelay = new HashSet<int>();
            var relayGotAny = false;

            for (var i = 0; i < names.Length; i++)
            {
                var legNorm = SwimmerDedupService.Normalize(names[i]);
                if (legNorm.Length == 0) continue;

                var matched = ResolveLeg(legNorm, candidates.Values, info.ClubId);
                if (matched == null)
                {
                    report.LegsUnmatched++;
                    if (report.UnmatchedSamples.Count < 25)
                        report.UnmatchedSamples.Add($"relay {relay.Id} (comp {info.CompetitionId}): '{names[i]}'");
                    continue;
                }
                if (!linkedInThisRelay.Add(matched.Value)) continue; // дубль внутри эстафеты

                newMembers.Add(new RelayMember { RelayId = relay.Id, SwimmerId = matched.Value, LegOrder = i + 1 });
                report.LegsLinked++;
                relayGotAny = true;
            }

            // Якорь: владелец строки — гарантированно участник, даже если его имя не сматчилось.
            if (linkedInThisRelay.Add(info.SwimmerId))
            {
                newMembers.Add(new RelayMember { RelayId = relay.Id, SwimmerId = info.SwimmerId, LegOrder = 0 });
                report.LegsLinked++;
                relayGotAny = true;
            }

            if (relayGotAny) report.RelaysLinked++;
        }

        if (apply && newMembers.Count > 0)
        {
            _db.RelayMembers.AddRange(newMembers);
            await _db.SaveChangesAsync();
        }

        return report;
    }

    /// <summary>Однозначный SwimmerId ноги в ростере, иначе null. При неоднозначности —
    /// сужаем до клуба эстафеты; если и там не однозначно — не линкуем.</summary>
    private static int? ResolveLeg(string legNorm, IEnumerable<Candidate> candidates, int relayClubId)
    {
        var matches = candidates
            .Where(c => legNorm == c.Nh || legNorm == c.NhSwap
                        || (c.Ne != null && legNorm == c.Ne) || (c.NeSwap != null && legNorm == c.NeSwap))
            .ToList();

        if (matches.Count == 0) return null;
        if (matches.Count == 1) return matches[0].SwimmerId;

        // Неоднозначно по соревнованию — сузим до клуба эстафеты.
        var inClub = matches.Where(c => c.ClubIds.Contains(relayClubId)).ToList();
        return inClub.Count == 1 ? inClub[0].SwimmerId : (int?)null;
    }
}
