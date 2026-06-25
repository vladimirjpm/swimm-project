using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

public interface IImportJobQueue
{
    Guid Enqueue(byte[] data, string fileName);
    ImportJobStatus? GetStatus(Guid jobId);
}
