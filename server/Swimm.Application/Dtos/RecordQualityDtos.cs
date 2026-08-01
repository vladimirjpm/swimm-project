namespace Swimm.Application.Dtos;

/// <summary>
/// Сводка качества справочника рекордов для дашборда /Admin
/// (docs/plans/records-quality-plan.md).
/// </summary>
/// <param name="Total">Всего строк в Records.</param>
/// <param name="Found">Сверено с протоколами: заплыв с таким временем найден.</param>
/// <param name="NotFound">
/// Сверено, заплыв не найден. ⚠ Это НЕ баг источника: у нас загружены не все годы.
/// Формулировка в UI обязана это говорить.
/// </param>
/// <param name="NotChecked">Сверка ещё не запускалась для этих строк.</param>
/// <param name="FoundWrongDate">Время нашлось, но в другой день — повод посмотреть глазами.</param>
/// <param name="LastCheckedAt">Когда последний раз гоняли сверку.</param>
/// <param name="IssuesOpen">Претензий в статусе open.</param>
/// <param name="IssuesTotal">Претензий всего, включая закрытые.</param>
/// <param name="Issues">Последние открытые претензии для панели деталей.</param>
public sealed record RecordQualitySummary(
    int Total,
    int Found,
    int NotFound,
    int NotChecked,
    int FoundWrongDate,
    DateTime? LastCheckedAt,
    int IssuesOpen,
    int IssuesTotal,
    IReadOnlyList<RecordIssueDto> Issues);

/// <summary>Строка реестра спорных рекордов.</summary>
/// <param name="RecordStillCurrent">
/// Есть ли СЕЙЧАС в Records строка с этой осью и этим временем. false — рекорд с тех пор
/// сменился (побили или источник поправил), и претензия относится к истории, а не к
/// текущей записи. Именно ради этого ключ реестра включает время.
/// </param>
public sealed record RecordIssueDto(
    int Id,
    string RegionType,
    string RegionCode,
    string Category,
    string AgeKey,
    string Gender,
    string PoolType,
    string Style,
    string Distance,
    string FlaggedTime,
    string Reason,
    string Status,
    string Note,
    string CreatedBy,
    DateTime CreatedAt,
    bool RecordStillCurrent);

/// <summary>Заведение претензии: ось рекорда + оспариваемое время + обоснование.</summary>
public sealed record RecordIssueInputDto(
    string RegionType,
    string RegionCode,
    string Category,
    string AgeKey,
    string Gender,
    string PoolType,
    string Style,
    string Distance,
    string FlaggedTime,
    string? Reason,
    string? Note);

/// <summary>Правка претензии: статус/обоснование/причина. null — поле не трогаем.</summary>
public sealed record RecordIssueUpdateDto(string? Status, string? Note, string? Reason);

/// <summary>Итог прогона сверки «рекорды ↔ протоколы».</summary>
public sealed record RecordVerifyResult(int Checked, int Found, int NotFound, int FoundWrongDate);
