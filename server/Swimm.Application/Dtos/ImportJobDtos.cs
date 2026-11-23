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

    /// <summary>compID isr.org.il задачи (null — файл с /Admin/Import): по нему чек-лист узнаёт,
    /// что доклейка его строки ещё идёт.</summary>
    public int? OrgCompId { get; init; }

    /// <summary>
    /// Автодоклейка промежуточных после импорта; null — не ставилась (импорт отказал, нет compID,
    /// тумблер выключен). Идёт ПОСЛЕ <see cref="ImportJobState.Completed"/>: итог импорта она не меняет.
    /// </summary>
    public ImportSplitsStatus? Splits { get; set; }
}

/// <summary>Ход автодоклейки промежуточных у задачи импорта (живёт в памяти, как сама очередь).</summary>
public sealed class ImportSplitsStatus
{
    /// <summary>queued — ждёт в очереди пост-шагов; running — качает loglig; done — итог в <see cref="Kind"/>.</summary>
    public string State { get; set; } = "queued";

    /// <summary>Код исхода (<c>SplitAutoAttachRules.Code</c>), пока не done — null.</summary>
    public string? Kind { get; set; }

    /// <summary>Короткий итог для чек-листа («доклеено: личных 340» / «на loglig промежуточных нет»).</summary>
    public string? Text { get; set; }

    /// <summary>Полный отчёт доклейки — подсказкой.</summary>
    public string? Message { get; set; }
}
