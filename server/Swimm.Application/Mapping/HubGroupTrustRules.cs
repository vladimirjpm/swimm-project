using System.Linq.Expressions;
using Swimm.Domain.Entities;

namespace Swimm.Application.Mapping;

/// <summary>
/// Доверие группы (Р56, docs/data-integrity.md И15): кто вправе выводить свои <c>public</c>-медиа
/// на ЧУЖИЕ страницы — протокол и карточку пловца. Доверенная = флаг админа сайта
/// (<see cref="HubGroup.IsTrusted"/>) ИЛИ официальная группа клуба (<see cref="HubGroup.IsOfficial"/>:
/// её уже одобрил админ сайта заявкой). Одно место правила — и для запросов, и для DTO.
/// </summary>
public static class HubGroupTrustRules
{
    public static bool IsTrusted(bool isTrustedFlag, bool isOfficial) => isTrustedFlag || isOfficial;

    public static bool IsTrusted(HubGroup g) => IsTrusted(g.IsTrusted, g.IsOfficial);

    /// <summary>То же для LINQ-запросов (переводится в SQL).</summary>
    public static readonly Expression<Func<HubGroup, bool>> IsTrustedExpr = g => g.IsTrusted || g.IsOfficial;
}
