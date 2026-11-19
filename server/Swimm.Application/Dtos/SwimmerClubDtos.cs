namespace Swimm.Application.Dtos;

/// <summary>
/// Одна смена клуба, которую пересчёт нашёл (dry-run) или записал (Р95).
/// <paramref name="Overlap"/> — у пловца периоды двух клубов пересекаются (выступал за оба
/// одновременно); такие смены стоит посмотреть глазами, часто это дубль клуба.
/// <paramref name="FromMasters"/> — клуб взят только из мастерс-стартов (<c>Swimmers.ClubFromMasters</c>, Р109).
/// </summary>
public sealed record SwimmerClubChangePlan(
    int SwimmerId, string SwimmerName,
    int? FromClubId, string? FromClubName,
    int ToClubId, string ToClubName,
    DateOnly LastSwimDate, bool Overlap, bool FromMasters = false);

/// <summary>
/// Итог пересчёта текущего клуба. <paramref name="Checked"/> — пловцов просмотрено,
/// <paramref name="Locked"/> — из них закреплены (не трогали), <paramref name="Ties"/> — ничья
/// в последнем соревновании (оставили как было), <paramref name="Changes"/> — кандидаты на смену
/// (с Р98 карточку сам пересчёт НЕ меняет: кандидата подтверждает loglig),
/// <paramref name="Applied"/> — кандидаты записаны в <c>Sys_SwimmerClubCandidates</c>,
/// <paramref name="Withdrawn"/> — сколько ждущих кандидатов снято (клуб снова совпал с карточкой,
/// пловца закрепили или ничья). <paramref name="MastersFlagged"/> — скольким пловцам флаг
/// <c>ClubFromMasters</c> поставлен этим прогоном (в dry-run — поставился бы), <paramref name="FromMasters"/> —
/// у скольких просмотренных клуб считается по мастерс-стартам (Р109).
/// </summary>
public sealed record SwimmerClubRefreshReport(
    int Checked, int Locked, int Ties,
    IReadOnlyList<SwimmerClubChangePlan> Changes, bool Applied, int Withdrawn = 0,
    int MastersFlagged = 0, int FromMasters = 0);

/// <summary>
/// Кандидат на смену клуба и что о нём сказал loglig (Р98). <paramref name="Verdict"/> —
/// <c>SwimmerClubLogligVerdicts</c>; null — ещё не сверяли. <paramref name="CheckedAt"/> —
/// когда карточку loglig открывали последний раз. <paramref name="FromMasters"/> — у пловца флаг
/// «клуб по мастерс-стартам» (Р109): новый клуб — из мастерс-соревнований.
/// </summary>
public sealed record SwimmerClubCandidateDto(
    int SwimmerId, string SwimmerName,
    int? FromClubId, string? FromClubName,
    int ToClubId, string ToClubName,
    DateOnly LastSwimDate, bool Overlap, string Source,
    string? Verdict, int? LogligId, string? LogligClubName, DateTime? CheckedAt,
    DateTime CreatedAt, bool FromMasters = false);

/// <summary>
/// Вердикт loglig по одной смене (dry-run CLI и кнопка «проверить сейчас»).
/// <paramref name="FromCache"/> — ответ loglig взят из кэша (моложе суток), в сеть не ходили.
/// <paramref name="Applied"/> — смена записана в карточку.
/// </summary>
public sealed record SwimmerClubVerdictDto(
    int SwimmerId, string Verdict, int? LogligId, string? LogligClubName, bool FromCache, bool Applied);

/// <summary>
/// Итог сверки кандидатов с loglig. <paramref name="Requests"/> — сколько карточек открыли (сеть),
/// <paramref name="FromCache"/> — сколько взяли из суточного кэша, <paramref name="Applied"/> —
/// сколько смен записано, <paramref name="ByVerdict"/> — счёт по вердиктам.
/// </summary>
public sealed record SwimmerClubCheckReport(
    int Checked, int Requests, int FromCache, int Applied,
    IReadOnlyDictionary<string, int> ByVerdict,
    IReadOnlyList<SwimmerClubVerdictDto> Verdicts);

/// <summary>Какие кандидаты сверять с loglig.</summary>
public enum SwimmerClubCheckScope
{
    /// <summary>Ещё не сверённые (вердикта нет) — проход сразу после импорта.</summary>
    Unchecked,
    /// <summary>Все ждущие — суточный прогон (ответ моложе суток берётся из кэша).</summary>
    AllPending,
}

/// <summary>Ждущие кандидаты для дашборда: счёт по причинам и первые строки каждой.</summary>
public sealed record SwimmerClubPendingSummary(
    int Total, IReadOnlyDictionary<string, int> ByVerdict, IReadOnlyList<SwimmerClubCandidateDto> Items);

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
/// периоды клубов по протоколам и последние смены из журнала. <paramref name="ClubFromMasters"/> — флаг
/// <c>Swimmers.ClubFromMasters</c> как лежит (null — не решено), <paramref name="RuleFromMasters"/> — правило
/// считало только мастерс-старты (Р109).
/// </summary>
public sealed record SwimmerClubAdminDto(
    int SwimmerId, string SwimmerName,
    int? ClubId, string? ClubName, bool ClubLocked,
    int? RuleClubId, string? RuleClubName,
    IReadOnlyList<SwimmerClubPeriodDto> Periods,
    IReadOnlyList<SwimmerClubChangeDto> History,
    SwimmerClubCandidateDto? Candidate = null,
    int? LogligId = null,
    bool? ClubFromMasters = null, bool RuleFromMasters = false);

/// <summary>Итог ручной правки клуба: что было и что стало (и флаг «клуб по мастерс-стартам», Р109).</summary>
public sealed record SwimmerClubSetResult(
    int SwimmerId, int? FromClubId, int? ToClubId, bool WasLocked, bool Locked,
    bool? WasFromMasters = null, bool? FromMasters = null);

/// <summary>
/// Пловец с флагом «клуб по мастерс-стартам» (Р109), у которого есть обычные старты за ДРУГОЙ клуб:
/// ради них флаг и заведён. <paramref name="OtherClubs"/> — клубы обычных стартов (не мастерс, без
/// псевдо, склеенные — приёмником), отличные от клуба карточки.
/// </summary>
public sealed record SwimmerClubMastersItem(
    int SwimmerId, string SwimmerName, int? ClubId, string? ClubName, IReadOnlyList<string> OtherClubs);

/// <summary>
/// Флаг «клуб по мастерс-стартам» для дашборда (Р109): <paramref name="Flagged"/> — у скольких стоит,
/// <paramref name="Mixed"/> — у скольких из них есть обычные старты за другой клуб, <paramref name="Items"/> —
/// первые из них.
/// </summary>
public sealed record SwimmerClubMastersSummary(int Flagged, int Mixed, IReadOnlyList<SwimmerClubMastersItem> Items);

/// <summary>Почему ручная правка клуба не прошла.</summary>
public enum SwimmerClubSetError
{
    None,
    SwimmerNotFound,
    ClubNotFound,
    /// <summary>Псевдоклуб (страна/сборная) — не клуб пловца.</summary>
    PseudoClub,
}
