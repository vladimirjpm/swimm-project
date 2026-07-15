using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Забор данных о соревнованиях с сайта федерации (isr.org.il, фаза 6).
/// Реализация ходит в сеть вежливо (интервалы, User-Agent) и падает ЯВНОЙ ошибкой,
/// если вёрстка сайта изменилась (0 распознанных строк ≠ успех — урок HeaderRxENinHE).
/// </summary>
public interface ICompetitionDiscoveryProvider
{
    /// <summary>Список соревнований (competitions.asp). finished=true — завершённые, false — предстоящие.</summary>
    Task<IReadOnlyList<DiscoveredListItem>> FetchListAsync(bool finished, CancellationToken ct = default);

    /// <summary>Детальная страница (comp.asp?compID=): площадка, loglig-id результатов.</summary>
    Task<DiscoveredDetails> FetchDetailsAsync(int orgCompId, CancellationToken ct = default);

    /// <summary>Скачать PDF-протокол результатов (loglig ExportSwimmingCompetitionResults).</summary>
    Task<byte[]> FetchResultsPdfAsync(int logligId, string culture = "he-IL", CancellationToken ct = default);
}
