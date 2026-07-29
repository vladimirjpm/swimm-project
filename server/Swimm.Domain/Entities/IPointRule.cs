namespace Swimm.Domain.Entities;

/// <summary>
/// Общее у правил начисления очков (клубных и пловца) — ровно то, что нужно для выбора
/// правила под соревнование. Позволяет держать один резолвер на оба вида
/// (см. CompetitionRuleResolver), чтобы логика привязки не разъехалась между
/// клубным зачётом и High Point.
/// </summary>
public interface IPointRule
{
    int Id { get; }

    /// <summary>"all" | "masters" | "non-masters".</summary>
    string Scope { get; }

    DateOnly EffectiveFrom { get; }

    /// <summary>true — правило берётся ТОЛЬКО по явной привязке к соревнованию и никогда
    /// не участвует в подборе по дате/scope.</summary>
    bool ManualOnly { get; }
}
