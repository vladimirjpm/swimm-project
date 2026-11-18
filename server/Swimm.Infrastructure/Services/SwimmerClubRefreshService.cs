using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Текущий клуб в карточке пловца (Р95, Ф1 docs/plans/swimmer-club-change-plan.md). Правило —
/// <see cref="SwimmerCurrentClubRule"/>; здесь только чтение строк, запись карточек и журнала.
///
/// Строки читаются уже сгруппированными (пловец × клуб × день → число строк, последняя дата):
/// догон по всем пловцам не тянет в память каждую строку Results. Только личные строки
/// (<c>RelayId == null</c>) — почему, сказано у правила.
///
/// Запись — обычным SaveChanges по отслеживаемым карточкам: перехватчик кэша сам сбросит
/// <c>row:Swimmers:{id}</c> и оба клуба (старый и новый FK), а с ними страницу и профиль пловца.
/// </summary>
public sealed class SwimmerClubRefreshService(SwimmDbContext db) : ISwimmerClubRefreshService
{
    private sealed record Card(int Id, int? ClubId, bool ClubLocked, string FirstName, string LastName, string FirstNameEn, string LastNameEn);
    private sealed record ClubInfo(int Id, string Name, string NameEn, bool IsPseudo, int? MergedIntoId);

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

        if (apply && changes.Count > 0) await ApplyAsync(changes, source, ct);
        return new SwimmerClubRefreshReport(checkedCount, locked, ties, changes, apply);
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

        return new SwimmerClubAdminDto(
            card.Id, SwimmerName(card.FirstName, card.LastName, card.FirstNameEn, card.LastNameEn),
            card.ClubId, card.ClubId is int c ? ClubName(clubs, c) : null, card.ClubLocked,
            rule, rule is int r ? ClubName(clubs, r) : null,
            periods, history);
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

    /// <summary>Число смен за <paramref name="days"/> дней — для карточки дашборда.</summary>
    internal static Task<int> CountRecentAsync(SwimmDbContext db, int days, CancellationToken ct) =>
        db.SwimmerClubChanges.AsNoTracking().CountAsync(c => c.ChangedAt >= DateTime.UtcNow.AddDays(-days), ct);

    private async Task ApplyAsync(List<SwimmerClubChangePlan> changes, string source, CancellationToken ct)
    {
        var byId = changes.ToDictionary(c => c.SwimmerId);
        var ids = byId.Keys.ToArray();
        var swimmers = await db.Swimmers.Where(s => ids.Contains(s.Id)).ToListAsync(ct);
        foreach (var swimmer in swimmers)
        {
            var plan = byId[swimmer.Id];
            // Между чтением и записью карточку могли закрепить или поправить — тогда не трогаем.
            if (swimmer.ClubLocked || swimmer.ClubId != plan.FromClubId) continue;
            swimmer.ClubId = plan.ToClubId;
            db.SwimmerClubChanges.Add(new SwimmerClubChange
            {
                SwimmerId = swimmer.Id, FromClubId = plan.FromClubId, ToClubId = plan.ToClubId,
                Source = source, LastSwimDate = plan.LastSwimDate,
            });
        }
        await db.SaveChangesAsync(ct);
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

    private async Task<Dictionary<int, ClubInfo>> LoadClubsAsync(CancellationToken ct) =>
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
        var swimmerIds = rows.Select(r => r.SwimmerId).Distinct().ToArray();
        var names = await db.Swimmers.AsNoTracking().Where(s => swimmerIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FirstName, s.LastName, s.FirstNameEn, s.LastNameEn })
            .ToDictionaryAsync(s => s.Id, s => SwimmerName(s.FirstName, s.LastName, s.FirstNameEn, s.LastNameEn), ct);

        return rows.Select(r => new SwimmerClubChangeDto(
                r.Id, r.SwimmerId, names.GetValueOrDefault(r.SwimmerId) ?? $"#{r.SwimmerId}",
                r.FromClubId, r.FromClubId is int f ? ClubName(clubs, f) : null,
                r.ToClubId, r.ToClubId is int t ? ClubName(clubs, t) : null,
                r.Source, r.LastSwimDate, r.ChangedAt))
            .ToList();
    }

    /// <summary>Имя на иврите по умолчанию, английское — фоллбек (правило витрины 28.08.2026).</summary>
    private static string SwimmerName(string first, string last, string firstEn, string lastEn)
    {
        var he = $"{first} {last}".Trim();
        return he.Length > 0 ? he : $"{firstEn} {lastEn}".Trim();
    }

    private static string ClubName(Dictionary<int, ClubInfo> clubs, int id) =>
        clubs.TryGetValue(id, out var c)
            ? (string.IsNullOrWhiteSpace(c.Name) ? c.NameEn : c.Name)
            : $"#{id}";
}
