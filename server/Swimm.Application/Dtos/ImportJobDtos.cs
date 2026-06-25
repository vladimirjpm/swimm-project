namespace Swimm.Application.Dtos;

public enum ImportJobState { Queued, Running, Completed, Failed }

public class ImportJobStatus
{
    public Guid JobId { get; init; }
    public ImportJobState State { get; set; }
    public DateTimeOffset QueuedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
    public ImportResult? Result { get; set; }
    public string? Error { get; set; }
}
