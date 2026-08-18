using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Идентичность соревнования по штампу сайта (docs/data-integrity.md, фаза Д2):
/// единственное место, где compID (<c>OrgCompId</c>) превращается в дни соревнования в БД.
///
/// Правило 1 из data-integrity.md: предикат живёт в ОДНОМ месте. Импорт и ретро-аудит
/// обязаны резолвить одинаково — иначе аудит будет отчитываться про одни дни, а переимпорт
/// писать в другие.
///
/// Штамп лежит в двух местах, и это не дублирование: <c>Competition.OrgCompId</c> —
/// альтернативный ключ с UNIQUE-индексом (на него ссылается FK из CompetitionResultUrls),
/// поэтому у многодневки его получает только один день; <c>CompetitionEvent.OrgCompId</c>
/// покрывает событие целиком.
/// </summary>
internal static class CompetitionIdentity
{
    /// <summary>
    /// Дни, относящиеся к этому compID: всё событие, если соревнование в него входит,
    /// иначе одиночное соревнование. Пустой список — связи нет (импортировали не через
    /// Discovery либо штамп ещё не проставлен).
    /// </summary>
    public static async Task<List<Competition>> ResolveDaysAsync(
        SwimmDbContext db, int orgCompId, CancellationToken ct = default)
    {
        var stamped = await db.Competitions.AsNoTracking()
            .FirstOrDefaultAsync(c => c.OrgCompId == orgCompId, ct);

        var eventId = stamped?.EventId
            ?? (await db.CompetitionEvents.AsNoTracking()
                    .FirstOrDefaultAsync(e => e.OrgCompId == orgCompId, ct))?.Id;

        if (eventId is int evId)
            return await db.Competitions.AsNoTracking().Where(c => c.EventId == evId).ToListAsync(ct);

        return stamped != null ? [stamped] : [];
    }
}
