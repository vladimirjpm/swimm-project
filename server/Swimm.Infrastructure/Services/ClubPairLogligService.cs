using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Что знает loglig про общих пловцов пары клубов-кандидатов в дубли (Р112, /Admin/Clubs,
/// эвристика «общие пловцы»). Вердикт по пловцу — чистое <see cref="ClubPairLogligRule"/>;
/// здесь выборка, сеть и запись.
///
/// • Чтение (<see cref="GetAsync"/>, <see cref="SummarizeAsync"/>) — только сохранённые ответы
///   <c>Sys_SwimmerLogligClubs</c>, в сеть не ходит: страница кандидатов открывается без loglig.
/// • Кнопка (<see cref="CheckAsync"/>) — карточки общих пловцов с несвежим ответом, по одной,
///   ~1 запрос в секунду (как сверка кандидатов Р98), не больше лимита за нажатие; каждый удачный
///   ответ сохраняется сразу — прерванная проверка не теряет сделанного.
/// • Общие пловцы — как у эвристики (<see cref="ClubDedupService"/>): клуб карточки и клубы строк
///   результатов, по самим Id пары (склеенные в кандидаты не попадают).
/// </summary>
public sealed class ClubPairLogligService(
    SwimmDbContext db,
    ILogligClient loglig,
    ILogger<ClubPairLogligService> logger,
    TimeSpan? requestGap = null) : IClubPairLogligService
{
    private readonly TimeSpan _gap = requestGap ?? SwimmerClubLogligCheckService.DefaultRequestGap;
    private DateTime? _lastRequestAt;

    public Task<ClubPairLogligReport?> GetAsync(int clubA, int clubB, CancellationToken ct = default) =>
        BuildAsync(clubA, clubB, maxRequests: 0, ct);

    public Task<ClubPairLogligReport?> CheckAsync(int clubA, int clubB, int maxRequests, CancellationToken ct = default) =>
        BuildAsync(clubA, clubB, Math.Max(0, maxRequests), ct);

    public async Task<Dictionary<(int A, int B), ClubPairLogligSummary>> SummarizeAsync(
        IReadOnlyList<(int A, int B, IReadOnlyCollection<int> SwimmerIds)> pairs, CancellationToken ct = default)
    {
        var result = new Dictionary<(int A, int B), ClubPairLogligSummary>();
        if (pairs.Count == 0) return result;

        var ids = pairs.SelectMany(p => p.SwimmerIds).Distinct().ToArray();
        var logligIds = await VerifiedLogligIdsAsync(ids, ct);
        var stored = await SwimmerLogligClubStore.LoadAsync(db, ids, ct);
        var matcher = await SwimmerClubLogligCheckService.LoadMatcherAsync(db, ct);

        foreach (var (a, b, swimmerIds) in pairs)
            result[(a, b)] = ClubPairLogligRule.Summarize(swimmerIds.Select(id =>
                ClubPairLogligRule.Classify(logligIds.GetValueOrDefault(id), stored.GetValueOrDefault(id), a, b, matcher)));
        return result;
    }

    private async Task<ClubPairLogligReport?> BuildAsync(int clubA, int clubB, int maxRequests, CancellationToken ct)
    {
        var pairClubs = await db.Clubs.AsNoTracking()
            .Where(c => c.Id == clubA || c.Id == clubB)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        if (clubA == clubB || !pairClubs.ContainsKey(clubA) || !pairClubs.ContainsKey(clubB)) return null;

        var ids = await SharedSwimmerIdsAsync(db, clubA, clubB, ct);
        var logligIds = await VerifiedLogligIdsAsync(ids, ct);
        var stored = await SwimmerLogligClubStore.LoadAsync(db, ids, ct);

        int requests = 0, failed = 0, remaining = 0;
        if (maxRequests > 0)
        {
            var now = DateTime.UtcNow;
            var stale = ids
                .Where(id => ClubPairLogligRule.NeedsFetch(now, logligIds.GetValueOrDefault(id), stored.GetValueOrDefault(id)))
                .ToList();
            remaining = Math.Max(0, stale.Count - maxRequests);
            foreach (var id in stale.Take(maxRequests))
            {
                ct.ThrowIfCancellationRequested();
                var lid = logligIds[id]!.Value;
                await ThrottleAsync(ct);
                var card = await loglig.GetPlayerCardAsync(lid, ct: ct);
                requests++;
                if (card is null) { failed++; continue; }

                var at = DateTime.UtcNow;
                var club = card.ClubName?.Trim();
                await SwimmerLogligClubStore.UpsertAsync(db, id, lid, club, at, ct);
                await db.SaveChangesAsync(ct);
                stored[id] = new StoredLogligClub(lid, club, at);
            }
            logger.LogInformation(
                "Пара клубов #{A}/#{B} ↔ loglig: общих {Shared}, открыто карточек {Requests}, не открылось {Failed}, осталось {Remaining}",
                clubA, clubB, ids.Count, requests, failed, remaining);
        }

        var matcher = await SwimmerClubLogligCheckService.LoadMatcherAsync(db, ct);
        var names = await SwimmerClubRefreshService.SwimmerNamesAsync(db, ids, ct);
        var cards = await db.Swimmers.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.BirthYear, s.ClubId, ClubName = s.Club != null ? s.Club.Name : null })
            .ToDictionaryAsync(s => s.Id, ct);

        var swimmers = ids.Select(id =>
            {
                var lid = logligIds.GetValueOrDefault(id);
                var answer = stored.GetValueOrDefault(id);
                var verdict = ClubPairLogligRule.Classify(lid, answer, clubA, clubB, matcher);
                var card = cards.GetValueOrDefault(id);
                // Ответ под другим loglig-id к пловцу больше не относится — не показываем.
                var own = answer is not null && answer.LogligId == lid ? answer : null;
                return new ClubPairLogligSwimmer(
                    id, names.GetValueOrDefault(id) ?? $"#{id}", card?.BirthYear is > 0 ? card.BirthYear : null,
                    card?.ClubId, card?.ClubName,
                    lid, own?.ClubName, own?.CheckedAt, verdict,
                    lid is int l ? loglig.BuildPublicProfileUrl(l) : null);
            })
            .OrderBy(s => Array.IndexOf(VerdictOrder, s.Verdict))
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToList();

        return new ClubPairLogligReport(
            clubA, pairClubs[clubA], clubB, pairClubs[clubB],
            ClubPairLogligRule.Summarize(swimmers.Select(s => s.Verdict)), swimmers,
            requests, failed, remaining);
    }

    private static readonly string[] VerdictOrder =
    [
        ClubPairLogligVerdicts.ClubA, ClubPairLogligVerdicts.ClubB, ClubPairLogligVerdicts.Both,
        ClubPairLogligVerdicts.Other, ClubPairLogligVerdicts.Archive, ClubPairLogligVerdicts.Unchecked,
        ClubPairLogligVerdicts.NoLogligId,
    ];

    /// <summary>Общие пловцы двух клубов — по карточке и по строкам результатов, как у эвристики «общие пловцы».</summary>
    internal static async Task<List<int>> SharedSwimmerIdsAsync(SwimmDbContext db, int clubA, int clubB, CancellationToken ct)
    {
        var fromCards = await db.Swimmers.AsNoTracking()
            .Where(s => s.ClubId == clubA || s.ClubId == clubB)
            .Select(s => new { ClubId = s.ClubId!.Value, SwimmerId = s.Id })
            .ToListAsync(ct);
        var fromResults = await db.Results.AsNoTracking()
            .Where(r => r.ClubId == clubA || r.ClubId == clubB)
            .Select(r => new { r.ClubId, r.SwimmerId })
            .Distinct()
            .ToListAsync(ct);
        var pairs = fromCards.Concat(fromResults).ToList();
        var a = pairs.Where(p => p.ClubId == clubA).Select(p => p.SwimmerId).ToHashSet();
        return pairs.Where(p => p.ClubId == clubB && a.Contains(p.SwimmerId))
            .Select(p => p.SwimmerId).Distinct().Order().ToList();
    }

    private async Task<Dictionary<int, int?>> VerifiedLogligIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        var arr = ids.ToArray();
        return await SwimmerClubRefreshService.VerifiedLogligIds(db.Swimmers.AsNoTracking())
            .Where(s => arr.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.LogligId, ct);
    }

    private async Task ThrottleAsync(CancellationToken ct)
    {
        if (_lastRequestAt is DateTime last)
        {
            var wait = _gap - (DateTime.UtcNow - last);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
        }
        _lastRequestAt = DateTime.UtcNow;
    }
}
