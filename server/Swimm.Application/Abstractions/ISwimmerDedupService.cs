using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Поиск кандидатов на склейку пловцов для админ-UI (фаза 7.2) — C#-порт логики
/// server/db/dedup-report.sql: нормализация имени (иврит: финальные буквы, гереш),
/// Левенштейн ≤ 2 по полному имени (прямой/переставленный/EN), одинаковый BirthYear.
/// Синтетика (SYNTH-) исключается. Читает БД, ничего не пишет.
/// </summary>
public interface ISwimmerDedupService
{
    /// <summary>Пары-кандидаты (уверенные + спорные) и сироты.</summary>
    Task<SwimmerDedupReport> FindCandidatesAsync(CancellationToken ct = default);
}
