using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Очередь прогонов «национальные рекорды по странам» (11.1.2). Прогон — фоновая задача:
/// 470 файлов в HTTP-запрос админки не пролезут, и ждать их некому.
///
/// Устройство то же, что у импорта протоколов (<see cref="IImportJobQueue"/>): очередь и
/// статусы в памяти процесса, админка опрашивает статус по <see cref="GetStatus"/>.
/// </summary>
public interface IRecordCountryRunQueue
{
    /// <summary>Поставить прогон в очередь. <paramref name="codes"/> пуст — все страны источника.</summary>
    Guid Enqueue(IReadOnlyList<string>? codes);

    RecordCountryRunStatus? GetStatus(Guid runId);
}
