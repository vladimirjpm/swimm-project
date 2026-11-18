using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Склейка клубов-дублей (мусор парсера, кросс-скриптовые пары — см.
/// docs/tasks/club-merge-plan.md) и переименований (план swimmer-club-change §5). Дубль
/// перевешивается на канонический клуб по всем FK и остаётся надгробием
/// (<c>Club.MergedIntoId</c>). Merge необратим — по умолчанию dry-run: полный план без изменений БД.
/// </summary>
public interface IClubMergeService
{
    /// <param name="pairs">пары (canonical, duplicate, вид)</param>
    /// <param name="dryRun">true (по умолчанию) — только план, БД не трогать</param>
    Task<ClubMergeReport> MergeAsync(
        IReadOnlyList<ClubMergePair> pairs, bool dryRun = true, CancellationToken ct = default);

    /// <summary>
    /// Разовый догон цепочек склеек (Н4): каждое надгробие перевешивается на итоговый живой
    /// клуб, прежние названия надгробий — тоже. Повторный прогон ничего не меняет.
    /// </summary>
    Task<ClubChainFlattenReport> FlattenChainsAsync(bool dryRun = true, CancellationToken ct = default);
}
