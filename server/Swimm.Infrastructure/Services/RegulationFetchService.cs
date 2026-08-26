using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Забор регламента (תקנון) с loglig + разбор существующим анализатором.
///
/// Своего парсера здесь НЕТ намеренно: ссылку со страницы соревнования вынимает
/// <see cref="ILogligClient.GetRegulationAsync"/>, содержимое читает
/// <see cref="IRegulationAnalyzer"/>. Этот класс только связывает их и переводит «не нашли»
/// в человеческое сообщение.
/// </summary>
public class RegulationFetchService : IRegulationFetchService
{
    private readonly ILogligClient _loglig;
    private readonly IRegulationAnalyzer _analyzer;
    private readonly ILogger<RegulationFetchService> _logger;

    public RegulationFetchService(
        ILogligClient loglig, IRegulationAnalyzer analyzer, ILogger<RegulationFetchService> logger)
    {
        _loglig = loglig;
        _analyzer = analyzer;
        _logger = logger;
    }

    public async Task<RegulationFetchDto> FetchAsync(int logligId, CancellationToken ct = default)
    {
        var doc = await _loglig.GetRegulationAsync(logligId, ct);
        if (doc is null)
            return new RegulationFetchDto(false, null, null,
                "У соревнования нет ссылки на регламент (תקנון) на loglig — или файл не скачался.");

        using var stream = new MemoryStream(doc.Pdf);
        var analysis = _analyzer.Analyze(stream, doc.FileName);

        if (analysis.Error != null)
        {
            _logger.LogWarning("Регламент {DocId} не прочитался: {Error}", doc.DocId, analysis.Error);
            return new RegulationFetchDto(false, doc.Url, null, analysis.Error);
        }

        return new RegulationFetchDto(true, doc.Url, analysis);
    }
}
