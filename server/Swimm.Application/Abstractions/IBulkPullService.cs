using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Пакетное затягивание входящих: одна кнопка на выборку фильтров вместо двадцати нажатий
/// «Затянуть» (docs/plans/bulk-pull-plan.md).
///
/// Работа фоновая и ПОСЛЕДОВАТЕЛЬНАЯ: каждая строка — это два PDF, разбор, проба клубного
/// зачёта и регламент; параллелить их значит долбить loglig. Состояние живёт в памяти
/// процесса — пачка переживает перезагрузку страницы, но не перезапуск приложения.
/// </summary>
public interface IBulkPullService
{
    /// <summary>Сколько строк берём за раз. Больше — получасовой забор и десятки мегабайт в памяти.</summary>
    int MaxBatchSize { get; }

    /// <summary>
    /// Поставить пачку. Список — то, что видит админ в текущей выборке; сервер сам отбросит
    /// строки, которые тянуть нечего (пустой протокол, скрытые, уже в БД), и — если не просили
    /// обратного — чемпионаты Израиля.
    /// </summary>
    Task<BulkPullBatchDto> StartAsync(
        IReadOnlyList<int> discoveredIds, bool includeChampionships, CancellationToken ct = default);

    /// <summary>Состояние пачки; null — такой пачки нет (перезапуск приложения, чужой id).</summary>
    BulkPullBatchDto? GetStatus(Guid batchId);

    /// <summary>
    /// Импортировать отмеченные строки пачки. Категория ставится всем одна
    /// (<c>results-8-99</c>), перезапись и удаление лишнего НЕ применяются никогда.
    /// </summary>
    Task<BulkImportResultDto> ImportAsync(
        Guid batchId, IReadOnlyList<int> discoveredIds, CancellationToken ct = default);
}
