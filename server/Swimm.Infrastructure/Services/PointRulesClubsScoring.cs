using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Чистый расчёт клубных очков за место по правилам <see cref="ClubPointsRule"/> —
/// выделен из <c>HubGroupPublicRepository</c> ради юнит-тестируемости (сезонный зачёт 8.5).
/// Логика подбора правила зеркалит клиентский <c>club-points-helper.ts</c>: при равенстве
/// дат вступления scope-специфичное правило (masters/non-masters) важнее общего "all".
/// </summary>
public static class ClubPointsScoring
{
    /// <summary>
    /// Очки за один заплыв. Незачтённое время (<paramref name="timeFail"/>) или отсутствие/
    /// некорректное место → 0. Правило выбирается по дате соревнования и области.
    /// </summary>
    public static int PointsFor(
        IReadOnlyCollection<ClubPointsRule> rules, int? position, bool timeFail, bool isMasters, DateOnly date)
    {
        if (timeFail || position is null || position < 1) return 0;

        var rule = SelectRule(rules, isMasters, date);
        if (rule is null) return 0;

        if (rule.MaxScoringPlace is int max && position > max) return rule.DefaultPoints;
        var entry = rule.Entries.FirstOrDefault(e => e.Place == position.Value);
        return entry?.Points ?? rule.DefaultPoints;
    }

    /// <summary>Действующее на дату правило нужной области (masters/non-masters), fallback на "all".</summary>
    public static ClubPointsRule? SelectRule(IReadOnlyCollection<ClubPointsRule> rules, bool isMasters, DateOnly date)
    {
        var scope = isMasters ? "masters" : "non-masters";
        return rules
            .Where(x => (x.Scope == scope || x.Scope == "all") && x.EffectiveFrom <= date)
            .OrderByDescending(x => x.EffectiveFrom)
            .ThenByDescending(x => x.Scope == "all" ? 0 : 1)
            .FirstOrDefault();
    }
}
