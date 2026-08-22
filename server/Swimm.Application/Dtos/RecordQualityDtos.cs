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
/// <param name="AgeAxis">Текущее значение настройки RecordAgeAxis: calendar / season.</param>
/// <param name="AgeAxisChecked">У скольких рекордов ступень вообще удалось проверить.</param>
/// <param name="AgeAxisBoth">Ступень сходится по обеим осям (заплыв января–августа).</param>
/// <param name="AgeAxisCalendarOnly">Только по календарной оси — так ведёт справочник федерация.</param>
/// <param name="AgeAxisSeasonOnly">Только по сезонной — как считаем возраст мы.</param>
/// <param name="AgeAxisNone">Ни по одной: протечка в старшую ступень или чужой однофамилец.</param>
/// <param name="AgeAxisMismatch">
/// Сколько проверенных ступеней НЕ сходится с текущей осью (<paramref name="AgeAxis"/>) —
/// то есть на скольких записях наша сверка попадёт не в ту строку справочника.
/// </param>
public sealed record RecordQualitySummary(
    int Total,
    int Found,
    int NotFound,
    int NotChecked,
    int FoundWrongDate,
    DateTime? LastCheckedAt,
    int IssuesOpen,
    int IssuesTotal,
    IReadOnlyList<RecordIssueDto> Issues,
    string AgeAxis = "calendar",
    int AgeAxisChecked = 0,
    int AgeAxisBoth = 0,
    int AgeAxisCalendarOnly = 0,
    int AgeAxisSeasonOnly = 0,
    int AgeAxisNone = 0,
    int AgeAxisMismatch = 0);

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
/// <param name="AgeAxisChecked">
/// У скольких найденных рекордов удалось проверить ступень: нужна возрастная категория
/// (age/masters), год рождения пловца и личная дисциплина (у эстафет ступень задаётся
/// составом, а не одним пловцом).
/// </param>
/// <param name="AgeAxisBoth">Ступень сходится по обеим осям — заплыв января–августа.</param>
/// <param name="AgeAxisCalendarOnly">Сходится только по календарной оси (ось федерации).</param>
/// <param name="AgeAxisSeasonOnly">Сходится только по сезонной (наш возраст в сезоне).</param>
/// <param name="AgeAxisNone">
/// Не сходится ни по одной. Не обязательно ошибка: чаще это протечка рекорда в старшую
/// ступень (время держится, пока не побили) либо совпадение времени у однофамильца.
/// </param>
public sealed record RecordVerifyResult(
    int Checked, int Found, int NotFound, int FoundWrongDate,
    int AgeAxisChecked = 0,
    int AgeAxisBoth = 0,
    int AgeAxisCalendarOnly = 0,
    int AgeAxisSeasonOnly = 0,
    int AgeAxisNone = 0);
