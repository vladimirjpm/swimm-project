using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// «Когда я последний раз открывал» виды таба Trainings (<see cref="HubGroupUserSeen"/>) — опора
/// меток внимания (docs/plans/trainings-tab-redesign-plan.md, Т1б): лента → «новые видео», Lanes →
/// «тебя перенесли». Одна строка на (группа, аккаунт), заводится при первом открытии.
/// </summary>
internal static class HubGroupSeenStore
{
    /// <summary>
    /// Медиа «новое», если добавлено не зрителем после его <c>TrainingsSeenAt</c>, но не старше
    /// этого окна: кто не заходил месяцами (или ни разу), не получает сотню «NEW» на старых плитках.
    /// </summary>
    public static readonly TimeSpan NewMediaWindow = TimeSpan.FromDays(14);

    public static Task<HubGroupUserSeen?> GetAsync(SwimmDbContext db, int hubGroupId, int userId) =>
        db.HubGroupUserSeen.AsNoTracking().FirstOrDefaultAsync(s => s.HubGroupId == hubGroupId && s.UserId == userId);

    /// <summary>С какого момента медиа других — «новое» для зрителя.</summary>
    public static DateTime NewMediaSince(HubGroupUserSeen? seen, DateTime nowUtc)
    {
        var floor = nowUtc - NewMediaWindow;
        return seen?.TrainingsSeenAt is DateTime at && at > floor ? at : floor;
    }

    /// <summary>
    /// Поставить отметку «открыл». Два открытия разом (две вкладки) первой вставкой спорят за
    /// ключ — проигравший перечитывает строку и обновляет её.
    /// </summary>
    public static async Task TouchAsync(SwimmDbContext db, int hubGroupId, int userId, Action<HubGroupUserSeen> mark)
    {
        for (var attempt = 0; ; attempt++)
        {
            var row = await db.HubGroupUserSeen.FirstOrDefaultAsync(s => s.HubGroupId == hubGroupId && s.UserId == userId);
            if (row == null)
            {
                row = new HubGroupUserSeen { HubGroupId = hubGroupId, UserId = userId };
                db.HubGroupUserSeen.Add(row);
            }
            mark(row);
            try
            {
                await db.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                db.Entry(row).State = EntityState.Detached;
            }
        }
    }
}
