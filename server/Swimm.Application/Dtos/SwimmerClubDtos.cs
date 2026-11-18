namespace Swimm.Application.Dtos;

/// <summary>
/// Одна смена клуба, которую пересчёт нашёл (dry-run) или записал (Р94).
/// <paramref name="Overlap"/> — у пловца периоды двух клубов пересекаются (выступал за оба
/// одновременно); такие смены стоит посмотреть глазами, часто это дубль клуба.
/// </summary>
public sealed record SwimmerClubChangePlan(
    int SwimmerId, string SwimmerName,
    int? FromClubId, string? FromClubName,
    int ToClubId, string ToClubName,
    DateOnly LastSwimDate, bool Overlap);

/// <summary>
/// Итог пересчёта текущего клуба. <paramref name="Checked"/> — пловцов просмотрено,
/// <paramref name="Locked"/> — из них закреплены (не трогали), <paramref name="Ties"/> — ничья
/// в последнем соревновании (оставили как было), <paramref name="Applied"/> — записано ли.
/// </summary>
public sealed record SwimmerClubRefreshReport(
    int Checked, int Locked, int Ties,
    IReadOnlyList<SwimmerClubChangePlan> Changes, bool Applied);

/// <summary>Строка журнала <c>Sys_SwimmerClubChanges</c> для админки и дашборда.</summary>
public sealed record SwimmerClubChangeDto(
    long Id, int SwimmerId, string SwimmerName,
    int? FromClubId, string? FromClubName,
    int? ToClubId, string? ToClubName,
    string Source, DateOnly? LastSwimDate, DateTime ChangedAt);

/// <summary>Период выступлений за клуб (по строкам протоколов).</summary>
public sealed record SwimmerClubPeriodDto(int ClubId, string ClubName, DateOnly First, DateOnly Last, int Rows);

/// <summary>
/// Блок «Клуб пловца» на /Admin/Swimmers: что в карточке, закреплено ли, что сказало бы правило
/// (<paramref name="RuleClubId"/>; null — не определить: ничья или нет строк с настоящим клубом),
/// периоды клубов по протоколам и последние смены из журнала.
/// </summary>
public sealed record SwimmerClubAdminDto(
    int SwimmerId, string SwimmerName,
    int? ClubId, string? ClubName, bool ClubLocked,
    int? RuleClubId, string? RuleClubName,
    IReadOnlyList<SwimmerClubPeriodDto> Periods,
    IReadOnlyList<SwimmerClubChangeDto> History);

/// <summary>Итог ручной правки клуба: что было и что стало.</summary>
public sealed record SwimmerClubSetResult(
    int SwimmerId, int? FromClubId, int? ToClubId, bool WasLocked, bool Locked);

/// <summary>Почему ручная правка клуба не прошла.</summary>
public enum SwimmerClubSetError
{
    None,
    SwimmerNotFound,
    ClubNotFound,
    /// <summary>Псевдоклуб (страна/сборная) — не клуб пловца.</summary>
    PseudoClub,
}
