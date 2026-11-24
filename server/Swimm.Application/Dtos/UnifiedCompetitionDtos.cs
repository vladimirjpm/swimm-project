namespace Swimm.Application.Dtos;

/// <summary>
/// Стадия жизненного цикла соревнования в объединённом списке (Competitions + Discovery).
/// Соревнование — одна реальная сущность, живущая в двух источниках: список isr.org.il
/// (<c>Sys_DiscoveredCompetitions</c>) и наша БД (<c>Competitions</c>), связка — OrgCompId.
/// </summary>
public enum CompetitionStage
{
    /// <summary>Есть на сайте (Discovery), в БД ещё нет — ждёт «Затянуть»/импорт.</summary>
    OnSite,
    /// <summary>Есть и на сайте, и в БД, связано по OrgCompId (или имя+дата).</summary>
    Imported,
    /// <summary>Есть только в БД (PDF-импорт без OrgCompId — напр. Маккабиада).</summary>
    DbOnly,
    /// <summary>Discovery-строка скрыта (ignored), в БД её нет.</summary>
    Ignored
}

/// <summary>Результат объединённого списка: страница строк + счётчики по месяцам (для фильтр-кнопок).</summary>
public sealed record UnifiedCompetitionList(
    PagedResult<UnifiedCompetitionRowDto> Page,
    /// <summary>Число соревнований в каждом месяце (индекс 0 = январь … 11 = декабрь) под текущими
    /// фильтрами, но БЕЗ фильтра по месяцу — чтобы на кнопках были полные счётчики.</summary>
    IReadOnlyList<int> MonthCounts,
    /// <summary>Сколько строк спрятал фильтр дисциплины (артистическое плавание и прочее).
    /// Показывается чипом «скрыто N»: молча пропавшие строки читаются как потеря данных.</summary>
    int HiddenByDiscipline = 0,
    /// <summary>Из <see cref="MonthCounts"/> — сколько уже затянуто в БД (строка есть в справочнике).</summary>
    IReadOnlyList<int>? MonthImported = null,
    /// <summary>Счётчики по сезонам под текущими фильтрами, но БЕЗ фильтров сезона и месяца —
    /// чипы-сезоны над списком (сезон, всего, затянуто). Порядок — от свежего к старому.</summary>
    IReadOnlyList<SeasonCountDto>? SeasonCounts = null,
    /// <summary>Из <see cref="MonthCounts"/> — сколько НЕЛЬЗЯ затянуть (нечего брать: пустой
    /// протокол или результаты не опубликованы). Без этого «12 из 14» читается как долг.</summary>
    IReadOnlyList<int>? MonthNothingToPull = null,
    /// <summary>Состояния (хэндофф «вариант E», раздел 2) по месяцам: индекс 0 = январь, под теми же
    /// фильтрами, что <see cref="MonthCounts"/> (с сезоном, без месяца). <c>Total</c> = <see cref="MonthCounts"/>.</summary>
    IReadOnlyList<Mapping.CompetitionStateCounts>? MonthStates = null,
    /// <summary>Состояния всего списка под текущими фильтрами (с месяцем), но БЕЗ фильтра
    /// <c>?state=</c> — числа переключателя «Все / Ждут / Впереди / Закрыто».</summary>
    Mapping.CompetitionStateCounts? ListStates = null);

/// <summary>
/// Сезон (год окончания, 2026 = 25/26) со счётчиками. Три числа, а не два: «затянуто»,
/// «тянуть нечего» (пустой протокол / результаты не опубликованы) и всего. Разница
/// <c>Total − Imported − NothingToPull</c> — единственное, что реально ждёт работы.
/// </summary>
public sealed record SeasonCountDto(int Season, int Total, int Imported, int NothingToPull = 0)
{
    /// <summary>Состояния строк сезона — для полосы сезонов (закрыто / долг / впереди).
    /// Инвариант: <c>States.Total == Total</c>.</summary>
    public Mapping.CompetitionStateCounts States { get; init; } = Mapping.CompetitionStateCounts.Zero;

