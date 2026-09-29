using System.Linq.Expressions;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Кому видна ОДОБРЕННАЯ публикация личного медиа — одно правило на все витрины: иконки видео в
/// протоколе, галерея страницы пловца, страница результатов группы, лайк и жалоба «Report».
/// Модель одной фразой (Р65, docs/data-integrity.md): **группа только следит; публичное — только
/// Trusted**.
/// <list type="bullet">
///   <item>public ВСЕМ, включая гостя, — только от доверенного источника: клубная публикация (её
///     одобряет админ сайта) или доверенная группа (<see cref="HubGroupTrustRules"/>: флаг «Trusted»
///     или официальная). Public недоверенной группы (подать его нельзя, но флаг могли снять после
///     публикации) — то же, что members;</item>
///   <item>members — активному участнику-аккаунту группы публикации, её владельцу, админу группы
///     и админу сайта. Заявка на вступление (pending) не в счёт.</item>
/// </list>
/// Медиа, спрятанное жалобами (Р62, <c>UserMedia.ModerationState</c>), не видит никто — см. NotHidden.
/// Статус «одобрена» и скоуп (соревнование, пловец, медиа) накладывает вызывающий.
/// </summary>
internal static class MediaPublicationAudience
{
    public static Expression<Func<UserMediaPublication, bool>> CanSee(SwimmDbContext db, int? userId, bool isSiteAdmin)
    {
        if (isSiteAdmin && userId != null) return NotHidden(db);

        // У клубной публикации HubGroupId пуст: подзапрос по группе ничего не находит, а
        // «HubGroupId == null» и есть «цель — клуб» (CHECK держит ровно одну цель).
        var trusted = HubGroupTrustRules.IsTrustedExpr;
        Expression<Func<UserMediaPublication, bool>> publicForAll = p => p.Level == UserMediaPublicationLevel.Public
            && (p.HubGroupId == null || db.HubGroups.Where(trusted).Any(g => g.Id == p.HubGroupId));

        if (userId is not int uid) return And(NotHidden(db), publicForAll);

        return And(NotHidden(db), Or(publicForAll, InGroupAudience(db, uid)));
    }

    /// <summary>
    /// Медиа не спрятано жалобами (Р62: <c>UserMedia.ModerationState</c> пуст). Спрятанное не видит
    /// через публикации НИКТО, и админ сайта тоже — иначе он решил бы, что прятание не сработало;
    /// разбирает он его на /Admin/MediaReports. Владелец своё видит отдельным путём («своё»).
    /// </summary>
    private static Expression<Func<UserMediaPublication, bool>> NotHidden(SwimmDbContext db) =>
        p => db.UserMedia.Any(m => m.Id == p.UserMediaId && m.ModerationState == null);

    /// <summary>Участник-аккаунт (active), владелец или админ группы публикации — любой уровень.</summary>
    private static Expression<Func<UserMediaPublication, bool>> InGroupAudience(SwimmDbContext db, int uid) =>
        p => db.HubGroupUserMembers.Any(um => um.HubGroupId == p.HubGroupId && um.UserId == uid
                && um.Status == HubGroupUserMemberStatus.Active)
            || db.HubGroupAdmins.Any(a => a.HubGroupId == p.HubGroupId && a.UserId == uid)
            || db.HubGroups.Any(g => g.Id == p.HubGroupId && g.OwnerUserId == uid);

    private static Expression<Func<T, bool>> Or<T>(Expression<Func<T, bool>> a, Expression<Func<T, bool>> b) =>
        Combine(a, b, Expression.OrElse);

    private static Expression<Func<T, bool>> And<T>(Expression<Func<T, bool>> a, Expression<Func<T, bool>> b) =>
        Combine(a, b, Expression.AndAlso);

    private static Expression<Func<T, bool>> Combine<T>(
        Expression<Func<T, bool>> a, Expression<Func<T, bool>> b, Func<Expression, Expression, BinaryExpression> op)
    {
        var p = a.Parameters[0];
        var bBody = new ReplaceParameter(b.Parameters[0], p).Visit(b.Body);
        return Expression.Lambda<Func<T, bool>>(op(a.Body, bBody), p);
    }

    private sealed class ReplaceParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}
