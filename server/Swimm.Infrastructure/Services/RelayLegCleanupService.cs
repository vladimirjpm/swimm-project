using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Реализация <see cref="IRelayLegCleanupService"/>: проходит все эстафеты базы с записанными
/// этапами, судит их тем же <see cref="RelayLegFloor"/>, что и доклейка, и стирает этапы у
/// команд с невозможным этапом. Каждая команда — строка аудита <c>relay.splits-clear</c>.
/// </summary>
public sealed class RelayLegCleanupService(SwimmDbContext db, ILogger<RelayLegCleanupService> logger)
    : IRelayLegCleanupService
{
    internal const string AuditAction = "relay.splits-clear";

    private static readonly JsonSerializerOptions AuditJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<RelayLegCleanupReport> RunAsync(
        bool apply, IReadOnlyCollection<int>? competitionIds = null, CancellationToken ct = default)
    {
        var floor = await RelayLegFloorSource.LoadAsync(db, ct);

        // Область: эстафеты выбранных соревнований (импорт) или вся база (консоль).
        var scope = competitionIds is null
            ? null
            : await db.Results.AsNoTracking()
                .Where(r => r.RelayId != null && competitionIds.Contains(r.CompetitionId))
                .Select(r => r.RelayId!.Value).Distinct().ToListAsync(ct);

        var legs = await db.RelayMembers.AsNoTracking()
            .Where(m => scope == null || scope.Contains(m.RelayId))
            .Where(m => db.RelayMembers.Any(x => x.RelayId == m.RelayId && x.SplitTime != null))
            .Select(m => new { m.RelayId, m.Id, m.LegOrder, m.Swimmer.BirthYear, m.Swimmer.Gender, m.SplitTime })
            .ToListAsync(ct);
        var relayIds = legs.Select(l => l.RelayId).Distinct().ToList();

        // Строка-владелец эстафеты: у одной эстафеты строк бывает несколько — берём первую по Id.
        var owners = (await db.Results.AsNoTracking()
                .Where(r => r.RelayId != null && relayIds.Contains(r.RelayId!.Value))
                .Select(r => new
                {
                    r.Id, RelayId = r.RelayId!.Value, r.CompetitionId, Style = r.Style.Name, r.Distance,
                    r.TimeOriginal, r.CompetitionDate.Year, r.Competition.PoolType,
                })
                .ToListAsync(ct))
            .GroupBy(r => r.RelayId)
            .Select(g => g.OrderBy(r => r.Id).First())
            .ToList();

        var legsByRelay = legs.GroupBy(l => l.RelayId).ToDictionary(g => g.Key, g => g.ToList());
        var rows = new List<RelayLegCleanupRow>();
        var toClear = new List<int>();
        foreach (var o in owners)
        {
            var teamLegs = legsByRelay[o.RelayId];
            var row = new RelayRow(o.Id, o.RelayId, o.Style, o.Distance, null, null, o.TimeOriginal,
                teamLegs.Select(l => new RelayLegRow(l.Id, l.LegOrder, l.BirthYear, l.SplitTime, l.Gender)).ToList(),
                o.Year, o.PoolType);
            if (floor.TeamImplausible(row) is not string reason) continue;

            var ordered = teamLegs.OrderBy(l => l.LegOrder).ToList();
            rows.Add(new RelayLegCleanupRow(o.RelayId, o.CompetitionId, o.Distance, o.Style, o.TimeOriginal, reason,
                ordered.Select(l => l.SplitTime).ToList()));
            toClear.AddRange(ordered.Where(l => l.SplitTime != null).Select(l => l.Id));
        }

        if (apply && toClear.Count > 0)
            await WriteAsync(rows, toClear, legs.ToDictionary(l => l.Id, l => (l.RelayId, l.LegOrder, l.SplitTime)),
                competitionIds is null ? "cli --clear-implausible-legs" : "import", ct);

        return new RelayLegCleanupReport(apply, owners.Count, rows, toClear.Count);
    }

    private async Task WriteAsync(
        List<RelayLegCleanupRow> rows, List<int> memberIds,
        Dictionary<int, (int RelayId, int LegOrder, string? SplitTime)> legOf, string actor, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var members = await db.RelayMembers.Where(m => memberIds.Contains(m.Id)).ToListAsync(ct);
            foreach (var m in members) m.SplitTime = null;

            foreach (var r in rows)
            {
                db.AdminAudits.Add(new AdminAudit
                {
                    ActorName = actor,
                    Action = AuditAction,
                    EntityType = "Relay",
                    EntityId = r.RelayId.ToString(),
                    Summary = Truncate(
                        $"Этапы обнулены (comp {r.CompetitionId}, {r.Distance} {r.Style}, итог {r.Time}): {r.Reason}", 1000),
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        competitionId = r.CompetitionId,
                        old = memberIds.Where(id => legOf[id].RelayId == r.RelayId)
                            .Select(id => new { memberId = id, leg = legOf[id].LegOrder, split = legOf[id].SplitTime })
                            .OrderBy(x => x.leg),
                        reason = r.Reason,
                    }, AuditJson),
                });
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        logger.LogInformation("Невозможные этапы: обнулено {Legs} ног у {Teams} команд", memberIds.Count, rows.Count);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
