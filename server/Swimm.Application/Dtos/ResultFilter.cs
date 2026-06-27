namespace Swimm.Application.Dtos;

public class ResultFilter
{
    public string? Competition { get; set; }
    /// <summary>Фильтр по событию (все дни многодневного соревнования).</summary>
    public int? EventId { get; set; }
    /// <summary>Фильтр по конкретному соревнованию (одному дню/однодневному).</summary>
    public int? CompetitionId { get; set; }
    public string? Name { get; set; }
    public string? Club { get; set; }
    public string? StyleName { get; set; }
    public string? Distance { get; set; }
    public string? Gender { get; set; }
    public string? PoolType { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
}
