namespace Swimm.Application.Dtos;

/// <summary>Пара для склейки: все связи дубля перевешиваются на канонический клуб.</summary>
public sealed record ClubMergePair(int CanonicalId, int DuplicateId);

/// <summary>Итог обработки одной пары.</summary>
public sealed class ClubMergePairResult
{
    public int CanonicalId { get; init; }
    public int DuplicateId { get; init; }

    /// <summary>merged | dry-run | conflict | error</summary>
    public string Status { get; set; } = "dry-run";

    /// <summary>Человекочитаемые действия («Results: 120 → canonical», …).</summary>
    public List<string> Actions { get; } = [];

    /// <summary>Причины отказа (обе стороны с официальной группой, синтетика, не найден…).</summary>
    public List<string> Conflicts { get; } = [];
}

/// <summary>Отчёт merge-прогона (dry-run по умолчанию — БД не меняется).</summary>
public sealed class ClubMergeReport
{
    public bool DryRun { get; init; }
    public List<ClubMergePairResult> Pairs { get; } = [];
}
