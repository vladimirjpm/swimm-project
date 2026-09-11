using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Официальная группа — главная (П4 плана docs/plans/hubgroup-club-subscription-plan.md): группа,
/// подписанная на клуб, у которого есть официальная группа (и это не она сама), в каталог
/// <c>/groups</c> не попадает, но по ссылке работает. Правило — одно выражение
/// <see cref="ListedInCatalog"/>: его зовут и каталог, и строки «My groups» / админки.
///
/// «Не в каталоге» ВЫЧИСЛЯЕТСЯ, колонки нет (§2): сняли официальный статус или удалили
/// официальную группу — копии сами вернулись в каталог. Переименование « · community» при этом
/// разовое и назад не откатывается.
///
/// Группы без подписки (состав руками) правило не трогает, даже если зовутся как клуб, — их имя
/// ловят валидация и переименование при одобрении.
/// </summary>
internal static class HubGroupCatalog
{
    public static Expression<Func<HubGroup, bool>> ListedInCatalog(SwimmDbContext db) =>
        g => !g.ClubSubscriptions.Any(s =>
            db.HubGroups.Any(o => o.IsOfficial && o.ClubId == s.ClubId && o.Id != g.Id));

    /// <summary>
    /// Клуб подписки и «не в каталоге из-за официальной X» — для строк «My groups» и админки.
    /// Решение «спрятана ли» берётся из того же <see cref="ListedInCatalog"/>, здесь только
    /// подписи к нему.
    /// </summary>
    public static async Task FillCatalogStatusAsync(SwimmDbContext db, IReadOnlyList<HubGroupAdminRowDto> rows)
    {
        if (rows.Count == 0) return;
        var ids = rows.Select(r => r.Id).ToList();

        var subscriptions = await db.HubGroupClubSubscriptions.AsNoTracking()
            .Where(s => ids.Contains(s.HubGroupId))
            .Select(s => new
            {
                s.HubGroupId,
                s.ClubId,
                ClubName = s.Club!.Name.Length > 0 ? s.Club.Name : s.Club.NameEn
            })
            .ToListAsync();
        if (subscriptions.Count == 0) return;

        var subscribedIds = subscriptions.Select(s => s.HubGroupId).ToList();
        var listed = await db.HubGroups.AsNoTracking()
            .Where(g => subscribedIds.Contains(g.Id))
            .Where(ListedInCatalog(db))
            .Select(g => g.Id)
            .ToListAsync();

        var clubIds = subscriptions.Select(s => (int?)s.ClubId).Distinct().ToList();
        var officials = await db.HubGroups.AsNoTracking()
            .Where(o => o.IsOfficial && clubIds.Contains(o.ClubId))
            .Select(o => new { o.Id, o.ClubId, o.Slug, o.Name })
            .ToListAsync();

        foreach (var row in rows)
        {
            var subscription = subscriptions.FirstOrDefault(s => s.HubGroupId == row.Id);
            if (subscription == null) continue;

            row.FollowedClubName = subscription.ClubName;
            if (listed.Contains(row.Id)) continue;

            var official = officials.FirstOrDefault(o => o.ClubId == subscription.ClubId && o.Id != row.Id);
            if (official == null) continue;

            row.HiddenByOfficialGroup = true;
            row.OfficialGroupSlug = official.Slug;
            row.CatalogNotice = HubGroupClubRules.NotInCatalogNotice(subscription.ClubName, official.Name);
        }
    }
}
