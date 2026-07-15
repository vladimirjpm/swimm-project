using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Склейка пловцов-дублей (варианты написания имени из PDF-импорта, см.
/// docs/tasks/dedup-report.md). Дубль перевешивается на канонического пловца по всем
/// FK и удаляется. Merge необратим — по умолчанию dry-run: полный план без изменений БД.
/// </summary>
public interface ISwimmerMergeService
{
    /// <param name="pairs">пары (canonical, duplicate)</param>
    /// <param name="dryRun">true (по умолчанию) — только план, БД не трогать</param>
    Task<SwimmerMergeReport> MergeAsync(
        IReadOnlyList<SwimmerMergePair> pairs, bool dryRun = true, CancellationToken ct = default);
}
