using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// «Затянуть» одну строку входящих: скачать протоколы (HE+EN) из loglig, разобрать парсером,
/// проверить рекорды и официальный клубный зачёт, отложить разбор в кэш до импорта.
///
/// Зачем шов: ровно это делают ДВА потока — кнопка «Затянуть» в строке и пакетный забор.
/// Пока логика жила в контроллере, второй поток мог только скопировать её.
/// </summary>
public interface IDiscoveryPreviewService
{
    /// <summary>Затянуть и разобрать; ошибка отдаётся полем <c>Error</c>, исключений не кидает.</summary>
    Task<DiscoveryPreviewResult> PreviewAsync(int discoveredId, CancellationToken ct = default);

    /// <summary>Достать отложенный разбор (для импорта / заведения правила очков).</summary>
    DiscoveryPreviewEntry? GetEntry(Guid previewId);

    /// <summary>Убрать разбор из кэша — после того, как он ушёл в импорт.</summary>
    void RemoveEntry(Guid previewId);

    /// <summary>Сколько живёт отложенный разбор — для сообщений «превью истекло».</summary>
    TimeSpan EntryLifetime { get; }

    /// <summary>
    /// Скачать протокол строки входящих (PDF нужной культуры). Отдельно от разбора: его
    /// качают и «Скачать PDF», и «Синхр. языки». <paramref name="refreshIfMissing"/> —
    /// подтянуть детали (loglig-id), если их ещё не загружали.
    /// </summary>
    Task<DiscoveryProtocolPdf> FetchProtocolAsync(
        int discoveredId, string language, bool refreshIfMissing, CancellationToken ct = default);
}
