namespace Swimm.Application.Dtos;

/// <summary>
/// Отчёт переноса результатов между соревнованиями (фаза 7.3). Dry-run (Applied=false) —
/// сколько бы переехало и есть ли риск дублей; apply — то же, но перенос выполнен.
/// </summary>
public sealed class ResultTransferReport
{
    public int SourceId { get; set; }
    public string SourceName { get; set; } = "";
    public string SourceDate { get; set; } = "";

    public int TargetId { get; set; }
    public string TargetName { get; set; } = "";
    public string TargetDate { get; set; } = "";

    /// <summary>Сколько результатов в источнике (все переедут).</summary>
    public int ResultsToMove { get; set; }

    /// <summary>Сколько результатов уже в цели (для контекста).</summary>
    public int TargetExistingResults { get; set; }

    /// <summary>
    /// Потенциальные дубли: индивидуальные заплывы источника, у которых (пловец, стиль,
    /// дистанция) уже есть в цели. &gt;0 — предупреждение (перенос не запрещаем, но админ решает).
    /// </summary>
    public int OverlapCount { get; set; }

    public bool Applied { get; set; }
}
