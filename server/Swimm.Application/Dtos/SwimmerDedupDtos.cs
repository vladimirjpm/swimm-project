namespace Swimm.Application.Dtos;

/// <summary>Пара-кандидат на склейку для админ-UI (canonical слева — у кого больше результатов).</summary>
public sealed record SwimmerDedupCandidate(
    int CanonicalId,
    string CanonicalName,
    string? CanonicalClub,
    int CanonicalResults,
    int DuplicateId,
    string DuplicateName,
    string? DuplicateClub,
    int DuplicateResults,
    int BirthYear,
    string? Gender,
    int Distance,
    /// <summary>true — «уверенная» (dist ≤ 1, год известен, клуб/пол не противоречат).</summary>
    bool Sure);

/// <summary>Пловец-сирота: 0 результатов и 0 связей.</summary>
public sealed record SwimmerOrphan(int Id, string Name, int BirthYear, string Origin, string? Club);

public sealed class SwimmerDedupReport
{
    public List<SwimmerDedupCandidate> Candidates { get; } = [];
    public List<SwimmerOrphan> Orphans { get; } = [];
    public int RealSwimmers { get; set; }
}

/// <summary>Итог массового удаления сирот (B2). Сервер сам пересчитывает актуальных
/// сирот тем же критерием — SkippedIds это id из запроса, которые сиротами не оказались.</summary>
public sealed record SwimmerOrphanCleanupReport(int Deleted, List<int> DeletedIds, List<int> SkippedIds);
