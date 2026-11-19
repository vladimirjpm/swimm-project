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
/// • Проверка (<see cref="RunAsync"/>) — из фоновой задачи «Проверить в loglig» (одна пара или все):
///   карточки общих пловцов с несвежим ответом, по одной, ~1 запрос в секунду (как сверка
///   кандидатов Р98), пловец, общий для нескольких пар, — один раз. Каждый удачный ответ
///   сохраняется сразу — прерванная проверка не теряет сделанного; после каждой пары её сводка
///   уходит в статус прогона, и страница перерисовывает строку.
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
        BuildAsync(clubA, clubB, ct);

    public async Task RunAsync(ClubPairLogligRunStatus status, IReadOnlyList<(int A, int B)> pairs, CancellationToken ct = default)
    {
        // Сначала весь объём: общие пловцы каждой пары и кого из них открывать — ради прогресса «12 / 87».
        var shared = new List<(int A, int B, List<int> Ids)>(pairs.Count);
        foreach (var (a, b) in pairs)
            if (a != b) shared.Add((a, b, await SharedSwimmerIdsAsync(db, a, b, ct)));
        var allIds = shared.SelectMany(p => p.Ids).Distinct().ToArray();
        var logligIds = await VerifiedLogligIdsAsync(allIds, ct);
        var stored = await SwimmerLogligClubStore.LoadAsync(db, allIds, ct);
        var now = DateTime.UtcNow;
        var stale = allIds
            .Where(id => ClubPairLogligRule.NeedsFetch(now, logligIds.GetValueOrDefault(id), stored.GetValueOrDefault(id)))
            .ToHashSet();
        status.PairsTotal = shared.Count;
        status.SwimmersTotal = stale.Count;
        var matcher = await SwimmerClubLogligCheckService.LoadMatcherAsync(db, ct);

        foreach (var (a, b, ids) in shared)
        {
            status.CurrentClubAId = a;
            status.CurrentClubBId = b;
            int requests = 0, failed = 0;
            foreach (var id in ids.Where(stale.Contains).ToList())
            {
                ct.ThrowIfCancellationRequested();
                stale.Remove(id); // общий для нескольких пар — открываем один раз
                var lid = logligIds[id]!.Value;
                await ThrottleAsync(ct);
                var card = await loglig.GetPlayerCardAsync(lid, ct: ct);
                requests++;
                if (card is null)
                {
                    failed++;
                    status.Failed++;
                }
                else
                {
                    var at = DateTime.UtcNow;
                    var club = card.ClubName?.Trim();
                    await SwimmerLogligClubStore.UpsertAsync(db, id, lid, club, at, ct);
                    await db.SaveChangesAsync(ct);
                    stored[id] = new StoredLogligClub(lid, club, at);
                }
                status.SwimmersDone++;
            }

            var summary = ClubPairLogligRule.Summarize(ids.Select(id =>
                ClubPairLogligRule.Classify(logligIds.GetValueOrDefault(id), stored.GetValueOrDefault(id), a, b, matcher)));
            status.Pairs = [.. status.Pairs, new ClubPairLogligRunPair(a, b, summary, requests, failed)];
            status.PairsDone++;
            if (requests > 0)
                logger.LogInformation(
                    "Пара клубов #{A}/#{B} ↔ loglig: общих {Shared}, открыто карточек {Requests}, не открылось {Failed}",
                    a, b, ids.Count, requests, failed);
        }
    }

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

    private async Task<ClubPairLogligReport?> BuildAsync(int clubA, int clubB, CancellationToken ct)
    {
        var pairClubs = await db.Clubs.AsNoTracking()
            .Where(c => c.Id == clubA || c.Id == clubB)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        if (clubA == clubB || !pairClubs.ContainsKey(clubA) || !pairClubs.ContainsKey(clubB)) return null;

        var ids = await SharedSwimmerIdsAsync(db, clubA, clubB, ct);
        var logligIds = await VerifiedLogligIdsAsync(ids, ct);
        var stored = await SwimmerLogligClubStore.LoadAsync(db, ids, ct);


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
            ClubPairLogligRule.Summarize(swimmers.Select(s => s.Verdict)), swimmers);
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
