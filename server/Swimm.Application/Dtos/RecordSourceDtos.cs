namespace Swimm.Application.Dtos;

/// <summary>
/// Запрос на получение рекордов из внешнего источника (этап 2.6). Либо файлы приложены
/// (ручная загрузка — обязательный fallback для isr.org.il), либо провайдер качает сам
/// по своим (whitelist-only) URL-ам — <see cref="PrimaryStream"/> тогда null.
/// </summary>
public sealed record RecordSourceRequest(
    string Source,
    Stream? PrimaryStream = null,
    string? PrimaryFileName = null,
    Stream? SecondaryStream = null,
    string? SecondaryFileName = null,
    string? PoolType = null);

/// <summary>Один спарсенный рекорд, ещё не сопоставленный с БД (см. <see cref="Swimm.Domain.Entities.Record"/>).</summary>
public sealed record ParsedRecordDto(
    string RegionType,
    string RegionCode,
    string Category,
    string AgeKey,
    string Gender,
    string PoolType,
    string Style,
    string Distance,
    string Time,
    string? HolderName,
    string? Club,
    string? HolderCountry,
    string? RecordDate);

/// <summary>Одна строка диффа (новая или изменившаяся) — для таблицы превью в UI.</summary>
public sealed record RecordDiffEntry(
    string RegionType,
    string RegionCode,
    string Category,
    string AgeKey,
    string Gender,
    string PoolType,
    string Style,
    string Distance,
    string? OldTime,
    string? OldHolderName,
    string? OldRecordDate,
    string NewTime,
    string? NewHolderName,
    string? NewRecordDate,
    /// <summary>
    /// Значение из источника, которое Apply НЕ ВОЗЬМЁТ: человек уже завёл на него претензию
    /// в реестре со статусом из <c>RecordIssueStatuses.SourceOverruled</c> (И-25). Строка
    /// остаётся в диффе видимой — иначе «изменённых 1, применено 0» выглядело бы как баг.
    /// </summary>
    bool ProtectedByIssue = false);

/// <summary>
/// Результат сравнения фетча с текущими Records. missingInSource — только информационно,
/// ничего не удаляется. DiffId — ключ во временном кэше (10 мин) для последующего Apply.
/// </summary>
/// <param name="Suspicious">
/// Строки диффа с неправдоподобным временем (сторож импорта, docs/data-integrity.md И-20).
/// Apply их НЕ отбрасывает — наша копия обязана совпадать с источником, — а заводит в реестр
/// спорных рекордов кандидатами.
/// </param>
public sealed record RecordDiffResult(
    string DiffId,
    string Source,
    int AddedCount,
    int ChangedCount,
    int UnchangedCount,
    int MissingInSourceCount,
    IReadOnlyList<RecordDiffEntry> Added,
    IReadOnlyList<RecordDiffEntry> Changed,
    IReadOnlyList<RecordSuspiciousEntry>? Suspicious = null,
    /// <summary>Сколько строк диффа защищено реестром от перезаписи (И-25).</summary>
    int ProtectedCount = 0);

/// <summary>
/// Неправдоподобное время в диффе рекордов — кандидат в реестр спорных записей.
/// </summary>
/// <param name="Time">Новое (оспариваемое) время — оно же <c>FlaggedTime</c> будущей претензии.</param>
/// <param name="Reason">Код правила из <c>RecordIssueReasons</c>.</param>
/// <param name="Note">Обоснование человеческим языком — уйдёт в <c>RecordIssue.Note</c>.</param>
public sealed record RecordSuspiciousEntry(
    string RegionType,
    string RegionCode,
    string Category,
    string AgeKey,
    string Gender,
    string PoolType,
    string Style,
    string Distance,
    string Time,
    string Reason,
    string Note);

public sealed record RecordDiffApplyRequest(string DiffId, bool ApplyAdded, bool ApplyChanged);

/// <param name="CandidatesCreated">
/// Сколько подозрительных значений Apply завёл в реестр кандидатами (статус <c>candidate</c>).
/// Уже разобранные человеком претензии на то же значение не трогаются и сюда не входят.
/// </param>
public sealed record RecordDiffApplyResult(bool Success, string? Error, int AppliedCount,
    int CandidatesCreated = 0,
    /// <summary>Сколько значений источника Apply не взял из-за претензии в реестре (И-25).</summary>
    int ProtectedCount = 0);

/// <summary>Статус источника для карточки в UI: когда последний раз реально обновлялись его рекорды.</summary>
public sealed record RecordSourceStatusDto(string Source, DateTime? LastUpdatedAt);

/// <summary>
/// Ссылка на файл-справочник рекордов, найденная на странице источника. Нужна админке,
/// чтобы до нажатия «Fetch» было видно, ЧТО именно подтянется и от какого числа файл.
/// </summary>
/// <param name="Trusted">
/// false — подпись ссылки на сайте противоречит имени файла, автозагрузка её не берёт.
/// Админке это надо показать: иначе «файл не найден» выглядит поломкой у нас, а сломана
/// ссылка у федерации (docs/data-integrity.md, И-15).
/// </param>
public sealed record RecordSourceLinkDto(
    string Url,
    string Label,
    string PoolType,
    bool IsMasters,
    DateOnly? UpdatedOn,
    bool Trusted = true);

/// <summary>
/// Страна источника рекордов (11.1.1): чем её обозначает наша модель (<paramref name="Code"/>,
/// alpha-3 — он же <c>Record.RegionCode</c>) и чем — сам источник
/// (<paramref name="SourceId"/>, внутренний GUID, которым запрашиваются национальные рекорды).
///
/// Список тянется живым в начале прогона и в БД не хранится: GUID нужен только импорту,
/// а таблицу <c>Countries</c> (названия для витрины) он не трогает.
/// </summary>
/// <param name="Region">Континент источника. Пустой он ровно у псевдо-сборных
/// (AIN, EOR, FINA…), поэтому и служит признаком «это не страна» — в DTO попадают только
/// настоящие.</param>
public sealed record RecordCountryDto(string Code, string SourceId, string Name, string Region);

/// <summary>
/// Национальные рекорды одной страны, как их отдал источник (11.1.3 п. 2): что мы готовы
/// записать и что отбросили.
///
/// Отбрасываем не молча: <paramref name="Mismatches"/> — требование плана, а не отладка.
/// В режиме «все страны» <c>RegionCode</c> берётся ИЗ ЗАПРОСА, поэтому строка, которую
/// источник пометил чужой федерацией, не должна ни записаться под нашим кодом, ни исчезнуть
/// без следа: она едет в отчёт прогона.
/// </summary>
public sealed record RecordCountryFetchResult(
    string Code,
    IReadOnlyList<ParsedRecordDto> Records,
    IReadOnlyList<RecordCountryMismatchDto> Mismatches);

/// <summary>
/// Строка отчёта прогона, которую НЕ записали: код федерации в строке не равен
/// запрошенной стране. Пустой <c>NF Code</c> выглядит так же — парсер подставляет на его
/// место колонку <c>Country</c>, а это страна МЕСТА соревнования («Great Britain»), не
/// спортсмена; в режиме «все страны» такой фоллбек запрещён (план §3а).
/// </summary>
public sealed record RecordCountryMismatchDto(
    string RequestedCode,
    string ReportedCountry,
    string PoolType,
    string Style,
    string Distance,
    string Gender,
    string Time);
