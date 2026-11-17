using Microsoft.EntityFrameworkCore;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Добавка к ответам занятия для УПРАВЛЯЮЩЕГО (docs/plans/trainings-coach-flow-plan.md, CF4):
/// у каждого человека переклички — кем он стоит (пловец состава) и его уровень; полоса «Needs you» —
/// идущие без уровня, дорожки сверх лимита, заявки на вступление и аккаунты без связи с пловцом.
/// Всё считается из ответов, уровней, плана и участников; своего списка «проблем» в данных нет.
/// </summary>
internal static class TrainingCoachNeeds
{
    public static async Task FillAsync(SwimmDbContext db, int hubGroupId, LanePlanKey planKey, TrainingRsvpDto dto,
        IReadOnlyDictionary<int, int?> resolved)
    {
        var people = dto.People ?? [];

        var roster = (await db.HubGroupMembers.AsNoTracking()
                .Where(m => m.HubGroupId == hubGroupId && !m.IsExcluded)
                .Select(m => new
                {
                    m.SwimmerId,
                    Name = (m.Swimmer!.LastName + " " + m.Swimmer.FirstName).Trim(),
                    NameEn = (m.Swimmer.LastNameEn + " " + m.Swimmer.FirstNameEn).Trim(),
                })
                .ToListAsync())
            .ToDictionary(r => r.SwimmerId, r => (Name: string.IsNullOrEmpty(r.Name) ? r.NameEn : r.Name, r.NameEn));
        var swimmerLevels = await db.HubGroupSwimmerLevels.AsNoTracking()
            .Where(l => l.HubGroupId == hubGroupId)
            .ToDictionaryAsync(l => l.SwimmerId, l => new { l.LevelId, l.SubLevel });
        var accountLevels = await db.HubGroupAccountLevels.AsNoTracking()
            .Where(l => l.HubGroupId == hubGroupId)
            .ToDictionaryAsync(l => l.UserId, l => l.LevelId);

        foreach (var p in people)
        {
            var linked = p.SwimmerId ?? (p.UserId is int uid ? resolved.GetValueOrDefault(uid) : null);
            p.LinkedSwimmerId = linked;
            if (linked is int sid && p.UserId != null && roster.TryGetValue(sid, out var r)) p.LinkedSwimmerName = r.Name;
            if (linked is int s && swimmerLevels.TryGetValue(s, out var level))
            {
                p.LevelId = level.LevelId;
                p.SubLevel = level.SubLevel;
            }
            else if (p.UserId is int u && accountLevels.TryGetValue(u, out var accountLevel)) p.LevelId = accountLevel;
        }

        // План ЭТОГО занятия (CF5): у второго занятия того же дня план свой.
        var (date, start) = (planKey.Date, planKey.Start);
        var plan = await db.LanePlans.AsNoTracking()
            .Include(x => x.Swimmers)
            .FirstOrDefaultAsync(x => x.HubGroupId == hubGroupId && x.Date == date && x.Start == start);
        var onLane = plan?.Swimmers.Where(s => s.LaneNo is int no && no <= plan.LaneCount).Select(s => s.SwimmerId).ToHashSet()
                     ?? [];

        var needs = new TrainingNeedsDto();

        // Идущий пловец без уровня: на дорожку его поставить некуда. Поставлен на дорожку руками —
        // уже решено (так же считает проверка публикации, решение 10).
        needs.NoLevel = people
            .Where(p => p.Answer == TrainingRsvpAnswer.Yes && p.LinkedSwimmerId is int sid
                        && !swimmerLevels.ContainsKey(sid) && !onLane.Contains(sid))
            .Select(p => new TrainingNeedPersonDto { UserId = p.UserId, SwimmerId = p.LinkedSwimmerId, Name = p.Name })
            .ToList();

        if (plan?.MaxPerLane is int max)
            needs.OverLanes = plan.Swimmers
                .Where(s => s.LaneNo is int no && no <= plan.LaneCount)
                .GroupBy(s => s.LaneNo!.Value)
                .Where(g => g.Count() > max)
                .OrderBy(g => g.Key)
                .Select(g => new TrainingNeedLaneDto { LaneNo = g.Key, Count = g.Count(), Max = max })
                .ToList();

        // Заявки и аккаунты без связи — шторка «Who is this?» (§5.7 плана).
        var accounts = await db.HubGroupUserMembers.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId)
            .Select(m => new { m.UserId, m.Status, Name = m.User!.DisplayName, m.SwimmerId, m.NotSwimmer, m.JoinedAt })
            .ToListAsync();
        var pending = accounts.Where(a => a.Status == HubGroupUserMemberStatus.Pending).OrderBy(a => a.JoinedAt).ToList();
        var unlinked = accounts
            .Where(a => a.Status == HubGroupUserMemberStatus.Active && !a.NotSwimmer
                        && resolved.TryGetValue(a.UserId, out var sid) && sid == null)
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (pending.Count == 0 && unlinked.Count == 0)
        {
            dto.Needs = needs;
            return;
        }

        // Подсказка: кем человек стоял бы по метке / привязке сайта / «Me» (только состав), иначе
        // совпадение по имени. Уже занятых активными аккаунтами не предлагаем.
        var pendingResolved = await HubGroupPersonResolver.ResolveAsync(db, hubGroupId, pending.Select(a => a.UserId).ToList());
        var claimed = resolved.Values.Where(v => v != null).Select(v => v!.Value).ToHashSet();
        var free = roster.Where(r => !claimed.Contains(r.Key)).ToList();
        int? Suggest(int userId, string displayName)
        {
            if (pendingResolved.GetValueOrDefault(userId) is int sid && !claimed.Contains(sid)) return sid;
            var tokens = Tokens(displayName);
            if (tokens.Count == 0) return null;
            var matches = free
                .Where(r => Tokens(r.Value.Name).SetEquals(tokens) || Tokens(r.Value.NameEn).SetEquals(tokens))
                .Select(r => r.Key)
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        needs.Requests = pending
            .Select(a => new TrainingNeedPersonDto { UserId = a.UserId, Name = a.Name, SuggestedSwimmerId = Suggest(a.UserId, a.Name) })
            .ToList();
        needs.Unlinked = unlinked
            .Select(a => new TrainingNeedPersonDto { UserId = a.UserId, Name = a.Name, SuggestedSwimmerId = Suggest(a.UserId, a.Name) })
            .ToList();
        dto.Needs = needs;
    }

    /// <summary>Слова имени без регистра: «Cohen Dana» и «Dana Cohen» совпадают.</summary>
    private static HashSet<string> Tokens(string? name) =>
        (name ?? "").Split([' ', '-', '\'', '"', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .ToHashSet();
}
