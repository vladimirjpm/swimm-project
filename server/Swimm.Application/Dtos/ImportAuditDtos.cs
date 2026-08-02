namespace Swimm.Application.Dtos;

/// <summary>Расхождение по одному заплыву: сколько в файле, сколько в БД.</summary>
public sealed record ImportAuditEventDiff(
    int CompetitionId, string EventKey, int ExpectedRows, int ActualRows);

/// <summary>
/// Итог сверки одного дня протокола с соревнованием в БД. <paramref name="CompetitionId"/>
/// = null — день файла вообще не сопоставился с БД (импортировали не всё либо дата разошлась).
/// </summary>
public sealed record ImportAuditDay(
    string Date, int? CompetitionId, string CompetitionName,
    int ExpectedRows, int ActualRows, IReadOnlyList<ImportAuditEventDiff> Mismatches);

/// <summary>Итог сверки одной discovery-записи (одного протокола, возможно многодневного).</summary>
public sealed record ImportAuditReport(
    int DiscoveredId, int OrgCompId, string Name, string? Error,
    IReadOnlyList<ImportAuditDay> Days)
{
    public bool HasProblems => Error != null || Days.Any(d => d.CompetitionId == null || d.Mismatches.Count > 0);
    public int MismatchCount => Days.Sum(d => d.Mismatches.Count);
}
