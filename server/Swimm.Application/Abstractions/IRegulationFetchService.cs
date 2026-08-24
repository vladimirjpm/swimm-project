using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Достать регламент соревнования (תקנון) САМИ и разобрать: скачивание с loglig
/// (<c>ILogligClient.GetRegulationAsync</c>) + существующий <see cref="IRegulationAnalyzer"/>.
///
/// Зачем шов: регламент нужен двум потокам — кнопке «Проверить регламент» в панели строки и
/// пакетному затягиванию, где файл приложить некому. Раньше единственным входом был
/// загруженный админом PDF; он остался запасным путём для соревнований без ссылки.
/// </summary>
public interface IRegulationFetchService
{
    /// <summary>Скачать и разобрать регламент по loglig-id соревнования.</summary>
    Task<RegulationFetchDto> FetchAsync(int logligId, CancellationToken ct = default);
}
