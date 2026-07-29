using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Выбор правила начисления очков под соревнование — один на оба вида правил
/// (клубные и пловца), чтобы привязка не разъехалась между клубным зачётом и High Point.
///
/// Порядок (docs/points-rules-per-competition-plan.md §4):
///   1) у соревнования проставлен FK → берём это правило, дата и scope не проверяются;
///   2) иначе — подбор по дате и scope, исключая ManualOnly-правила;
///   3) иначе — null (0 очков / legacy-расчёт).
///
/// Шаг 2 после бэкфилла практически недостижим (у всех соревнований FK проставлен,
/// при импорте выбор обязателен) — он оставлен страховкой, чтобы забытая привязка не
/// обнуляла клубный зачёт молча.
/// </summary>
public static class CompetitionRuleResolver
{
    public static TRule? Resolve<TRule>(
        IReadOnlyCollection<TRule> rules, int? pinnedRuleId, bool isMasters, DateOnly date)
        where TRule : class, IPointRule
    {
        if (pinnedRuleId is int id)
        {
            var pinned = rules.FirstOrDefault(r => r.Id == id);
            if (pinned is not null)
                return pinned;
            // FK указывает на отсутствующее правило (правило удалили в обход RESTRICT) —
            // не молчим, а падаем на подбор: пустой зачёт хуже приблизительного.
        }

        return SelectByDate(rules, isMasters, date);
    }

    /// <summary>Действующее на дату правило нужной области (masters/non-masters), fallback на "all".
    /// ManualOnly-правила исключены: они существуют только для явной привязки.</summary>
    public static TRule? SelectByDate<TRule>(
        IReadOnlyCollection<TRule> rules, bool isMasters, DateOnly date)
        where TRule : class, IPointRule
    {
        var scope = isMasters ? "masters" : "non-masters";
        return rules
            .Where(x => !x.ManualOnly)
            .Where(x => (x.Scope == scope || x.Scope == "all") && x.EffectiveFrom <= date)
            .OrderByDescending(x => x.EffectiveFrom)
            .ThenByDescending(x => x.Scope == "all" ? 0 : 1)
            .FirstOrDefault();
    }
}
