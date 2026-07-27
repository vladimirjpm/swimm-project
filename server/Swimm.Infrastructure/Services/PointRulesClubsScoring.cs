using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Чистый расчёт клубных очков за место по правилам <see cref="PointRuleClubs"/> —
/// выделен из <c>HubGroupPublicRepository</c> ради юнит-тестируемости (сезонный зачёт 8.5).
/// Выбор правила живёт в <see cref="CompetitionRuleResolver"/> (привязка по Id важнее
/// подбора по дате); здесь — только начисление по уже выбранному правилу.
/// </summary>
public static class PointRulesClubsScoring
{
    /// <summary>
    /// Очки за один заплыв по КОНКРЕТНОМУ правилу. Незачтённое время
    /// (<paramref name="timeFail"/>), отсутствие правила или некорректное место → 0.
    /// Эстафетный множитель здесь НЕ применяется — он в <see cref="RelayPointsFor"/>.
    /// </summary>
    public static int PointsFor(PointRuleClubs? rule, int? position, bool timeFail)
    {
        if (rule is null || timeFail || position is null || position < 1) return 0;

        if (rule.MaxScoringPlace is int max && position > max) return rule.DefaultPoints;
        var entry = rule.Entries.FirstOrDefault(e => e.Place == position.Value);
        return entry?.Points ?? rule.DefaultPoints;
    }

    /// <summary>Очки за заплыв с учётом эстафетного множителя правила
    /// (регламент бугрим п.17 «ניקוד כפול» — раньше был хардкод *2).</summary>
    public static int RelayPointsFor(PointRuleClubs? rule, int? position, bool timeFail, bool isRelay)
    {
        var points = PointsFor(rule, position, timeFail);
        return isRelay && rule is not null ? points * rule.RelayMultiplier : points;
    }

    /// <summary>
    /// Совместимая перегрузка: сама подбирает правило по дате/области. Используется там,
    /// где привязки соревнования нет под рукой (и в тестах подбора).
    /// </summary>
    public static int PointsFor(
        IReadOnlyCollection<PointRuleClubs> rules, int? position, bool timeFail, bool isMasters, DateOnly date)
    {
        if (timeFail || position is null || position < 1) return 0;
        return PointsFor(SelectRule(rules, isMasters, date), position, timeFail);
    }

    /// <summary>Действующее на дату правило нужной области (masters/non-masters), fallback на "all".</summary>
    public static PointRuleClubs? SelectRule(IReadOnlyCollection<PointRuleClubs> rules, bool isMasters, DateOnly date)
        => CompetitionRuleResolver.SelectByDate(rules, isMasters, date);
}
