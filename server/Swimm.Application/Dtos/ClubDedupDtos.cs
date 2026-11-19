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
    /// <summary>same-name (одно имя у разных Id, после Normalize) | suffix (мусорный хвост
    /// парсера) | name-en (латинское Name = NameEn ивритского клуба) | word-order (те же слова в другом порядке) |
    /// swimmers (пересечение пловцов) | levenshtein.</summary>
    string Heuristic,
    /// <summary>Общих пловцов (для эвристики swimmers; иначе 0).</summary>
    int SharedSwimmers,
    /// <summary>true — «уверенная» (мусорный хвост): можно лить пачкой.</summary>
    bool Sure,
    /// <summary>Что знает loglig про общих пловцов (Р112) — только у эвристики swimmers, из
    /// сохранённых ответов, без сети; null у остальных эвристик.</summary>
    ClubPairLogligSummary? Loglig = null,
    /// <summary>Оба клуба в одних протоколах (Р113) — у всех эвристик; <see cref="ClubPairOverlap.TwoClubsLikely"/>
    /// — «скорее два клуба, не склеивать».</summary>
    ClubPairOverlap? Protocols = null,
    /// <summary>Вывод по паре (Р113): скорее один клуб / переименование / разные / не ясно.</summary>
    ClubPairRecommendation? Recommendation = null);

public sealed class ClubDedupReport
{
    public List<ClubDedupCandidate> Candidates { get; } = [];

    /// <summary>Клубов без синтетики (SYNTH%).</summary>
    public int RealClubs { get; set; }
}
