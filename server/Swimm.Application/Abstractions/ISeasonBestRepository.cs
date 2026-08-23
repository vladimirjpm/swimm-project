using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Национальный season best по НАШИМ протоколам — источник таба «Season best» рядом с
/// возрастными рекордами на странице результатов (design_handoff_age_records_sb).
///
/// Клубный аналог живёт в <see cref="IClubPublicRepository"/> и отвечает на другой вопрос
/// («наши первые в стране»); здесь — просто лучшее время сезона в каждой ступени.
/// </summary>
public interface ISeasonBestRepository
{
    /// <summary>
    /// Лучшее время сезона для одной дисциплины в каждой паре «пол × возраст в сезоне».
    ///
    /// Решения по составу выборки (Влад, 2026-08-22):
    /// • ось возраста — СЕЗОННАЯ (SeasonMath.AgeInSeason), не календарная ось справочника;
    /// • соревнования masters (<c>Competition.IsMasters</c>) не участвуют вовсе;
    /// • эстафетные ноги, незачётные и помеченные <c>SuspectReason</c> заплывы отброшены —
    ///   как и в клубном season best.
    ///
    /// <paramref name="distance">Как в Results.Distance — без «m»: «50», «100».</paramref>
    /// <paramref name="poolType">«25m» / «50m»; null — оба бассейна в одной выборке.</paramref>
    /// <paramref name="season">Год НАЧАЛА сезона; null — текущий.</paramref>
    /// </summary>
    Task<SeasonBestNationalDto> GetNationalSeasonBestAsync(
        string style, string distance, string? poolType, int? season, CancellationToken ct = default);
}