    /// <summary>Сезон из состояний его строк — единственный способ собрать счётчик: «затянуто» и
    /// «тянуть нечего» — разбивка «закрыто», а не отдельный подсчёт.</summary>
    public static SeasonCountDto FromStates(int season, Mapping.CompetitionStateCounts states) =>
        new(season, states.Total, states.ClosedInDb, states.ClosedNothingToPull) { States = states };

    /// <summary>Сколько ещё можно затянуть. 0 — сезон закрыт, показываем ✓.</summary>
    public int Pending => Math.Max(0, Total - Imported - NothingToPull);
}

/// <summary>Discovery-сторона объединённой строки: данные с isr.org.il.</summary>
public sealed class UnifiedSiteInfo
{
    /// <summary>Вид спорта строки с сайта (проставлен при обнаружении, правится в админке).</summary>
    public string Discipline { get; set; } = "swimming";

    public int DiscoveredId { get; set; }
    public int OrgCompId { get; set; }
    public string Name { get; set; } = "";
    public DateTime DateStart { get; set; }
    public DateTime DateEnd { get; set; }
    public string? Venue { get; set; }
    /// <summary>new | imported | ignored (сырой статус Discovery-строки).</summary>
    public string Status { get; set; } = "";
    /// <summary>Языки загруженных PDF: null | "he" | "en" | "he,en".</summary>
    public string? Languages { get; set; }
    public string? LastError { get; set; }

    /// <summary>Протокола нет: PDF пуст (см. DiscoveredCompetition.EmptySourceAt). Не ошибка —
    /// факт «тянуть нечего», поэтому строка помечается, а не прячется.</summary>
    public DateTime? EmptySourceAt { get; set; }

    public int? LogligId { get; set; }

    /// <summary>
    /// Явный признак исхода «на странице федерации нет loglig-iframe» (не сбой сети/таймаут/разбор).
    /// Админка по нему пишет «Протокола нет и не будет» и мигает «Нет протокола».
    /// </summary>
    public bool NoLogligIframe => LogligId is null && LastError == DiscoveryErrors.NoLogligIframe;

    /// <summary>
    /// Вторая ступень: «нет loglig-iframe» И с последнего дня старта прошло не меньше
    /// <see cref="NoLogligIframeRules.FinalAfterDays"/> дней — «Протокола нет и не будет» (ярко, кнопка
    /// мигает). Раньше — «Протокола пока нет» (спокойно). Считает репозиторий списка.
    /// </summary>
    public bool NoLogligIframeFinal { get; set; }

    /// <summary>
    /// Стартовый протокол уже затягивали — бейдж из журнала <c>Sys_StartListPulls</c>
    /// (<c>StartListBadgeRules</c>). null — ни разу. Заполняется только у строк текущей страницы.
    /// </summary>
    public Mapping.StartListBadge? StartList { get; set; }

    /// <summary>Вердикт results-auto (П5, <c>ResultsAutoVerdicts</c>); null — задача строку не смотрела.</summary>
    public string? AutoPullVerdict { get; set; }

    /// <summary>Причина «ждёт тебя» / что ещё ждёт — для чипа.</summary>
    public string? AutoPullBlockedReason { get; set; }

    public DateTime? AutoPullCheckedAt { get; set; }

    /// <summary>Длина бассейна, с которой задача взяла бы старт (у would-take).</summary>
    public string? AutoPullPoolType { get; set; }

    /// <summary>Перенос: первая объявленная дата начала (null — переноса нет или его отменили).</summary>
    public DateTime? PreviousDateStart { get; set; }

    /// <summary>Пропал из списка isr.org.il (вероятно, отменён) — с какого момента; null — на месте.</summary>
    public DateTime? MissingSince { get; set; }

    /// <summary>Когда забор «Входящих» впервые нашёл старт у федерации — шаг 1 чек-листа панели.</summary>
    public DateTime DiscoveredAt { get; set; }

