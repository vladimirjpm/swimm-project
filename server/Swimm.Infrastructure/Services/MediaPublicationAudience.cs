using System.Linq.Expressions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Кому видна ОДОБРЕННАЯ публикация личного медиа — одно правило на все витрины: иконки видео в
/// протоколе, галерея страницы пловца, лайки. Аудитория members — та же, что у ленты members на
/// странице группы (HubGroupsController.GetPublishedMedia: CanEdit ∪ активный участник-аккаунт):
/// <list type="bullet">
///   <item>public — всем, включая гостя;</item>
///   <item>members — активному участнику-аккаунту группы публикации, её владельцу, админу группы
///     и админу сайта. Заявка на вступление (pending) не в счёт.</item>
/// </list>
/// Раньше в протоколе и на странице пловца members видели только участники-аккаунты, а лайк
/// пускал и pending: админ группы, не вступивший в неё, видел разбор на странице группы, но не в
/// протоколе (10.09.2026). Три копии правила разъехались — поэтому оно здесь одно.
/// Статус «одобрена» и скоуп (соревнование, пловец, медиа) накладывает вызывающий.
/// </summary>
internal static class MediaPublicationAudience
{
    public static Expression<Func<UserMediaPublication, bool>> CanSee(SwimmDbContext db, int? userId, bool isSiteAdmin)
    {
        if (userId is not int uid) return p => p.Level == UserMediaPublicationLevel.Public;
        if (isSiteAdmin) return p => true;

        // У клубной публикации HubGroupId пуст, и подзапросы ниже ничего не находят — остаётся
        // только public, а другого уровня у клуба и не бывает.
        return p => p.Level == UserMediaPublicationLevel.Public
            || db.HubGroupUserMembers.Any(um => um.HubGroupId == p.HubGroupId && um.UserId == uid
                && um.Status == HubGroupUserMemberStatus.Active)
            || db.HubGroupAdmins.Any(a => a.HubGroupId == p.HubGroupId && a.UserId == uid)
            || db.HubGroups.Any(g => g.Id == p.HubGroupId && g.OwnerUserId == uid);
    }
}
