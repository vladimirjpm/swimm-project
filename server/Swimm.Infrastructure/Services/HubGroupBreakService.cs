using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Флаг «On break» (<see cref="IHubGroupBreakService"/>, Ш3.1). Пишет и читает через
/// <see cref="SwimmDbContext"/>: таблица — <c>Sys_</c>, роль swimm_ro её не видит. Без кэша.
/// </summary>
public class HubGroupBreakService : IHubGroupBreakService
{
    private readonly SwimmDbContext _db;

    public HubGroupBreakService(SwimmDbContext db) => _db = db;

    private static DateOnly Today(DateTime nowUtc) => DateOnly.FromDateTime(IsraelTime.ToLocal(nowUtc));

    public async Task<HubGroupBreaksDto> GetAsync(int hubGroupId, int viewerUserId, bool isManager, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var today = Today(now);

        var mine = await _db.HubGroupBreaks.AsNoTracking()
            .Where(b => b.HubGroupId == hubGroupId && b.UserId == viewerUserId
                        && b.EndedAt == null && (b.Until == null || b.Until >= today))
            .Select(b => new { b.UserId, b.SwimmerId, b.Since, b.Until, b.SetByUserId, b.EndedAt, b.EndedByRsvp, Name = b.User!.DisplayName })
            .FirstOrDefaultAsync();

        var dto = new HubGroupBreaksDto
        {
            Mine = mine == null ? null : ToDto(mine.UserId, mine.SwimmerId, mine.Name, mine.Since, mine.Until, mine.SetByUserId, null),
            CanManage = isManager,
        };
        if (!isManager) return dto;

        var since = now.AddDays(-HubGroupBreakRules.BackMarkerDays);
        var rows = await _db.HubGroupBreaks.AsNoTracking()
            .Where(b => b.HubGroupId == hubGroupId
                        && ((b.EndedAt == null && (b.Until == null || b.Until >= today))
                            || (b.EndedByRsvp && b.EndedAt >= since)))
            .Select(b => new
            {
                b.UserId, b.SwimmerId, b.Since, b.Until, b.SetByUserId, b.EndedAt, b.EndedByRsvp,
                UserName = b.User != null ? b.User.DisplayName : null,
                SwimmerName = b.Swimmer != null ? (b.Swimmer.LastName + " " + b.Swimmer.FirstName).Trim() : null,
                SwimmerNameEn = b.Swimmer != null ? (b.Swimmer.LastNameEn + " " + b.Swimmer.FirstNameEn).Trim() : null,
            })
            .ToListAsync();

        string NameOf(string? user, string? he, string? en) =>
            user ?? (!string.IsNullOrEmpty(he) ? he : en) ?? "";

        dto.Breaks = rows
            .Where(r => r.EndedAt == null)
            .Select(r => ToDto(r.UserId, r.SwimmerId, NameOf(r.UserName, r.SwimmerName, r.SwimmerNameEn),
                r.Since, r.Until, r.SetByUserId, null))
            .OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        dto.Returns = rows
            .Where(r => r.EndedAt != null)
            .Select(r => ToDto(r.UserId, r.SwimmerId, NameOf(r.UserName, r.SwimmerName, r.SwimmerNameEn),
                r.Since, r.Until, r.SetByUserId,
                HubGroupBreakRules.BackAfterDays(r.Since, r.EndedAt, r.EndedByRsvp, now)))
            .Where(b => b.BackAfterDays != null)
            .OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return dto;
    }

    private static HubGroupBreakDto ToDto(int? userId, int? swimmerId, string name, DateTime since,
        DateOnly? until, int? setBy, int? backAfterDays) => new()
    {
        UserId = userId,
        SwimmerId = swimmerId,
        Name = name,
        Since = since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Until = until?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        // Пловец без аккаунта сам себе перерыв не ставит — его строку всегда ставил тренер.
        SetByCoach = setBy != null && setBy != userId,
        BackAfterDays = backAfterDays,
    };

    public async Task<HubGroupBreakSaveResult> SetAsync(int hubGroupId, int actorUserId, bool isManager,
        HubGroupBreakInputDto input, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var today = Today(now);

        if (input.UserId != null && input.SwimmerId != null)
            return HubGroupBreakSaveResult.Fail(400, "Pick a member or a swimmer, not both.");

        DateOnly? until = null;
        if (!string.IsNullOrWhiteSpace(input.Until))
        {
            if (!DateOnly.TryParseExact(input.Until.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
                return HubGroupBreakSaveResult.Fail(400, "Date must be yyyy-MM-dd.");
            until = parsed;
        }

        IQueryable<HubGroupBreak> subject;
        int? userId = null, swimmerId = null;
        if (input.SwimmerId is int sid)
        {
            // Пловец состава — только тренер: у пловца без аккаунта некому сказать за себя.
            if (!isManager) return HubGroupBreakSaveResult.Fail(403, "Only the group coach can set a break for a swimmer.");
            var inRoster = await _db.HubGroupMembers
                .AnyAsync(m => m.HubGroupId == hubGroupId && m.SwimmerId == sid && !m.IsExcluded);
            if (!inRoster) return HubGroupBreakSaveResult.Fail(400, "This swimmer is not in the group.");
            swimmerId = sid;
            subject = _db.HubGroupBreaks.Where(b => b.HubGroupId == hubGroupId && b.SwimmerId == sid);
        }
        else
        {
            var target = input.UserId ?? actorUserId;
            var forOther = target != actorUserId;
            if (forOther && !isManager)
                return HubGroupBreakSaveResult.Fail(403, "Only the group coach can set a break for another member.");
            var isMember = await _db.HubGroupUserMembers.AnyAsync(m =>
                m.HubGroupId == hubGroupId && m.UserId == target && m.Status == HubGroupUserMemberStatus.Active);
            if (!isMember)
                return HubGroupBreakSaveResult.Fail(400, forOther
                    ? "This person is not an active member of the group."
                    : "Only group members can take a break. Join the group first.");
            userId = target;
            subject = _db.HubGroupBreaks.Where(b => b.HubGroupId == hubGroupId && b.UserId == target);
        }

        var open = await subject.FirstOrDefaultAsync(b => b.EndedAt == null);

        if (!input.OnBreak)
        {
            if (open != null) open.EndedAt = now;
        }
        else
        {
            var invalid = HubGroupBreakRules.ValidateUntil(until, today, bySelf: !isManager);
            if (invalid != null) return HubGroupBreakSaveResult.Fail(400, invalid);

            if (open == null)
            {
                open = new HubGroupBreak { HubGroupId = hubGroupId, UserId = userId, SwimmerId = swimmerId };
                _db.HubGroupBreaks.Add(open);
            }
            // Истёкшую открытую строку переиспользуем как новый перерыв: закрывать её и вставлять
            // рядом новую в одном SaveChanges — риск наступить на частичный UNIQUE.
            if (open.Id == 0 || !HubGroupBreakRules.IsActive(open.EndedAt, open.Until, today))
                open.Since = now;
            open.Until = until;
            open.EndedByRsvp = false;
            open.SetByUserId = actorUserId;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return HubGroupBreakSaveResult.Fail(409, "The break was changed elsewhere. Reload and try again.");
        }

        return HubGroupBreakSaveResult.Ok(await GetAsync(hubGroupId, actorUserId, isManager, now));
    }
}
