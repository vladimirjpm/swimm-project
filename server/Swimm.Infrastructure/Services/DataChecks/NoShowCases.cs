using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services.DataChecks;

/// <summary>Неявка вместе с тем, что стоит в её заплыве и дорожке, — вход <see cref="NoShowReasons"/>.</summary>
public sealed record NoShowCase(
    int CompetitionId, string Distance, string Style,
    NoShowReasons.Entry Entry, IReadOnlyList<NoShowReasons.LaneRow> Lane, IReadOnlyList<NoShowReasons.OwnRow> Own)
{
    public NoShowReasons.Reason Reason { get; } = NoShowReasons.Classify(Entry, Lane, Own);

    /// <summary>Строка для находки: кто, что, где и почему.</summary>
    public string Line =>
        $"#{Entry.Swimmer.Id} {Entry.Swimmer.Name} ({Entry.Swimmer.BirthYear}), {Distance} {Style}, "
        + $"заплыв {Entry.Heat}, дорожка {Entry.Lane}: {Reason.Text}";
}

/// <summary>
/// Загрузка неявок соревнований-дней с их заплывами и дорожками — общая для проверки
/// <see cref="NoShowUnmatchedCheck"/> и автосклейки двойников (<see cref="NoShowTwinMergeService"/>):
/// обе обязаны видеть одно и то же.
/// </summary>
public static class NoShowCases
{
    public static async Task<List<NoShowCase>> LoadAsync(
        SwimmDbContext db, IReadOnlyCollection<int> competitionIds, CancellationToken ct = default)
    {
        var ids = competitionIds.ToList();
        var entries = await db.CompetitionEntries.AsNoTracking()
            .Where(e => e.Status == CompetitionEntryStatus.NoShow && e.CompetitionId != null
                        && ids.Contains(e.CompetitionId.Value))
            .Select(e => new
            {
                CompetitionId = e.CompetitionId!.Value, e.SwimmerId, e.ClubId, e.StyleId, Style = e.Style.Name,
                e.Distance, e.Gender, e.Heat, e.Lane,
                Swimmer = new NoShowReasons.Person(e.SwimmerId, e.Swimmer.LastName, e.Swimmer.FirstName,
                    e.Swimmer.BirthYear, e.Swimmer.LogligId, e.Swimmer.ClubId),
            })
            .ToListAsync(ct);
        if (entries.Count == 0) return [];

        var styleIds = entries.Select(e => e.StyleId).Distinct().ToList();
        var ownEntries = (await db.CompetitionEntries.AsNoTracking()
                .Where(e => e.CompetitionId != null && ids.Contains(e.CompetitionId.Value) && styleIds.Contains(e.StyleId))
                .Select(e => new { CompetitionId = e.CompetitionId!.Value, e.SwimmerId, e.StyleId, e.Distance })
                .ToListAsync(ct))
            .Select(e => (e.CompetitionId, e.SwimmerId, e.StyleId, e.Distance))
            .ToHashSet();

        var rows = await db.Results.AsNoTracking()
            .Where(r => ids.Contains(r.CompetitionId) && styleIds.Contains(r.StyleId))
            .Select(r => new
            {
                r.CompetitionId, r.StyleId, r.Distance, r.Heat, r.Lane, r.SwimmerId, r.ClubId, r.Gender,
                r.TimeOriginal, r.RelayId,
                Swimmer = new NoShowReasons.Person(r.SwimmerId, r.Swimmer.LastName, r.Swimmer.FirstName,
                    r.Swimmer.BirthYear, r.Swimmer.LogligId, r.Swimmer.ClubId),
            })
            .ToListAsync(ct);

        var relayIds = rows.Where(r => r.RelayId != null).Select(r => r.RelayId!.Value).Distinct().ToList();
        var members = (await db.RelayMembers.AsNoTracking()
                .Where(m => relayIds.Contains(m.RelayId))
                .OrderBy(m => m.LegOrder)
                .Select(m => new
                {
                    m.RelayId,
                    Swimmer = new NoShowReasons.Person(m.SwimmerId, m.Swimmer.LastName, m.Swimmer.FirstName,
                        m.Swimmer.BirthYear, m.Swimmer.LogligId, m.Swimmer.ClubId),
                })
                .ToListAsync(ct))
            .ToLookup(m => m.RelayId, m => m.Swimmer);

        var byLane = rows.ToLookup(r => (r.CompetitionId, r.StyleId, r.Distance, r.Heat, r.Lane));
        var byOwn = rows.Where(r => r.RelayId == null).ToLookup(r => (r.CompetitionId, r.StyleId, r.Distance, r.SwimmerId));

        return entries
            .OrderBy(e => e.CompetitionId).ThenBy(e => e.Distance).ThenBy(e => e.Style).ThenBy(e => e.Heat).ThenBy(e => e.Lane)
            .Select(e =>
            {
                var isRelay = e.Distance.Contains('x', StringComparison.OrdinalIgnoreCase);
                var lane = byLane[(e.CompetitionId, e.StyleId, e.Distance, e.Heat, e.Lane)]
                    .Where(r => (r.RelayId != null) == isRelay)
                    .Select(r => new NoShowReasons.LaneRow(r.Swimmer, r.ClubId, r.Gender, r.TimeOriginal,
                        ownEntries.Contains((e.CompetitionId, r.SwimmerId, e.StyleId, e.Distance)),
                        r.RelayId is int id ? members[id].ToList() : []))
                    .ToList();
                var own = isRelay
                    ? []
                    : byOwn[(e.CompetitionId, e.StyleId, e.Distance, e.SwimmerId)]
                        .Where(r => r.Heat != e.Heat || r.Lane != e.Lane)
                        .Select(r => new NoShowReasons.OwnRow(r.Heat, r.Lane, r.TimeOriginal))
                        .ToList();
                return new NoShowCase(e.CompetitionId, e.Distance, e.Style,
                    new NoShowReasons.Entry(e.Swimmer, e.ClubId, e.Gender, isRelay, e.Heat, e.Lane), lane, own);
            })
            .ToList();
    }
}
