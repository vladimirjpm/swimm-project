using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Сверка кандидатов на смену клуба с карточкой loglig.com (Р98, §6 docs/plans/swimmer-club-change-plan.md).
/// Вердикт — чистое <see cref="SwimmerClubLogligRule"/>; здесь сеть, кэш и запись.
///
/// • Сеть последовательно, не чаще <see cref="DefaultRequestGap"/> между запросами (~1 в секунду):
///   loglig — сайт федерации, долбить его параллельно незачем; ~330 кандидатов ≈ 6 минут.
/// • Кэш ответа на сутки — в самом кандидате (<c>LogligClubName</c>, <c>CheckedAt</c>): суточный
///   прогон и повторный импорт в те же сутки в loglig не ходят, только пересчитывают вердикт.
/// • Подтверждённая смена (<c>confirmed</c>) → <c>Swimmers.ClubId</c> + журнал (источник
///   <c>loglig</c>), кандидат удаляется. Перед записью карточка перечитывается: закреплённую
///   (<c>ClubLocked</c>) или поправленную за это время не трогаем.
/// • Остальные вердикты кандидата не трогают — он ждёт со своей причиной (дашборд).
///
/// Запись карточки — обычным SaveChanges по отслеживаемой сущности: перехватчик кэша сам сбросит
/// <c>row:Swimmers:{id}</c> и оба клуба, как и при прежней прямой смене (Ф1).
/// </summary>
public sealed class SwimmerClubLogligCheckService(
    SwimmDbContext db,
    ILogligClient loglig,
    ILogger<SwimmerClubLogligCheckService> logger,
    TimeSpan? requestGap = null) : ISwimmerClubLogligCheckService
{
    /// <summary>Пауза между запросами к loglig (решение §6.2: ~1 запрос в секунду).</summary>
    public static readonly TimeSpan DefaultRequestGap = TimeSpan.FromSeconds(1);

    private readonly TimeSpan _gap = requestGap ?? DefaultRequestGap;
    private DateTime? _lastRequestAt;

    private sealed record Item(int SwimmerId, int? FromClubId, int ToClubId, DateOnly LastSwimDate, SwimmerClubCandidate? Stored);

    public async Task<SwimmerClubCheckReport> CheckPendingAsync(SwimmerClubCheckScope scope, CancellationToken ct = default)
    {
        var query = db.SwimmerClubCandidates.AsQueryable();
        if (scope == SwimmerClubCheckScope.Unchecked) query = query.Where(c => c.Verdict == null);
        var candidates = await query.OrderByDescending(c => c.LastSwimDate).ThenBy(c => c.SwimmerId).ToListAsync(ct);
        var items = candidates.Select(c => new Item(c.SwimmerId, c.FromClubId, c.ToClubId, c.LastSwimDate, c)).ToList();
        return await RunAsync(items, apply: true, ignoreCache: false, progress: null, ct);
    }

    public async Task<SwimmerClubVerdictDto?> CheckSwimmerAsync(int swimmerId, CancellationToken ct = default)
    {
        var c = await db.SwimmerClubCandidates.FirstOrDefaultAsync(x => x.SwimmerId == swimmerId, ct);
        if (c is null) return null;
        var report = await RunAsync([new Item(c.SwimmerId, c.FromClubId, c.ToClubId, c.LastSwimDate, c)],
            apply: true, ignoreCache: true, progress: null, ct);
        return report.Verdicts.Single();
    }

    public async Task<SwimmerClubCheckReport> PreviewAsync(
        IReadOnlyList<SwimmerClubChangePlan> plans, Action<int, int>? progress = null, CancellationToken ct = default)
    {
        var ids = plans.Select(p => p.SwimmerId).ToArray();
        // Уже записанные кандидаты — только как кэш ответа loglig; AsNoTracking: dry-run ничего не пишет.
        var stored = await db.SwimmerClubCandidates.AsNoTracking()
            .Where(c => ids.Contains(c.SwimmerId)).ToDictionaryAsync(c => c.SwimmerId, ct);
        var items = plans
            .Select(p => new Item(p.SwimmerId, p.FromClubId, p.ToClubId, p.LastSwimDate, stored.GetValueOrDefault(p.SwimmerId)))
            .ToList();
        return await RunAsync(items, apply: false, ignoreCache: false, progress, ct);
    }

    private async Task<SwimmerClubCheckReport> RunAsync(
        List<Item> items, bool apply, bool ignoreCache, Action<int, int>? progress, CancellationToken ct)
    {
        if (items.Count == 0)
            return new SwimmerClubCheckReport(0, 0, 0, 0, new Dictionary<string, int>(), []);

        var matcher = await LoadMatcherAsync(ct);
        var ids = items.Select(i => i.SwimmerId).ToArray();
        var logligIds = await SwimmerClubRefreshService.VerifiedLogligIds(db.Swimmers.AsNoTracking())
            .Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.LogligId, ct);

        var verdicts = new List<SwimmerClubVerdictDto>(items.Count);
        int requests = 0, fromCache = 0, applied = 0, done = 0;
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            var logligId = logligIds.GetValueOrDefault(item.SwimmerId);
            LogligClubAnswer? answer = null;
            var cached = false;

            // Хвост склейки (карточка на надгробии нового клуба) — правило решит без loglig.
            var sameClub = item.FromClubId is int from && matcher.Live(from) is int f && f == matcher.Live(item.ToClubId);
            if (logligId is int lid && !sameClub)
            {
                var s = item.Stored;
                if (!ignoreCache && s is not null
                    && SwimmerClubLogligRule.CacheValid(DateTime.UtcNow, s.CheckedAt, s.LogligId, lid, s.LogligClubName))
                {
                    answer = new LogligClubAnswer(true, s.LogligClubName);
                    cached = true;
                    fromCache++;
                }
                else
                {
                    await ThrottleAsync(ct);
                    var card = await loglig.GetPlayerCardAsync(lid, ct: ct);
                    requests++;
                    answer = card is null ? new LogligClubAnswer(false, null) : new LogligClubAnswer(true, card.ClubName?.Trim());
                }
            }

            var verdict = SwimmerClubLogligRule.Decide(logligId, answer, item.FromClubId, item.ToClubId, matcher);
            var changed = false;
            if (apply && item.Stored is { } stored)
            {
                stored.Verdict = verdict;
                stored.LogligId = logligId;
                if (answer is { Fetched: true } && !cached)
                {
                    stored.LogligClubName = Clip(answer.ClubName);
                    stored.CheckedAt = DateTime.UtcNow;
                }
                stored.UpdatedAt = DateTime.UtcNow;
                if (SwimmerClubLogligRule.Applies(verdict))
                    changed = await ApplyAsync(stored, ct);
                await db.SaveChangesAsync(ct);
                if (changed) applied++;
            }

            verdicts.Add(new SwimmerClubVerdictDto(item.SwimmerId, verdict, logligId,
                answer?.ClubName ?? item.Stored?.LogligClubName, cached, changed));
            progress?.Invoke(++done, items.Count);
        }

        var byVerdict = verdicts.GroupBy(v => v.Verdict).ToDictionary(g => g.Key, g => g.Count());
        if (requests > 0 || applied > 0)
            logger.LogInformation(
                "Клуб пловца ↔ loglig: сверено {Checked}, запросов {Requests}, из кэша {Cached}, применено {Applied}",
                verdicts.Count, requests, fromCache, applied);
        return new SwimmerClubCheckReport(verdicts.Count, requests, fromCache, applied, byVerdict, verdicts);
    }

    /// <summary>
    /// Записать подтверждённую смену. Карточку перечитываем: если её закрепили или клуб в ней уже
    /// не тот, с которого заводили кандидата, — ничего не меняем, кандидат снимается (следующий
    /// пересчёт заведёт его заново, если правило всё ещё даёт другой клуб).
    /// </summary>
    private async Task<bool> ApplyAsync(SwimmerClubCandidate candidate, CancellationToken ct)
    {
        var swimmer = await db.Swimmers.FirstOrDefaultAsync(s => s.Id == candidate.SwimmerId, ct);
        db.SwimmerClubCandidates.Remove(candidate);
        if (swimmer is null || swimmer.ClubLocked || swimmer.ClubId != candidate.FromClubId) return false;

        swimmer.ClubId = candidate.ToClubId;
        db.SwimmerClubChanges.Add(new SwimmerClubChange
        {
            SwimmerId = swimmer.Id, FromClubId = candidate.FromClubId, ToClubId = candidate.ToClubId,
            Source = SwimmerClubChangeSources.Loglig, LastSwimDate = candidate.LastSwimDate,
        });
        return true;
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

    private async Task<ClubNameMatcher> LoadMatcherAsync(CancellationToken ct)
    {
        var clubs = await db.Clubs.AsNoTracking()
            .Select(c => new ClubAliasClub(c.Id, c.Name, c.NameEn, c.MergedIntoId, c.IsPseudo))
            .ToListAsync(ct);
        var names = await db.ClubNames.AsNoTracking()
            .Select(n => new ClubAliasName(n.Id, n.ClubId, n.Name, n.NameEn, n.Kind))
            .ToListAsync(ct);
        return new ClubNameMatcher(clubs, names);
    }

    private static string? Clip(string? s) => s is { Length: > 200 } ? s[..200] : s;
}
