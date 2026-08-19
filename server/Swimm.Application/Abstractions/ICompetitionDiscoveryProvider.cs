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
}
