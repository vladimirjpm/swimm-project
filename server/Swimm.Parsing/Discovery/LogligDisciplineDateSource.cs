using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.Parsing.Discovery;

/// <summary>
/// Даты дисциплин из сетки loglig (<c>AthleticsDisciplines</c>: «03/01/2025 09:15:00» у каждой
/// дисциплины) — docs/plans/multiday-split-plan.md, М1. Ключ строится теми же словарями, что у
/// сборщика эстафет: стиль — <see cref="LogligRelaySplitProvider.ParserStyleOf"/> (комплекс →
/// individual_medley), пол — <see cref="LogligRelaySplitProvider.RelayGender"/> (у мастерсов
/// категория начинается с «מאסטרס», и сетка сама отдаёт none; «מיקס» — mixed).
/// </summary>
public sealed class LogligDisciplineDateSource(ICompetitionDiscoveryProvider loglig) : IDisciplineDateSource
{
    public async Task<DisciplineDates> FetchAsync(int logligId, CancellationToken ct = default) =>
        FromGrid(await loglig.FetchDisciplineGridAsync(logligId, ct));

    /// <summary>Чистая часть: сетка → словарь дат. Дисциплина без времени начала пропускается.</summary>
    internal static DisciplineDates FromGrid(IEnumerable<LogligDisciplineGridRowDto> grid) =>
        new(grid
            .Where(d => d.StartAtLocal != null)
            .Select(d => (
                new DisciplineDayKey(
                    LogligRelaySplitProvider.ParserStyleOf(d.StyleName),
                    d.Distance,
                    LogligRelaySplitProvider.RelayGender(d.Category, d.Gender)),
                DateOnly.FromDateTime(d.StartAtLocal!.Value))));
}
