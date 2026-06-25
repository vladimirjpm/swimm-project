using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Порт для чтения правил начисления клубных очков.
/// </summary>
public interface IClubPointsRepository
{
    /// <summary>Возвращает все правила со шкалой очков по местам.</summary>
    Task<IReadOnlyList<ClubPointsRuleDto>> GetRulesAsync();
}
