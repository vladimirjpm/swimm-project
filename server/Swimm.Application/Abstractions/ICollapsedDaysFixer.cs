using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Шаг импорта loglig: даты дисциплин многодневки из сетки вместо одной даты протокола
/// (docs/plans/multiday-split-plan.md, М3; логика — <see cref="Mapping.CollapsedDaysRewriter"/>).
/// Сбой источника не роняет импорт: протокол уходит как был, причина — в предупреждения.
/// </summary>
public interface ICollapsedDaysFixer
{
    Task<ParsedCompetition> FixAsync(int logligId, ParsedCompetition parsed, CancellationToken ct = default);
}
