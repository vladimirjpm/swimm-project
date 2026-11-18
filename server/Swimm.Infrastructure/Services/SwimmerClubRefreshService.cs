using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Текущий клуб в карточке пловца (Р95, Ф1 docs/plans/swimmer-club-change-plan.md). Правило —
/// <see cref="SwimmerCurrentClubRule"/>; здесь только чтение строк, запись кандидатов, ручная
/// правка карточки и журнал.
///
/// С Р98 (§6 плана, 18.11.2026) пересчёт карточку сам НЕ меняет: правило даёт кандидата
/// (<c>Sys_SwimmerClubCandidates</c>), а применяет его сверка с loglig —
/// <see cref="SwimmerClubLogligCheckService"/>. Сеть — не здесь: импорт зовёт этот сервис после
/// коммита, а дальше будит фоновую службу сигналом.
///
/// Строки читаются уже сгруппированными (пловец × клуб × день → число строк, последняя дата):
/// догон по всем пловцам не тянет в память каждую строку Results. Только личные строки
/// (<c>RelayId == null</c>) — почему, сказано у правила.
///
/// Запись — обычным SaveChanges по отслеживаемым карточкам: перехватчик кэша сам сбросит
/// <c>row:Swimmers:{id}</c> и оба клуба (старый и новый FK), а с ними страницу и профиль пловца.
/// </summary>
public sealed class SwimmerClubRefreshService(SwimmDbContext db, ISwimmerClubCheckSignal? signal = null)
    : ISwimmerClubRefreshService
{
    private sealed record Card(int Id, int? ClubId, bool ClubLocked, string FirstName, string LastName, string FirstNameEn, string LastNameEn);
    internal sealed record ClubInfo(int Id, string Name, string NameEn, bool IsPseudo, int? MergedIntoId);

    public async Task<SwimmerClubRefreshReport> RefreshAsync(
        IReadOnlyCollection<int>? swimmerIds, string source, bool apply, CancellationToken ct = default)
    {
        if (swimmerIds is { Count: 0 }) return new(0, 0, 0, [], apply);
        var ids = swimmerIds?.Distinct().ToArray();

        var cardsQuery = db.Swimmers.AsNoTracking();
        if (ids is not null) cardsQuery = cardsQuery.Where(s => ids.Contains(s.Id));
        var cards = await cardsQuery
            .Select(s => new Card(s.Id, s.ClubId, s.ClubLocked, s.FirstName, s.LastName, s.FirstNameEn, s.LastNameEn))
            .ToListAsync(ct);

        var rowsBySwimmer = await LoadRowsAsync(ids, ct);
        var clubs = await LoadClubsAsync(ct);
        var facts = clubs.ToDictionary(c => c.Key, c => new ClubFacts(c.Value.IsPseudo, c.Value.MergedIntoId));

        var changes = new List<SwimmerClubChangePlan>();
        int locked = 0, ties = 0, checkedCount = 0;
        foreach (var card in cards)
        {
            if (!rowsBySwimmer.TryGetValue(card.Id, out var rows)) continue; // без личных строк — нечего решать
            checkedCount++;
            if (card.ClubLocked) { locked++; continue; }

            var periods = SwimmerCurrentClubRule.Periods(rows, facts);
            if (periods.Count == 0) continue;                       // только псевдоклубы
            if (SwimmerCurrentClubRule.CurrentClub(rows, facts) is not int wanted) { ties++; continue; }
            if (wanted == card.ClubId) continue;

            changes.Add(new SwimmerClubChangePlan(
                card.Id, SwimmerName(card.FirstName, card.LastName, card.FirstNameEn, card.LastNameEn),
                card.ClubId, card.ClubId is int from ? ClubName(clubs, from) : null,
                wanted, ClubName(clubs, wanted),
                DateOnly.FromDateTime(periods.Max(p => p.Last)),
                SwimmerCurrentClubRule.HasOverlap(periods)));
        }

        // Ждущие кандидаты в охвате прогона, которых правило больше не даёт: следующий старт снова
        // за клуб карточки, пловца закрепили, ничья или строк не осталось — снимаем.
        var wantedIds = changes.Select(c => c.SwimmerId).ToHashSet();
        var scope = db.SwimmerClubCandidates.AsQueryable();
        if (ids is not null) scope = scope.Where(c => ids.Contains(c.SwimmerId));
        var existing = await scope.ToListAsync(ct);
        var withdrawn = existing.Where(c => !wantedIds.Contains(c.SwimmerId)).ToList();

        if (apply)
        {
            var fresh = UpsertCandidates(existing, changes, source);
            db.SwimmerClubCandidates.RemoveRange(withdrawn);
            await db.SaveChangesAsync(ct);
            if (fresh > 0 && source == SwimmerClubChangeSources.Import) signal?.Notify();
        }
        return new SwimmerClubRefreshReport(checkedCount, locked, ties, changes, apply, withdrawn.Count);
    }

    /// <summary>
    /// Завести новых кандидатов и обновить ждущих. Сменился клуб «было» или «стало» — вердикт
    /// сбрасывается (сверять заново), но ответ loglig остаётся кэшем: клуб на карточке от нашего
    /// кандидата не зависит. Возвращает, сколько кандидатов ждут сверки впервые или заново.
    /// </summary>
    private int UpsertCandidates(List<SwimmerClubCandidate> existing, List<SwimmerClubChangePlan> changes, string source)
    {
        var byId = existing.ToDictionary(c => c.SwimmerId);
        var now = DateTime.UtcNow;
        var fresh = 0;
        foreach (var plan in changes)
        {
            if (!byId.TryGetValue(plan.SwimmerId, out var c))
            {
                db.SwimmerClubCandidates.Add(new SwimmerClubCandidate
                {
                    SwimmerId = plan.SwimmerId, FromClubId = plan.FromClubId, ToClubId = plan.ToClubId,
                    LastSwimDate = plan.LastSwimDate, Overlap = plan.Overlap, Source = source,
                    CreatedAt = now, UpdatedAt = now,
                });
                fresh++;
                continue;
            }

            var touched = false;
            if (c.FromClubId != plan.FromClubId || c.ToClubId != plan.ToClubId)
            {
                c.FromClubId = plan.FromClubId;
                c.ToClubId = plan.ToClubId;
                c.Verdict = null;
                c.Source = source;
                fresh++;
                touched = true;
            }
            if (c.LastSwimDate != plan.LastSwimDate || c.Overlap != plan.Overlap)
            {
                c.LastSwimDate = plan.LastSwimDate;
                c.Overlap = plan.Overlap;
                touched = true;
            }
            if (touched) c.UpdatedAt = now;
        }
        return fresh;
    }

    public async Task<SwimmerClubAdminDto?> GetForAdminAsync(int swimmerId, CancellationToken ct = default)
    {
        var card = await db.Swimmers.AsNoTracking().Where(s => s.Id == swimmerId)
            .Select(s => new Card(s.Id, s.ClubId, s.ClubLocked, s.FirstName, s.LastName, s.FirstNameEn, s.LastNameEn))
            .FirstOrDefaultAsync(ct);
        if (card is null) return null;

        var clubs = await LoadClubsAsync(ct);
        var facts = clubs.ToDictionary(c => c.Key, c => new ClubFacts(c.Value.IsPseudo, c.Value.MergedIntoId));
        var rows = (await LoadRowsAsync([swimmerId], ct)).GetValueOrDefault(swimmerId) ?? [];
        var rule = SwimmerCurrentClubRule.CurrentClub(rows, facts);

        var periods = SwimmerCurrentClubRule.Periods(rows, facts)
            .OrderByDescending(p => p.Last)
            .Select(p => new SwimmerClubPeriodDto(p.ClubId, ClubName(clubs, p.ClubId),
                DateOnly.FromDateTime(p.First), DateOnly.FromDateTime(p.Last), p.Rows))
            .ToList();

        var history = await QueryChanges(db.SwimmerClubChanges.Where(c => c.SwimmerId == swimmerId), 20, clubs, ct);
        var name = SwimmerName(card.FirstName, card.LastName, card.FirstNameEn, card.LastNameEn);
        var candidate = await db.SwimmerClubCandidates.AsNoTracking()
            .FirstOrDefaultAsync(c => c.SwimmerId == swimmerId, ct);
        var logligId = await VerifiedLogligIds(db.Swimmers.AsNoTracking().Where(s => s.Id == swimmerId))
            .Select(s => s.LogligId).FirstOrDefaultAsync(ct);

        return new SwimmerClubAdminDto(
            card.Id, name,
            card.ClubId, card.ClubId is int c ? ClubName(clubs, c) : null, card.ClubLocked,
            rule, rule is int r ? ClubName(clubs, r) : null,
            periods, history,
            candidate is null ? null : ToDto(candidate, name, clubs),
            logligId);
    }

    public async Task<(SwimmerClubSetResult? Result, SwimmerClubSetError Error)> SetByAdminAsync(
        int swimmerId, int? clubId, bool locked, CancellationToken ct = default)
    {
        var swimmer = await db.Swimmers.FirstOrDefaultAsync(s => s.Id == swimmerId, ct);
        if (swimmer is null) return (null, SwimmerClubSetError.SwimmerNotFound);

        int? target = null;
        if (clubId is int requested)
        {
            var clubs = await LoadClubsAsync(ct);
            if (!clubs.ContainsKey(requested)) return (null, SwimmerClubSetError.ClubNotFound);
            var facts = clubs.ToDictionary(c => c.Key, c => new ClubFacts(c.Value.IsPseudo, c.Value.MergedIntoId));
            target = SwimmerCurrentClubRule.Resolve(requested, facts);
            if (target is null) return (null, SwimmerClubSetError.PseudoClub);
        }

        var result = new SwimmerClubSetResult(swimmerId, swimmer.ClubId, target, swimmer.ClubLocked, locked);
        // Человек решил сам — ждущий кандидат снимается. Без закрепления следующий импорт может
        // завести его снова, если правило по-прежнему даёт другой клуб.
        if (await db.SwimmerClubCandidates.FirstOrDefaultAsync(c => c.SwimmerId == swimmerId, ct) is { } pending)
            db.SwimmerClubCandidates.Remove(pending);
        if (swimmer.ClubId != target)
        {
            db.SwimmerClubChanges.Add(new SwimmerClubChange
            {
                SwimmerId = swimmerId, FromClubId = swimmer.ClubId, ToClubId = target,
                Source = SwimmerClubChangeSources.Admin,
            });
            swimmer.ClubId = target;
        }
        swimmer.ClubLocked = locked;
        await db.SaveChangesAsync(ct);
        return (result, SwimmerClubSetError.None);
    }

    public async Task<IReadOnlyList<SwimmerClubChangeDto>> RecentChangesAsync(int days, int take, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-days);
        var clubs = await LoadClubsAsync(ct);
        return await QueryChanges(db.SwimmerClubChanges.Where(c => c.ChangedAt >= since), take, clubs, ct);
    }

    public async Task<SwimmerClubPendingSummary> PendingAsync(int takePerVerdict, CancellationToken ct = default)
    {
        var all = await db.SwimmerClubCandidates.AsNoTracking()
            .OrderByDescending(c => c.LastSwimDate).ThenBy(c => c.SwimmerId)
            .ToListAsync(ct);
        var groups = all.GroupBy(c => c.Verdict ?? PendingUnchecked).ToList();
        var byVerdict = groups.ToDictionary(g => g.Key, g => g.Count());
        var shown = groups.SelectMany(g => g.Take(takePerVerdict)).ToList();
        if (shown.Count == 0) return new SwimmerClubPendingSummary(all.Count, byVerdict, []);

        var clubs = await LoadClubsAsync(db, ct);
        var names = await SwimmerNamesAsync(db, shown.Select(c => c.SwimmerId), ct);
        return new SwimmerClubPendingSummary(all.Count, byVerdict,
            shown.Select(c => ToDto(c, names.GetValueOrDefault(c.SwimmerId) ?? $"#{c.SwimmerId}", clubs)).ToList());
    }

    /// <summary>Ключ «ещё не сверяли» в счёте ждущих по вердиктам (у кандидата <c>Verdict</c> = null).</summary>
    public const string PendingUnchecked = "unchecked";

    /// <summary>Число смен за <paramref name="days"/> дней — для карточки дашборда.</summary>
    internal static Task<int> CountRecentAsync(SwimmDbContext db, int days, CancellationToken ct) =>
        db.SwimmerClubChanges.AsNoTracking().CountAsync(c => c.ChangedAt >= DateTime.UtcNow.AddDays(-days), ct);

    /// <summary>
    /// Подтверждённый loglig-id (статус Verified): предложенный пользователем, но не проверенный
    /// (Suggested), и отклонённый с карточкой пловца не сверяем — карточка может быть чужой.
    /// </summary>
    internal static IQueryable<Swimmer> VerifiedLogligIds(IQueryable<Swimmer> swimmers) =>
        swimmers.Where(s => s.LogligId != null && s.LogligIdStatus == "Verified");

    internal static SwimmerClubCandidateDto ToDto(SwimmerClubCandidate c, string swimmerName, Dictionary<int, ClubInfo> clubs) =>
        new(c.SwimmerId, swimmerName,
            c.FromClubId, c.FromClubId is int f ? ClubName(clubs, f) : null,
            c.ToClubId, ClubName(clubs, c.ToClubId),
            c.LastSwimDate, c.Overlap, c.Source,
            c.Verdict, c.LogligId, c.LogligClubName, c.CheckedAt, c.CreatedAt);

    internal static async Task<Dictionary<int, string>> SwimmerNamesAsync(SwimmDbContext db, IEnumerable<int> ids, CancellationToken ct)
    {
        var arr = ids.Distinct().ToArray();
        return await db.Swimmers.AsNoTracking().Where(s => arr.Contains(s.Id))
            .Select(s => new { s.Id, s.FirstName, s.LastName, s.FirstNameEn, s.LastNameEn })
            .ToDictionaryAsync(s => s.Id, s => SwimmerName(s.FirstName, s.LastName, s.FirstNameEn, s.LastNameEn), ct);
    }

    private async Task<Dictionary<int, List<SwimmerClubRow>>> LoadRowsAsync(int[]? ids, CancellationToken ct)
    {
        var results = db.Results.AsNoTracking().Where(r => r.RelayId == null);
        if (ids is not null) results = results.Where(r => ids.Contains(r.SwimmerId));

        var grouped = await results
            .GroupBy(r => new { r.SwimmerId, r.ClubId, r.CompetitionId, r.Competition.EventId })
            .Select(g => new { g.Key.SwimmerId, g.Key.ClubId, g.Key.CompetitionId, g.Key.EventId,
                               Date = g.Max(r => r.CompetitionDate), Count = g.Count() })
            .ToListAsync(ct);

        return grouped
            .GroupBy(g => g.SwimmerId)
            .ToDictionary(g => g.Key, g => g
                .Select(x => new SwimmerClubRow(x.ClubId, x.CompetitionId, x.EventId, x.Date, x.Count))
                .ToList());
    }

    private Task<Dictionary<int, ClubInfo>> LoadClubsAsync(CancellationToken ct) => LoadClubsAsync(db, ct);

    internal static async Task<Dictionary<int, ClubInfo>> LoadClubsAsync(SwimmDbContext db, CancellationToken ct) =>
        await db.Clubs.AsNoTracking()
            .Select(c => new ClubInfo(c.Id, c.Name, c.NameEn, c.IsPseudo, c.MergedIntoId))
            .ToDictionaryAsync(c => c.Id, ct);

    private async Task<IReadOnlyList<SwimmerClubChangeDto>> QueryChanges(
        IQueryable<SwimmerClubChange> query, int take, Dictionary<int, ClubInfo> clubs, CancellationToken ct)
    {
        var rows = await query.AsNoTracking()
            .OrderByDescending(c => c.ChangedAt).ThenByDescending(c => c.Id)
            .Take(take)
            .ToListAsync(ct);
        var names = await SwimmerNamesAsync(db, rows.Select(r => r.SwimmerId), ct);

        return rows.Select(r => new SwimmerClubChangeDto(
                r.Id, r.SwimmerId, names.GetValueOrDefault(r.SwimmerId) ?? $"#{r.SwimmerId}",
                r.FromClubId, r.FromClubId is int f ? ClubName(clubs, f) : null,
                r.ToClubId, r.ToClubId is int t ? ClubName(clubs, t) : null,
                r.Source, r.LastSwimDate, r.ChangedAt))
            .ToList();
    }

    /// <summary>Имя на иврите по умолчанию, английское — фоллбек (правило витрины 28.08.2026).</summary>
    internal static string SwimmerName(string first, string last, string firstEn, string lastEn)
    {
        var he = $"{first} {last}".Trim();
        return he.Length > 0 ? he : $"{firstEn} {lastEn}".Trim();
    }

    internal static string ClubName(Dictionary<int, ClubInfo> clubs, int id) =>
        clubs.TryGetValue(id, out var c)
            ? (string.IsNullOrWhiteSpace(c.Name) ? c.NameEn : c.Name)
            : $"#{id}";
}
