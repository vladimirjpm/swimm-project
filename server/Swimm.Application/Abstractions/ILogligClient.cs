using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>Клиент для чтения публичной карточки игрока loglig.com (docs/loglig-id-plan.md).</summary>
public interface ILogligClient
{
    /// <summary>
    /// Тянет карточку игрока loglig; null — карточка недоступна (404/500/невалидный HTML).
    /// <paramref name="seasonId"/> — сезон, за который смотреть результаты; null = сезон из
    /// конфига (текущий). Для проверки СТАРОГО протокола нужен сезон того соревнования:
    /// в текущем сезоне его заплывов ещё/уже нет.
    /// </summary>
    Task<LogligPlayerCard?> GetPlayerCardAsync(
        int logligId, int? seasonId = null, CancellationToken ct = default);

    /// <summary>Публичный URL карточки игрока (с актуальным seasonId — без него страница
    /// отдаёт 500, на старом сезоне — урезанную таблицу). Единый источник сборки URL.</summary>
    string BuildPublicProfileUrl(int logligId, int? seasonId = null, bool resultsTab = false);

    /// <summary>
    /// seasonId соревнования — со страницы `AthleticsDisciplines/{logligId}`. Нужен, чтобы
    /// открыть карточку пловца ЗА ТОТ сезон, в котором плыли протокол.
    /// </summary>
    Task<int?> GetCompetitionSeasonIdAsync(int competitionLogligId, CancellationToken ct = default);

    /// <summary>
    /// Официальный клубный зачёт соревнования: опубликован ли он и по какой шкале посчитан.
    /// null — соревнование недоступно (сеть/404); это НЕ то же, что «зачёта нет».
    /// </summary>
    /// <param name="scaleSampleEvents">Сколько индивидуальных заплывов опросить ради шкалы.</param>
    Task<LogligCompetitionStanding?> GetCompetitionStandingAsync(
        int logligId, int scaleSampleEvents = 12, CancellationToken ct = default);

    /// <summary>
    /// Регламент соревнования («תקנון», PDF) со страницы соревнования loglig. null — ссылки
    /// на регламент нет (её ставят не всем) либо файл не скачался.
    /// </summary>
    Task<LogligRegulationDoc?> GetRegulationAsync(int logligId, CancellationToken ct = default);

    /// <summary>
    /// Участники соревнования с их loglig-id — со страниц заплывов, где имя напечатано
    /// ссылкой на карточку. Обход последовательный и останавливается, как только найдены все
    /// <paramref name="wanted"/> (ключи вида «имя#год», см. реализацию) либо кончились заплывы
    /// / исчерпан <paramref name="maxEvents"/>.
    /// </summary>
    Task<IReadOnlyList<LogligParticipant>> GetCompetitionParticipantsAsync(
        int competitionLogligId,
        IReadOnlyCollection<string>? wanted = null,
        int maxEvents = 60,
        CancellationToken ct = default);
}
