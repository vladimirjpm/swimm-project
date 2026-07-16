using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

public interface IImportJobQueue
{
    Guid Enqueue(byte[] data, string fileName, IReadOnlyList<string>? categoryKeys = null, ImportEventOptions? eventOptions = null, int? discoveredId = null);
    ImportJobStatus? GetStatus(Guid jobId);
}
