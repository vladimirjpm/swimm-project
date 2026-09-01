using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Забор данных о соревнованиях с сайта федерации (isr.org.il, фаза 6).
/// Реализация ходит в сеть вежливо (интервалы, User-Agent) и падает ЯВНОЙ ошибкой,
/// если вёрстка сайта изменилась (0 распознанных строк ≠ успех — урок HeaderRxENinHE).
/// </summary>
public interface ICompetitionDiscoveryProvider
{
    /// <summary>
    /// Список соревнований (competitions.asp). finished=true — завершённые, false — предстоящие.
    /// year — сезон сайта (cYear); null = текущий.
    /// </summary>
    Task<IReadOnlyList<DiscoveredListItem>> FetchListAsync(
        bool finished, int? year = null, CancellationToken ct = default);

    /// <summary>Детальная страница (comp.asp?compID=): площадка, loglig-id результатов.</summary>
    Task<DiscoveredDetails> FetchDetailsAsync(int orgCompId, CancellationToken ct = default);

    /// <summary>Скачать PDF-протокол результатов (loglig ExportSwimmingCompetitionResults).</summary>
    Task<byte[]> FetchResultsPdfAsync(int logligId, string culture = "he-IL", CancellationToken ct = default);

    /// <summary>
    /// Идентификаторы ПОСОБЫТИЙНЫХ результатов соревнования (страница AthleticsDisciplines):
    /// по одному на дисциплину-категорию, в порядке программы. Нужны там, где PDF-экспорт
    /// беднее сайта: он склеивает утреннюю и вечернюю сессии в один список, а сайт держит
    /// их разными событиями (И13, docs/data-integrity.md §10).
    /// </summary>
    Task<IReadOnlyList<int>> FetchEventIdsAsync(int logligId, CancellationToken ct = default);

    /// <summary>Результаты одного события: секции с раундом, места, времена и официальные очки.</summary>
    Task<LogligEventResultsDto> FetchEventResultsAsync(int eventId, CancellationToken ct = default);

    /// <summary>
    /// Сетка заплывов дня (та же страница AthleticsDisciplines, что у <see cref="FetchEventIdsAsync"/>,
    /// но разобранная целиком): программа, категории, ВРЕМЯ СТАРТА каждого заплыва и счётчики
    /// «записалось / участвует».
    ///
    /// Зачем отдельно от <see cref="FetchEventIdsAsync"/>: тот берёт id только из кнопок
    /// результатов, а у предстоящего соревнования их ещё нет — то есть ровно там, где нужен
    /// стартовый протокол, он вернул бы ноль. Здесь id берётся из любой кнопки строки.
    /// </summary>
    Task<IReadOnlyList<LogligDisciplineGridRowDto>> FetchDisciplineGridAsync(
        int logligId, CancellationToken ct = default);

    /// <summary>
    /// Стартовый протокол одного заплыва: кто, в каком заплыве, на какой дорожке и во сколько.
    /// Пустой список строк — законный ответ (в заплыв никто не записался), в отличие от
    /// <see cref="FetchEventIdsAsync"/>, где ноль означает сломанную вёрстку.
    /// </summary>
    Task<LogligStartListDto> FetchStartListAsync(int disciplineId, CancellationToken ct = default);
}
