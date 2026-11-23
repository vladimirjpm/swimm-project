using Swimm.Application.Mapping;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Итог автодоклейки промежуточных после импорта (решение Влада 23.11.2026).
/// </summary>
/// <param name="Kind">Что вышло — по всему прогону (худший исход из событий compID).</param>
/// <param name="SwimWrites">Личных строк, получивших промежуточные.</param>
/// <param name="LegWrites">Ног эстафет, получивших этапы.</param>
/// <param name="Message">Отчёт доклейки целиком (та же строка, что у кнопки и CLI) — в журнал задачи.</param>
/// <param name="CompetitionIds">Дни, по которым прошла доклейка (все дни затронутых событий).</param>
public sealed record SplitAutoOutcome(
    SplitAutoKind Kind, int SwimWrites, int LegWrites, string Message, IReadOnlyList<int> CompetitionIds);

/// <summary>
/// Доклейка промежуточных САМА после импорта — тот же <see cref="ISplitAttachService"/>, что у
/// кнопки «Промежуточные» и CLI <c>--attach-splits</c>, только боевым прогоном и по всем дням,
/// у которых этот compID. Каждый день получает строку аудита <c>competition.attach-splits</c> с
/// актором <see cref="SplitAutoAttachRules.ActorName"/> — в том числе «нечего доклеивать» и «не
/// удалось»: по ней шаг 9 чек-листа показывает итог и после перезагрузки страницы.
/// Исключений наружу не бросает: сбой — это <see cref="SplitAutoKind.Failed"/>.
/// </summary>
public interface ISplitAutoAttachService
{
    Task<SplitAutoOutcome> AttachAfterImportAsync(int orgCompId, Guid jobId, CancellationToken ct = default);
}

/// <summary>Последняя доклейка дня — для шага 9 чек-листа.</summary>
/// <param name="At">Когда (UTC).</param>
/// <param name="Auto">Сама после импорта (иначе — кнопка или CLI).</param>
public sealed record SplitAttachNote(
    DateTime At, bool Auto, SplitAutoKind Kind, int SwimWrites, int LegWrites, string Summary);

/// <summary>
/// Чтение итогов доклейки из аудита: последняя строка <c>competition.attach-splits</c> по дню.
/// </summary>
public interface ISplitAttachJournal
{
    Task<IReadOnlyDictionary<int, SplitAttachNote>> GetLatestAsync(
        IReadOnlyCollection<int> competitionIds, CancellationToken ct = default);
}
