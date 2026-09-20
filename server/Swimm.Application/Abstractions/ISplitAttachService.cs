using Swimm.Application.Mapping;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Разобранные промежуточные ОДНОГО соревнования loglig — вход доклейки
/// (docs/plans/splits-attach-without-repull-plan.md §2).
/// </summary>
/// <param name="Message">Что удалось скачать и разобрать — строкой в отчёт.</param>
public sealed record SplitSource(
    IReadOnlyList<SplitSourceTeam> Teams, IReadOnlyList<SplitSourceSwim> Swims, string Message);

/// <summary>
/// Источник промежуточных для доклейки БЕЗ переимпорта. Отдельный порт от
/// <see cref="IRelaySplitProvider"/>: тот дописывает промежуточные в РАЗОБРАННЫЙ JSON
/// протокола (путь импорта), а этот просто отдаёт разобранное — сопоставлять со строками
/// базы будет <see cref="SplitAttachMatcher"/>.
/// </summary>
public interface ISplitSourceProvider
{
    Task<SplitSource> FetchAsync(int logligId, CancellationToken ct = default);
}

/// <summary>Итог доклейки по одному дню соревнования.</summary>
public sealed record SplitAttachDay(int CompetitionId, string Name, string Date, int LegWrites, int SwimWrites);

/// <summary>Итог доклейки: отчёт сопоставления + что реально записано по дням.</summary>
public sealed record SplitAttachResult(
    bool Applied, int LogligId, IReadOnlyList<SplitAttachDay> Days, SplitAttachReport Report, string Message)
{
    public static SplitAttachResult Failed(string message) =>
        new(false, 0, [], new SplitAttachReport(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), message);
}

/// <summary>
/// Доклейка промежуточных к строкам, которые УЖЕ в базе: пишет только
/// <c>RelayMembers.SplitTime</c> и <c>Results.TimeSplit</c>, ничего не создавая и не удаляя
/// (решение Влада 20.09.2026 — основной путь вместо переимпорта, см. план §1).
/// </summary>
public interface ISplitAttachService
{
    /// <param name="competitionId">Любой день старта — прогон идёт по ВСЕМ дням его события (§6-2).</param>
    /// <param name="logligId">Задан — берём его; иначе ищем по строке discovery (§6-1).</param>
    /// <param name="apply">false — сухой прогон: отчёт тот же, в базу ничего не пишем.</param>
    Task<SplitAttachResult> AttachAsync(
        int competitionId, int? logligId, bool apply, CancellationToken ct = default);
}