    /// <summary>
    /// Сколько дней старта с заданной разминкой (<c>CompetitionWarmUps</c> по OrgCompId) — шаг 3
    /// чек-листа. Заполняется только у строк текущей страницы, как <see cref="StartList"/>.
    /// </summary>
    public int WarmUpDays { get; set; }
}

/// <summary>
/// Строка объединённого списка: БД-сторона (соревнование или свёрнутое событие) и/или
/// Discovery-сторона. Хотя бы одна из <see cref="Db"/>/<see cref="Site"/> не null.
/// </summary>
public sealed class UnifiedCompetitionRowDto
{
    public CompetitionStage Stage { get; set; }

    /// <summary>БД-сторона (одиночное соревнование или свёрнутое многодневное событие).
    /// null — строка существует только на сайте (<see cref="CompetitionStage.OnSite"/>/<see cref="CompetitionStage.Ignored"/>).</summary>
    public CompetitionRowDto? Db { get; set; }

    /// <summary>Discovery-сторона. null — соревнование только в БД
    /// (<see cref="CompetitionStage.DbOnly"/>).</summary>
    public UnifiedSiteInfo? Site { get; set; }

    /// <summary>Ключ сортировки по дате (для единого date-desc порядка списка).</summary>
    public DateTime SortDate { get; set; }

    /// <summary>Чемпионат Израиля (по названию любой стороны строки) — иконка 🏆 и фильтр «Тип».</summary>
    public bool IsChampionship { get; set; }

    /// <summary>
    /// Вид спорта строки: swimming | artistic | other. У строки в БД берётся из
    /// <c>Competition.Discipline</c>, у «только на сайте» — из
    /// <c>Sys_DiscoveredCompetitions.Discipline</c>; и то и другое проставлено при
    /// обнаружении/импорте, а не угадывается на каждый показ.
    /// </summary>
    public string Discipline { get; set; } = "swimming";

    /// <summary>
    /// Рабочее состояние (впереди / идёт / ждёт затягивания / ждём протокол / закрыто) с причиной
    /// и пометками — <see cref="Mapping.CompetitionRowStateRules.Evaluate"/>. Репозиторий
    /// заполняет его у всех строк до счётчиков.
    /// </summary>
    public Mapping.CompetitionRowStatus? Status { get; set; }

    /// <summary>Ключ строки (<c>?sel=</c>, раскрытие панели) — <see cref="Mapping.CompetitionRowKey"/>.</summary>
    public string Key => Mapping.CompetitionRowKey.Of(this);

    /// <summary>
    /// Открытые находки Health (Warning/Error) по соревнованию и его строкам — по проверкам, у события —
    /// сумма по дням (<see cref="Mapping.CompetitionProblemRules.Merge"/>). Находки по пловцу не входят.
    /// Репозиторий заполняет у всех строк (один запрос на список).
    /// </summary>
    public IReadOnlyList<CompetitionHealthCount> Health { get; set; } = [];

    /// <summary>
    /// Первые открытые находки Health строки (не больше <see cref="CompetitionHealthFinding.PerRowLimit"/>) —
    /// текст для блока «Что не так». Только у строк страницы.
    /// </summary>
    public IReadOnlyList<CompetitionHealthFinding> HealthFindings { get; set; } = [];

    /// <summary>
    /// Последняя доклейка промежуточных хоть у одного дня не удалась или прошла частично
    /// (<see cref="Mapping.SplitAutoAttachRules.NeedsRetry"/>). У строк страницы — всегда, у остальных —
    /// только при фильтре «С ошибками».
    /// </summary>
    public bool SplitsNeedRetry { get; set; }
}

/// <summary>Открытых находок одной проверки одной severity у строки списка.</summary>
public sealed record CompetitionHealthCount(string CheckId, int Severity, int Count);

/// <summary>Открытая находка Health для блока «Что не так» карточки соревнования.</summary>
public sealed record CompetitionHealthFinding(int Id, string CheckId, int Severity, string Message, string? Link)
{
    /// <summary>Сколько находок строки показывать текстом; остальное — числом и ссылкой в Health.</summary>
    public const int PerRowLimit = 8;
}
