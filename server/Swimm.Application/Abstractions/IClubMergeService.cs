using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Склейка клубов-дублей (мусор парсера, кросс-скриптовые пары — см.
/// docs/tasks/club-merge-plan.md). Дубль перевешивается на канонический клуб по всем
/// FK и удаляется. Merge необратим — по умолчанию dry-run: полный план без изменений БД.
/// </summary>
public interface IClubMergeService
{
    /// <param name="pairs">пары (canonical, duplicate)</param>
    /// <param name="dryRun">true (по умолчанию) — только план, БД не трогать</param>
    Task<ClubMergeReport> MergeAsync(
        IReadOnlyList<ClubMergePair> pairs, bool dryRun = true, CancellationToken ct = default);
}
