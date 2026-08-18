using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>Клиент для чтения публичной карточки игрока loglig.com (docs/loglig-id-plan.md).</summary>
public interface ILogligClient
{
    /// <summary>Тянет карточку игрока loglig; null — карточка недоступна (404/500/невалидный HTML).</summary>
    Task<LogligPlayerCard?> GetPlayerCardAsync(int logligId, CancellationToken ct = default);

    /// <summary>Публичный URL карточки игрока (с актуальным seasonId — без него страница
    /// отдаёт 500, на старом сезоне — урезанную таблицу). Единый источник сборки URL.</summary>
    string BuildPublicProfileUrl(int logligId);

    /// <summary>
    /// Официальный клубный зачёт соревнования: опубликован ли он и по какой шкале посчитан.
    /// null — соревнование недоступно (сеть/404); это НЕ то же, что «зачёта нет».
    /// </summary>
    /// <param name="scaleSampleEvents">Сколько индивидуальных заплывов опросить ради шкалы.</param>
    Task<LogligCompetitionStanding?> GetCompetitionStandingAsync(
        int logligId, int scaleSampleEvents = 12, CancellationToken ct = default);
}
