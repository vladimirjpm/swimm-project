using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Поиск кандидатов на склейку клубов-дублей для админ-UI (docs/tasks/club-merge-plan.md,
/// фаза B). Три эвристики по убыванию уверенности: мусорный хвост парсера («… DNS»,
/// «… 4.4 SW /»), пересечение пловцов (ловит кросс-скрипт без транслитерации),
/// Левенштейн ≤ 1 внутри одного скрипта. Синтетика (SYNTH%) и псевдоклубы Maccabiah
/// (страны/команды: USA, Israel, M25…) исключаются. Читает БД, ничего не пишет.
/// </summary>
public interface IClubDedupService
{
    Task<ClubDedupReport> FindCandidatesAsync(CancellationToken ct = default);
}
