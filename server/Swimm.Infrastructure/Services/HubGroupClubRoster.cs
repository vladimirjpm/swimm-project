using Microsoft.EntityFrameworkCore;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Кто сейчас пловец клуба — запросом к БД (окно — <see cref="HubGroupClubRules.ActivitySince"/>).
/// Одно место на пересборку состава (<see cref="HubGroupClubSubscriptionService"/>) и на удаление
/// ручного участника (<see cref="HubGroupCrudCore.RemoveMemberAsync"/>): разъедутся — пересборка
/// будет возвращать того, кого удаление считало «не из клуба».
/// </summary>
internal static class HubGroupClubRoster
{
    /// <summary>
    /// Пловцы, выступавшие за клуб в окне активности. Только индивидуальные заплывы: у эстафеты
    /// в SwimmerId бывает «пловец-тень» со склеенными именами ног (docs/relays.md), а сами ноги
    /// эстафеты без личного старта за клуб — редкость, которой состав не стоит.
    /// <c>Results.ClubId</c> — клуб на момент заплыва: перешедший в другой клуб уйдёт из
    /// подписки на старый, когда выпадет из окна.
    /// </summary>
    public static IQueryable<int> SwimmerIds(SwimmDbContext db, int clubId, DateTime since) =>
        db.Results
            .Where(r => r.ClubId == clubId && r.RelayId == null && r.CompetitionDate >= since)
            .Select(r => r.SwimmerId)
            .Distinct();

    /// <summary>Пловец входит в клуб, на который подписана группа (в окне активности).</summary>
    public static Task<bool> IsInSubscribedClubAsync(SwimmDbContext db, int hubGroupId, int swimmerId, DateTime since)
    {
        var clubIds = db.HubGroupClubSubscriptions
            .Where(s => s.HubGroupId == hubGroupId)
            .Select(s => s.ClubId);

        return db.Results.AnyAsync(r => r.SwimmerId == swimmerId && r.RelayId == null
            && r.CompetitionDate >= since && clubIds.Contains(r.ClubId));
    }
}
