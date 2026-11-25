using Microsoft.EntityFrameworkCore;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Общее место последнего ответа loglig про клуб пловца (<c>Sys_SwimmerLogligClubs</c>, Р112):
/// читают и пишут сверка кандидатов на смену клуба (Р98), проверка пары клубов на /Admin/Clubs и
/// запасные пути узнавания пловца на импорте (<see cref="ImportClubEvidence"/>, Р110).
/// С Р125 тут же лежит «кто на карточке» (имя, год, пол) — по нему пол пловца берут с loglig
/// (<see cref="LogligCardGenderService"/>): один визит на карточку служит и клубу, и полу.
/// Пишется только удачный ответ; сохранение — у вызывающего (SaveChanges).
/// </summary>
internal static class SwimmerLogligClubStore
{
    public static async Task<Dictionary<int, StoredLogligClub>> LoadAsync(
        SwimmDbContext db, IReadOnlyCollection<int> swimmerIds, CancellationToken ct)
    {
        if (swimmerIds.Count == 0) return [];
        var ids = swimmerIds.ToArray();
        return await db.SwimmerLogligClubs.AsNoTracking()
            .Where(x => ids.Contains(x.SwimmerId))
            .ToDictionaryAsync(x => x.SwimmerId,
                x => new StoredLogligClub(x.LogligId, x.ClubName, x.CheckedAt, x.CardName, x.CardBirthYear,
                    x.Gender, x.ResultsMatched), ct);
    }

    /// <param name="card">
    /// Сама карточка — из неё в строку уходят имя, год и пол (Р125). null — «кто на карточке»
    /// стирается: ответ нового визита не должен донашивать имя и пол старого.
    /// </param>
    /// <param name="resultsMatched">
    /// Нашёлся ли на карточке заплыв пловца (Р126). null — вызывающий не сверял: прежнее значение
    /// остаётся, если loglig-id тот же (сверка клуба не должна стирать то, что узнал шаг пола).
    /// </param>
    public static async Task UpsertAsync(
        SwimmDbContext db, int swimmerId, int logligId, string? clubName, DateTime checkedAt, CancellationToken ct,
        LogligPlayerCard? card = null, bool? resultsMatched = null)
    {
        var row = await db.SwimmerLogligClubs.FindAsync([swimmerId], ct);
        if (row is null)
        {
            row = new SwimmerLogligClub { SwimmerId = swimmerId };
            db.SwimmerLogligClubs.Add(row);
        }
        row.ResultsMatched = resultsMatched ?? (row.LogligId == logligId && card is not null ? row.ResultsMatched : null);
        row.LogligId = logligId;
        row.ClubName = clubName is { Length: > 200 } ? clubName[..200] : clubName;
        row.CheckedAt = checkedAt;
        row.CardName = card?.FullName is { Length: > 200 } name ? name[..200] : card?.FullName;
        row.CardBirthYear = card?.BirthYear;
        row.Gender = LogligCardGenderRule.ToSwimmerGender(card?.Gender);
    }
}
