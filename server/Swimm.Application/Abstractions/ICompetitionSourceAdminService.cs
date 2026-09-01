using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Привязка соревнования к его источникам стартового протокола — compID федерации
/// (<c>CompetitionSources</c>, решение от 2026-08-28).
///
/// Зачем руками, если импорт привязывает сам: у одного нашего старта источников бывает
/// несколько, а импорт знает только тот compID, из которого пришёл файл. Окружные
/// чемпионаты («8-11 חורף 2026» — север, центр ×2, юг) собираются только человеком.
/// </summary>
public interface ICompetitionSourceAdminService
{
    /// <summary>
    /// Привязки соревнования плюс кандидаты из «Входящих»: строки, чья дата попадает в
    /// диапазон дней этого соревнования и которые ещё не привязаны сюда.
    /// </summary>
    Task<CompetitionSourcesViewDto> GetAsync(int competitionId, CancellationToken ct = default);

    /// <summary>Привязать источник. Повторная привязка того же compID — не ошибка (идемпотентно).</summary>
    Task<CompetitionSourcesViewDto> LinkAsync(int competitionId, int orgCompId, CancellationToken ct = default);

    /// <summary>Снять привязку. Заявки при этом НЕ удаляются — они принадлежат compID, а не нам.</summary>
    Task<CompetitionSourcesViewDto> UnlinkAsync(int competitionId, int orgCompId, CancellationToken ct = default);
}
