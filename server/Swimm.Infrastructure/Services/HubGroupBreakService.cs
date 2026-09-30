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
/// Статус человека в группе (<see cref="IHubGroupBreakService"/>): «On break» (Ш3.1) и
/// «Doesn't train with us» (Р74) — одна строка на человека, вид в <c>Kind</c>. Пишет и читает через
/// <see cref="SwimmDbContext"/>: таблица — <c>Sys_</c>, роль swimm_ro её не видит. Без кэша.
/// </summary>
public class HubGroupBreakService : IHubGroupBreakService
{
    private readonly SwimmDbContext _db;

    public HubGroupBreakService(SwimmDbContext db) => _db = db;

    /// <summary>Статус «в строю» — строки нет (открытая закрывается).</summary>
    private const string StatusActive = "active";

    private static DateOnly Today(DateTime nowUtc) => DateOnly.FromDateTime(IsraelTime.ToLocal(nowUtc));

    public async Task<HubGroupBreaksDto> GetAsync(int hubGroupId, int viewerUserId, bool isManager, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var today = Today(now);

        var mine = await _db.HubGroupBreaks.AsNoTracking()
            .Where(b => b.HubGroupId == hubGroupId && b.UserId == viewerUserId
                        && b.EndedAt == null && (b.Until == null || b.Until >= today))
            .Select(b => new { b.UserId, b.SwimmerId, b.Since, b.Until, b.SetByUserId, b.EndedAt, b.EndedByRsvp, b.Kind, Name = b.User!.DisplayName })
            .FirstOrDefaultAsync();

        var dto = new HubGroupBreaksDto
        {
            Mine = mine == null ? null : ToDto(mine.UserId, mine.SwimmerId, mine.Name, mine.Since, mine.Until, mine.SetByUserId, null, mine.Kind),
            CanManage = isManager,
        };

        // «Не тренируется с группой» — всем, кто видит группу изнутри (Р74): пометка в Following.
        var notTraining = await _db.HubGroupBreaks.AsNoTracking()
            .Where(b => b.HubGroupId == hubGroupId && b.EndedAt == null && b.Kind == HubGroupBreakKind.NotTraining)
            .Select(b => new
            {
                b.UserId, b.SwimmerId, b.Since, b.SetByUserId,
                UserName = b.User != null ? b.User.DisplayName : null,
                SwimmerName = b.Swimmer != null ? (b.Swimmer.LastName + " " + b.Swimmer.FirstName).Trim() : null,
                SwimmerNameEn = b.Swimmer != null ? (b.Swimmer.LastNameEn + " " + b.Swimmer.FirstNameEn).Trim() : null,
            })
            .ToListAsync();
        dto.NotTraining = notTraining
            .Select(r => ToDto(r.UserId, r.SwimmerId, NameOf(r.UserName, r.SwimmerName, r.SwimmerNameEn),
                r.Since, null, r.SetByUserId, null, HubGroupBreakKind.NotTraining))
            .OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (!isManager) return dto;

        var since = now.AddDays(-HubGroupBreakRules.BackMarkerDays);
        var rows = await _db.HubGroupBreaks.AsNoTracking()
            .Where(b => b.HubGroupId == hubGroupId && b.Kind == HubGroupBreakKind.Break
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

    private static string NameOf(string? user, string? he, string? en) =>
        user ?? (!string.IsNullOrEmpty(he) ? he : en) ?? "";

    private static HubGroupBreakDto ToDto(int? userId, int? swimmerId, string name, DateTime since,
        DateOnly? until, int? setBy, int? backAfterDays, string kind = HubGroupBreakKind.Break) => new()
    {
        Kind = kind,
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

        // Статус (Р74): active / break / not_training; без него — старый флаг on_break.
        var status = input.Status?.Trim().ToLowerInvariant() ?? (input.OnBreak ? HubGroupBreakKind.Break : StatusActive);
        if (status != StatusActive && !HubGroupBreakKind.All.Contains(status))
            return HubGroupBreakSaveResult.Fail(400, "Status must be active, break or not_training.");
        // «Не тренируется с группой» ставит только тренер (решение Влада 30.09.2026) — и бессрочно.
        if (status == HubGroupBreakKind.NotTraining && !isManager)
            return HubGroupBreakSaveResult.Fail(403, "Only the group coach can mark someone as not training with the group.");

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

        if (status == StatusActive)
        {
            if (open != null) open.EndedAt = now;
        }
        else
        {
            if (status == HubGroupBreakKind.NotTraining)
            {
                until = null;
            }
            else
            {
                var invalid = HubGroupBreakRules.ValidateUntil(until, today, bySelf: !isManager);
                if (invalid != null) return HubGroupBreakSaveResult.Fail(400, invalid);
            }

            if (open == null)
            {
                open = new HubGroupBreak { HubGroupId = hubGroupId, UserId = userId, SwimmerId = swimmerId };
                _db.HubGroupBreaks.Add(open);
            }
            // Истёкшую открытую строку (и строку другого вида — статус сменился) переиспользуем как
            // новую: закрывать её и вставлять рядом новую в одном SaveChanges — риск наступить на
            // частичный UNIQUE. Новый статус — новый отсчёт «с какого дня».
            if (open.Id == 0 || open.Kind != status || !HubGroupBreakRules.IsActive(open.EndedAt, open.Until, today))
                open.Since = now;
            open.Kind = status;
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
