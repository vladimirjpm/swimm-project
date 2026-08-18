namespace Swimm.Application.Dtos;

/// <summary>Пара-кандидат на склейку клубов (canonical слева).</summary>
public sealed record ClubDedupCandidate(
    int CanonicalId,
    string CanonicalName,
    string? CanonicalNameEn,
    int CanonicalResults,
    int DuplicateId,
    string DuplicateName,
    string? DuplicateNameEn,
    int DuplicateResults,
    /// <summary>same-name (одно имя у разных Id) | suffix (мусорный хвост парсера) |
    /// swimmers (пересечение пловцов) | levenshtein.</summary>
    string Heuristic,
    /// <summary>Общих пловцов (для эвристики swimmers; иначе 0).</summary>
    int SharedSwimmers,
    /// <summary>true — «уверенная» (мусорный хвост): можно лить пачкой.</summary>
    bool Sure);

public sealed class ClubDedupReport
{
    public List<ClubDedupCandidate> Candidates { get; } = [];

    /// <summary>Клубов без синтетики (SYNTH%).</summary>
    public int RealClubs { get; set; }
}
