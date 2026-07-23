namespace Swimm.Application.Dtos;

/// <summary>Развязанная пара дедупа (IdA &lt; IdB); имена — для отображения в админке.</summary>
public sealed record DedupIgnoredPairDto(int IdA, string NameA, int IdB, string NameB, DateTime CreatedAt);
