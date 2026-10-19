using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Календарь сезона для главной <c>/competitions</c> (docs/plans/competitions-home-plan.md, Б1).
/// Правила сборки — <c>CompetitionCalendarBuilder</c>; здесь только чтение из базы.
/// </summary>
public interface ICompetitionCalendarRepository
{
    /// <param name="season">Год начала сезона (сентябрь–август).</param>
    /// <param name="today">«Сегодня» по Asia/Jerusalem — от него статусы live/upcoming/done.</param>
    Task<CompetitionCalendarDto> GetSeasonAsync(int season, DateOnly today, CancellationToken ct = default);
}
