using Swimm.Application.Mapping;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Даты дисциплин старта по пособытийному источнику (сетка loglig) — для деления схлопнутых
/// многодневок (docs/plans/multiday-split-plan.md, М1). Реализация — в Swimm.Parsing: там
/// общие с эстафетами словари стиля и пола, переписывать их здесь нельзя — разъедутся.
/// </summary>
public interface IDisciplineDateSource
{
    Task<DisciplineDates> FetchAsync(int logligId, CancellationToken ct = default);
}
