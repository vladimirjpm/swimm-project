using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Узнавание выпуска серии турнира (docs/plans/official-awards-plan.md §5.8): серия — по ключевой
/// фразе в названии, часть — по возрастам пловцов дня (правило — <c>SeriesMatch</c>). Одна точка
/// на импорт (привязка после коммита) и проверки Health (только диагноз).
/// </summary>
public interface ISeriesBinder
{
    /// <summary>
    /// Диагноз без записи: для каждого дня, чьё название подходит под фразу какой-либо серии, или
    /// уже привязанного к серии — что подходит и какая часть выбирается. null — все дни базы.
    /// Дни вне серий в ответ не попадают.
    /// </summary>
    Task<IReadOnlyList<SeriesBindingDto>> EvaluateAsync(
        IReadOnlyCollection<int>? competitionIds = null, CancellationToken ct = default);

    /// <summary>
    /// Привязать непривязанные дни из списка, если серия и часть определились однозначно. Уже
    /// проставленную (руками) часть не трогает. Возвращает диагноз по каждому подошедшему дню.
    /// </summary>
    Task<IReadOnlyList<SeriesBindingDto>> BindAsync(
        IReadOnlyCollection<int> competitionIds, CancellationToken ct = default);
}
