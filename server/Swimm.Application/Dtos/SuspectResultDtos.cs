namespace Swimm.Application.Dtos;

/// <summary>Итог прогона проверок качества.</summary>
public sealed record SuspectScanResultDto(
    int Scanned,
    int Flagged,
    int Cleared,
    int ManualKept,
    IReadOnlyList<SuspectRowDto> Rows);

/// <summary>Помеченная строка — в объёме, достаточном для таблицы в админке.</summary>
public sealed record SuspectRowDto(
    long ResultId,
    int CompetitionId,
    DateTime CompetitionDate,
    string SwimmerName,
    string Club,
    string StyleName,
    string Distance,
    string Gender,
    string Time,
    string Reason,
    bool IsManual,
    string? Note);
