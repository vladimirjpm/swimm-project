namespace Swimm.Application.Dtos;

/// <summary>Пара для склейки: все связи дубля перевешиваются на канонического пловца.</summary>
public sealed record SwimmerMergePair(int CanonicalId, int DuplicateId);

/// <summary>Итог обработки одной пары.</summary>
public sealed class SwimmerMergePairResult
{
    public int CanonicalId { get; init; }
    public int DuplicateId { get; init; }

    /// <summary>merged | dry-run | conflict | error</summary>
    public string Status { get; set; } = "dry-run";

    /// <summary>Человекочитаемые действия («Results: 3 → canonical», …).</summary>
    public List<string> Actions { get; } = [];

    /// <summary>Причины отказа (общие заплывы, не найден пловец, синтетика…).</summary>
    public List<string> Conflicts { get; } = [];
}

/// <summary>Отчёт merge-прогона (dry-run по умолчанию — БД не меняется).</summary>
public sealed class SwimmerMergeReport
{
    public bool DryRun { get; init; }
    public List<SwimmerMergePairResult> Pairs { get; } = [];
}
