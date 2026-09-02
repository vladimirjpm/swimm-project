using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Контракты страницы спортсмена (docs/plans/athlete-page-plan.md §3).
/// camelCase — как в остальных публичных DTO этой страницы.
///
/// ⚠ Чего здесь сознательно НЕТ:
/// • <c>isFavorite</c>/<c>isMe</c> — они у каждого зрителя свои, а ответ кэшируется и раздаётся
///   всем; клиент и так знает их из <c>useFavoritesContext</c>;
/// • <c>level</c> (разряд) — считается на клиенте из <c>NormativeStandard</c>
///   (<c>Helper.getNormativeLevelInfo</c>); вторая реализация на сервере разъедется с первой.
/// </summary>
public sealed class SwimmerSeasonOptionDto
{
    /// <summary>Год НАЧАЛА сезона (2025 → «2025/26»), он же значение <c>?season=</c>.</summary>
    [JsonPropertyName("season")]
    public int Season { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>Календарно текущий сезон.</summary>
    [JsonPropertyName("isCurrent")]
    public bool IsCurrent { get; set; }

    /// <summary>
    /// Сезон, на котором стоит карусель по умолчанию — ВИТРИННЫЙ: до зимних чемпионатов
    /// текущего сезона это прошлый сезон (docs/season-boundary-rule.md). Ровно один true.
    /// </summary>
    [JsonPropertyName("isDisplayDefault")]
    public bool IsDisplayDefault { get; set; }

    /// <summary>Заплывов в сезоне — подпись под годом в карусели.</summary>
    [JsonPropertyName("swims")]
    public int Swims { get; set; }
}

/// <summary>Зачётная группа возрастной лестницы (Kids/Young/Juniors/Adults/Masters).</summary>
public sealed class SwimmerAgeGroupDto
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>Буква для бейджа («K», «Y», «J»…).</summary>
    [JsonPropertyName("badge")]
    public string? Badge { get; set; }
}

public sealed class MedalCountsDto
{
    [JsonPropertyName("gold")]
    public int Gold { get; set; }

    [JsonPropertyName("silver")]
    public int Silver { get; set; }

    [JsonPropertyName("bronze")]
    public int Bronze { get; set; }
}

/// <summary>Соревнование в сводке сезона (таб Season) и в истории карьеры (таб History).</summary>
public sealed class SwimmerCompetitionDto
{
    /// <summary>Id для ссылки. У многодневки — первый день события.</summary>
    [JsonPropertyName("competitionId")]
    public int CompetitionId { get; set; }

    /// <summary>Событие многодневки: все дни делят один eventId и считаются ОДНИМ стартом.</summary>
    [JsonPropertyName("eventId")]
    public int? EventId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Дата первого дня, ISO.</summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    /// <summary>Единственный источник значка 🏆.</summary>
    [JsonPropertyName("isChampionship")]
    public bool IsChampionship { get; set; }

    /// <summary>Роль в сезоне: winter | summer | openwater | null (обычный старт).</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("poolType")]
    public string PoolType { get; set; } = string.Empty;

    /// <summary>pool | open — площадка. До появления Competition.WaterKind всегда «pool».</summary>
    [JsonPropertyName("waterKind")]
    public string WaterKind { get; set; } = "pool";

    [JsonPropertyName("swims")]
    public int Swims { get; set; }

    [JsonPropertyName("points")]
    public int Points { get; set; }

    [JsonPropertyName("medals")]
    public MedalCountsDto Medals { get; set; } = new();

    /// <summary>Лучшее протокольное место на этом старте; null — мест не было.</summary>
    [JsonPropertyName("bestPlace")]
    public int? BestPlace { get; set; }
}

/// <summary>Признак качества времени — то же, что понимает <c>UI_SwimTime</c> на клиенте.</summary>
public sealed class SwimQualityDto
{
    /// <summary>protocol — ошибка протокола федерации; record — спорная запись справочника.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "protocol";

    /// <summary>Код причины (manual, personal_outlier…).</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

/// <summary>
/// Строка таба Results: ОДНА дистанция — одно лучшее время за сезон.
/// Категория заплыва (open/para/U17) в ключ строки не входит: «лучшее на 50 вольным» —
/// это лучшее среди всех зачётов. В детекции личных рекордов категория, наоборот, участвует.
/// </summary>
public sealed class SwimmerBestTimeDto
{
    /// <summary>Ключ дисциплины — его же принимает <c>/progress</c>.</summary>
    [JsonPropertyName("disciplineKey")]
    public string DisciplineKey { get; set; } = string.Empty;

    [JsonPropertyName("styleId")]
    public int StyleId { get; set; }

    /// <summary>Ключ стиля как на клиенте (freestyle/backstroke/…).</summary>
    [JsonPropertyName("stroke")]
    public string? Stroke { get; set; }

    [JsonPropertyName("distance")]
    public string Distance { get; set; } = string.Empty;

    [JsonPropertyName("poolType")]
    public string PoolType { get; set; } = string.Empty;

    [JsonPropertyName("waterKind")]
    public string WaterKind { get; set; } = "pool";

    /// <summary>Время как в протоколе — его и показывает UI_SwimTime.</summary>
    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("timeMs")]
    public int? TimeMs { get; set; }

    /// <summary>null — вопросов к времени нет.</summary>
    [JsonPropertyName("quality")]
    public SwimQualityDto? Quality { get; set; }

    /// <summary>Очки FINA. null у помеченного времени — оно в зачёт не идёт.</summary>
    [JsonPropertyName("points")]
    public int? Points { get; set; }

    [JsonPropertyName("place")]
    public int? Place { get; set; }

    /// <summary>prelim / final / null (timed final или данные без признака). Место
    /// prelim-заплыва — ранжир сессии: клиент рисует его без медали.</summary>
    [JsonPropertyName("heatType")]
    public string? HeatType { get; set; }

    /// <summary>Возраст в сезоне заплыва — один на все старты сезона (SeasonMath.AgeInSeason).</summary>
    [JsonPropertyName("ageInSeason")]
    public int? AgeInSeason { get; set; }

    [JsonPropertyName("splits")]
    public string? Splits { get; set; }

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("competition")]
    public SwimmerCompetitionRefDto Competition { get; set; } = new();

    [JsonPropertyName("resultId")]
    public long ResultId { get; set; }

    /// <summary>Это же время — лучшее за всю карьеру, а не только за сезон.</summary>
    [JsonPropertyName("isCareerBest")]
    public bool IsCareerBest { get; set; }

    /// <summary>
    /// Заплыв был на МАСТЕРС-старте (<c>Competition.IsMasters</c>). Нужен разряду: у мастерса
    /// своя таблица нормативов с возрастными полосами, и без флага время 45-летней женщины
    /// меряется юношеской шкалой — 00:31.47 на 50 на спине даёт «первый взрослый» вместо МСМК.
    /// </summary>
    [JsonPropertyName("isMasters")]
    public bool IsMasters { get; set; }

}

/// <summary>
/// Официальный рекорд, который держит пловец, — секция над таблицей личников.
/// Это НЕ то же, что <c>holdsNationalAgeRecord</c> у личника: там сравнение по времени с
/// рекордом своей ступени, здесь — строка справочника, где держателем записан он сам.
/// </summary>
public sealed class SwimmerHeldRecordDto
{
    /// <summary>country | club | world — уровень рекорда.</summary>
    [JsonPropertyName("regionType")]
    public string RegionType { get; set; } = string.Empty;

    /// <summary>alpha-3 региона («ISR»).</summary>
    [JsonPropertyName("regionCode")]
    public string RegionCode { get; set; } = string.Empty;

    /// <summary>age | open | masters — ось справочника.</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>Возрастная ступень («12»); пусто у открытой категории.</summary>
    [JsonPropertyName("ageKey")]
    public string AgeKey { get; set; } = string.Empty;

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = string.Empty;

    [JsonPropertyName("poolType")]
    public string PoolType { get; set; } = string.Empty;

    /// <summary>Стиль СТРОКОЙ, как в справочнике (у Record нет StyleId).</summary>
    [JsonPropertyName("stroke")]
    public string Stroke { get; set; } = string.Empty;

    [JsonPropertyName("distance")]
    public string Distance { get; set; } = string.Empty;

    /// <summary>Время как напечатано в справочнике — его и показывает UI_SwimTime.</summary>
    [JsonPropertyName("time")]
    public string Time { get; set; } = string.Empty;

    /// <summary>Дата установления, как в справочнике (формат не гарантирован).</summary>
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    /// <summary>
    /// Качество САМОЙ записи справочника (kind = record). Инвариант И11: показано время —
    /// показан и признак его качества.
    /// </summary>
    [JsonPropertyName("quality")]
    public SwimQualityDto? Quality { get; set; }
}

/// <summary>Ссылка на соревнование в строке результата.</summary>
public sealed class SwimmerCompetitionRefDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("eventId")]
    public int? EventId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("isChampionship")]
    public bool IsChampionship { get; set; }
}

/// <summary>
/// Строка таба Records &amp; PB: личный рекорд за карьеру плюс две дельты.
/// ⚠ «Клубный рекорд» считается ПО НАШЕЙ БАЗЕ (лучшее время пловцов клуба), а не берётся из
/// справочника: у <c>Record</c> нет <c>ClubId</c>, и стена рекордов клуба связана с ним по
/// названию. Поэтому поле называется <c>clubBest</c>, а не <c>clubRecord</c>, и в UI обязана
/// быть подпись «among N meets in our database» (решение §6.3 плана).
/// </summary>
public sealed class SwimmerPersonalBestDto
{
    [JsonPropertyName("disciplineKey")]
    public string DisciplineKey { get; set; } = string.Empty;

    [JsonPropertyName("styleId")]
    public int StyleId { get; set; }

    [JsonPropertyName("stroke")]
    public string? Stroke { get; set; }

    [JsonPropertyName("distance")]
    public string Distance { get; set; } = string.Empty;

    [JsonPropertyName("poolType")]
    public string PoolType { get; set; } = string.Empty;

    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("timeMs")]
    public int? TimeMs { get; set; }

    [JsonPropertyName("quality")]
    public SwimQualityDto? Quality { get; set; }

    [JsonPropertyName("points")]
    public int? Points { get; set; }

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("competition")]
    public SwimmerCompetitionRefDto Competition { get; set; } = new();

    [JsonPropertyName("resultId")]
    public long ResultId { get; set; }

    /// <summary>Лучшее время клуба в этой дисциплине принадлежит ему самому.</summary>
    [JsonPropertyName("holdsClubBest")]
    public bool HoldsClubBest { get; set; }

    /// <summary>Отставание от лучшего в клубе, мс. null — сравнивать не с чем.</summary>
    [JsonPropertyName("deltaToClubBestMs")]
    public int? DeltaToClubBestMs { get; set; }

    /// <summary>Рекорд Израиля своего возраста принадлежит ему (сверка по времени, не по имени).</summary>
    [JsonPropertyName("holdsNationalAgeRecord")]
    public bool HoldsNationalAgeRecord { get; set; }

    /// <summary>Отставание от рекорда Израиля своего возраста, мс. null — рекорда нет в справочнике.</summary>
    [JsonPropertyName("deltaToNationalAgeRecordMs")]
    public int? DeltaToNationalAgeRecordMs { get; set; }

    /// <summary>Само время рекорда, как напечатано в справочнике.</summary>
    [JsonPropertyName("nationalAgeRecordTime")]
    public string? NationalAgeRecordTime { get; set; }

    /// <summary>
    /// Качество САМОГО рекорда (kind = record): справочник федерации тоже ошибается, и
    /// сравнивать с заведомо кривым рекордом, не сказав об этом, нельзя (инвариант И11).
    /// </summary>
    [JsonPropertyName("nationalAgeRecordQuality")]
    public SwimQualityDto? NationalAgeRecordQuality { get; set; }

    /// <summary>Возрастная ступень рекорда, с которой сравнивали («12»).</summary>
    [JsonPropertyName("nationalAgeKey")]
    public string? NationalAgeKey { get; set; }

    /// <summary>
    /// Ступень справочника, которой мерили: «age 14», «masters 45-49», «open». Нужна подписи:
    /// у взрослых эталон — мастерская полоса или ОТКРЫТЫЙ рекорд страны, и «national age
    /// record» там читалось бы неправдой. null — сравнивать было не с чем.
    /// </summary>
    [JsonPropertyName("nationalRecordScope")]
    public string? NationalRecordScope { get; set; }
}

/// <summary>Точка графика прогресса — один заплыв в выбранной дисциплине.</summary>
public sealed class SwimmerProgressPointDto
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("timeMs")]
    public int? TimeMs { get; set; }

    /// <summary>Заплыв был личным рекордом НА МОМЕНТ старта (повтор времени — не рекорд).</summary>
    [JsonPropertyName("isPb")]
    public bool IsPb { get; set; }

    /// <summary>null — вопросов к времени нет; помеченные точки в линию не входят.</summary>
    [JsonPropertyName("quality")]
    public SwimQualityDto? Quality { get; set; }

    [JsonPropertyName("points")]
    public int? Points { get; set; }

    [JsonPropertyName("place")]
    public int? Place { get; set; }

    /// <summary>prelim / final / null — место prelim-заплыва рисуется без медали.</summary>
    [JsonPropertyName("heatType")]
    public string? HeatType { get; set; }

    [JsonPropertyName("ageInSeason")]
    public int? AgeInSeason { get; set; }

    /// <summary>Мастерс-старт: у разряда своя таблица нормативов (см. SwimmerBestTimeDto).</summary>
    [JsonPropertyName("isMasters")]
    public bool IsMasters { get; set; }

    [JsonPropertyName("season")]
    public int Season { get; set; }

    [JsonPropertyName("competition")]
    public SwimmerCompetitionRefDto Competition { get; set; } = new();

    [JsonPropertyName("resultId")]
    public long ResultId { get; set; }
}

/// <summary>
/// Таб Progress: история ВСЕХ заплывов одной связки стиль+дистанция+бассейн по возрастанию
/// даты. Ранги сравнимы между стартами, очки — нет: у каждого старта своё правило начисления,
/// и переключатель «очки» в UI обязан нести эту сноску.
/// </summary>
public sealed class SwimmerProgressDto
{
    [JsonPropertyName("disciplineKey")]
    public string DisciplineKey { get; set; } = string.Empty;

    [JsonPropertyName("styleId")]
    public int StyleId { get; set; }

    [JsonPropertyName("stroke")]
    public string? Stroke { get; set; }

    [JsonPropertyName("distance")]
    public string Distance { get; set; } = string.Empty;

    [JsonPropertyName("poolType")]
    public string PoolType { get; set; } = string.Empty;

    [JsonPropertyName("points")]
    public List<SwimmerProgressPointDto> Points { get; set; } = [];
}

/// <summary>KPI-плитки, шапка панели и содержимое табов Season/History.</summary>
public sealed class SwimmerSummaryDto
{
    /// <summary>Год начала сезона либо null при <c>?season=all</c>.</summary>
    [JsonPropertyName("season")]
    public int? Season { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>Сумма очков FINA. Они сравнимы между стартами — в отличие от клубных очков.</summary>
    [JsonPropertyName("points")]
    public int Points { get; set; }

    [JsonPropertyName("medals")]
    public MedalCountsDto Medals { get; set; } = new();

    /// <summary>Заплывов, включая эстафетные ноги.</summary>
    [JsonPropertyName("swims")]
    public int Swims { get; set; }

    /// <summary>Дисциплин (стиль × дистанция × бассейн × категория), в которых были старты.</summary>
    [JsonPropertyName("events")]
    public int Events { get; set; }

    /// <summary>Соревнований: многодневка считается одним стартом.</summary>
    [JsonPropertyName("competitionCount")]
    public int CompetitionCount { get; set; }

    /// <summary>Личных рекордов, поставленных в этом сезоне.</summary>
    [JsonPropertyName("personalBests")]
    public int PersonalBests { get; set; }

    /// <summary>
    /// Старты сезона от новых к старым. При <c>?season=all</c> — вся карьера, это и есть
    /// содержимое таба History (отдельного эндпоинта у него нет).
    /// </summary>
    [JsonPropertyName("competitions")]
    public List<SwimmerCompetitionDto> Competitions { get; set; } = [];
}

/// <summary>
/// Фильтр «Season best» страницы спортсмена: где пловец стоит СРЕДИ СВЕРСТНИКОВ — пловцов
/// того же года рождения и того же пола — по лучшим временам выбранного сезона.
///
/// Ответ намеренно НЕ повторяет строки результатов: клиент уже держит их из
/// <c>/best-times</c> за тот же сезон и склеивает по <c>disciplineKey</c>. Второй набор тех же
/// строк означал бы два места, где «лучшее время сезона» определяется заново, — ровно та
/// ошибка, ради которой заведён <see cref="Swimm.Application.Mapping.SwimmerPageBuilder"/>.
/// </summary>
public sealed class SwimmerSeasonRankDto
{
    /// <summary>Год начала сезона; null — сезон не выбран (режим карьеры), места не считаются.</summary>
    [JsonPropertyName("season")]
    public int? Season { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>Возраст В СЕЗОНЕ (SeasonMath.AgeInSeason). null — года рождения нет в базе.</summary>
    [JsonPropertyName("age")]
    public int? Age { get; set; }

    /// <summary>male | female | null — пол, по которому собрана группа сверстников.</summary>
    [JsonPropertyName("gender")]
    public string? Gender { get; set; }

    /// <summary>Готовая подпись группы («girls 9»): её обязан показать UI рядом с местом.</summary>
    [JsonPropertyName("groupLabel")]
    public string? GroupLabel { get; set; }

    /// <summary>
    /// Новый сезон уже идёт, но витрина держит прошлый — пояснение «season best откроется
    /// после зимнего чемпионата» (docs/season-boundary-rule.md). null — объяснять нечего.
    /// </summary>
    [JsonPropertyName("season_notice")]
    public ShowcaseSeasonNoticeDto? SeasonNotice { get; set; }

    [JsonPropertyName("rows")]
    public List<SwimmerDisciplineRankDto> Rows { get; set; } = [];
}

/// <summary>
/// Место в одной дисциплине среди сверстников. Ключ тот же, что у строки <c>/best-times</c>.
/// Равные времена делят место (спортивный ранжир): двое по 41.23 — оба вторые, следующий четвёртый.
/// </summary>
public sealed class SwimmerDisciplineRankDto
{
    [JsonPropertyName("disciplineKey")]
    public string DisciplineKey { get; set; } = string.Empty;

    /// <summary>Место среди сверстников, 1 — быстрейший в группе (тогда же и бейдж SB).</summary>
    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    /// <summary>Сколько сверстников вообще плавало эту дисциплину в сезоне (включая самого).</summary>
    [JsonPropertyName("peerCount")]
    public int PeerCount { get; set; }

    /// <summary>Время пловца, мс — то же, что в строке /best-times; здесь для самопроверки клиента.</summary>
    [JsonPropertyName("timeMs")]
    public int TimeMs { get; set; }

    /// <summary>Лучшее время группы, мс.</summary>
    [JsonPropertyName("leaderTimeMs")]
    public int LeaderTimeMs { get; set; }

    /// <summary>Отставание от лидера группы, мс. 0 — он сам лидер.</summary>
    [JsonPropertyName("gapToLeaderMs")]
    public int GapToLeaderMs { get; set; }
}

/// <summary>
/// Строка публичного поиска пловцов (<c>GET /api/swimmers/search</c>) — селектор соперника
/// таба H2H. Имя приходит УЖЕ выбранным по правилу проекта: иврит, английский — только
/// фоллбеком; клиенту решать это заново нечем (у него нет обеих пар полей).
/// </summary>
public sealed class SwimmerSearchHitDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Год рождения; 0 — в базе не заполнен (у пловцов из стартовых протоколов бывает).</summary>
    [JsonPropertyName("birthYear")]
    public int BirthYear { get; set; }

    [JsonPropertyName("gender")]
    public string? Gender { get; set; }

    [JsonPropertyName("clubName")]
    public string? ClubName { get; set; }
}

/// <summary>
/// Таб H2H: лучшие времена двух пловцов бок о бок за один период. Соперник выбирается
/// вручную через поиск — автоматического списка «кто рядом» здесь нет (решение Влада
/// 2026-09-01), поэтому и когорта сверстников не тянется.
///
/// Период — тот же, что у остальных табов: сезон карусели либо ∞ (карьера).
/// </summary>
public sealed class SwimmerCompareDto
{
    /// <summary>Год начала сезона; null — сравнение за карьеру.</summary>
    [JsonPropertyName("season")]
    public int? Season { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("mine")]
    public SwimmerCompareSideDto Mine { get; set; } = new();

    [JsonPropertyName("rival")]
    public SwimmerCompareSideDto Rival { get; set; } = new();

    /// <summary>
    /// Дистанции, где плавал хотя бы один из двоих: строка на связку стиль × дистанция,
    /// бассейны внутри неё. Сначала те, где есть что сравнивать, дальше односторонние.
    /// </summary>
    [JsonPropertyName("rows")]
    public List<SwimmerCompareRowDto> Rows { get; set; } = [];

    /// <summary>
    /// Сколько пар «дистанция × бассейн» плавали ОБА — заголовок панели говорит это вслух.
    /// Считается по бассейнам, а не по строкам: 50 брасс в 25м и в 50м — два разных
    /// сравнения, и схлопнуть их в одно значило бы потерять половину результата.
    /// </summary>
    [JsonPropertyName("sharedCount")]
    public int SharedCount { get; set; }

    /// <summary>Из общих пар: где быстрее хозяин страницы / соперник / поровну.</summary>
    [JsonPropertyName("mineFaster")]
    public int MineFaster { get; set; }

    [JsonPropertyName("rivalFaster")]
    public int RivalFaster { get; set; }

    [JsonPropertyName("ties")]
    public int Ties { get; set; }

    /// <summary>
    /// Сезон, за который посчитаны SB. В режиме ∞ он НЕ равен периоду сравнения: места среди
    /// сверстников живут внутри сезона, поэтому за карьеру они считаются за витринный — и
    /// UI обязан сказать это подписью, иначе цифра выглядит как «за всё время».
    /// </summary>
    [JsonPropertyName("seasonBestSeason")]
    public int? SeasonBestSeason { get; set; }

    [JsonPropertyName("seasonBestLabel")]
    public string? SeasonBestLabel { get; set; }
}

/// <summary>Шапка одной стороны сравнения.</summary>
public sealed class SwimmerCompareSideDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("birthYear")]
    public int? BirthYear { get; set; }

    [JsonPropertyName("gender")]
    public string? Gender { get; set; }

    [JsonPropertyName("clubName")]
    public string? ClubName { get; set; }

    /// <summary>Возраст В СЕЗОНЕ сравнения (SeasonMath.AgeInSeason); null — за карьеру или без года рождения.</summary>
    [JsonPropertyName("ageInSeason")]
    public int? AgeInSeason { get; set; }

    // ── Статы шапки (макет H2H, три строки между линиями) ──────────────────────
    // Считаются за ТОТ ЖЕ период, что и времена ниже: иначе шапка и таблица говорили бы
    // о разном, а подписи, объясняющей расхождение, в макете нет.

    /// <summary>
    /// Сколько дисциплин пловец возглавляет среди СВЕРСТНИКОВ (тот же год рождения и пол).
    /// За карьеру не считается — сравнение живёт внутри сезона, и в режиме ∞ здесь 0.
    /// </summary>
    [JsonPropertyName("seasonBests")]
    public int SeasonBests { get; set; }

    /// <summary>Медали периода — только там, где их вручали (<c>Competition.IsAward</c>).</summary>
    [JsonPropertyName("medals")]
    public MedalCountsDto Medals { get; set; } = new();

    /// <summary>Лучшие очки FINA за один заплыв периода. 0 — зачётных заплывов нет.</summary>
    [JsonPropertyName("bestPoints")]
    public int BestPoints { get; set; }
}

/// <summary>
/// Одна дистанция в сравнении — <b>стиль × дистанция</b>, бассейны внутри (25м и 50м своими
/// парами времён). Ключ строится БЕЗ пола (в отличие от <c>disciplineKey</c> остальных
/// табов): иначе у разнополой пары не совпала бы ни одна строка и таблица вышла бы пустой
/// при полном наборе данных с обеих сторон.
///
/// Почему бассейн ВНУТРИ строки, а не в её ключе: «50 брасс» — одна дистанция, и двумя
/// строками она читалась бы как две разные. Но сравнивать 25м с 50м нельзя — в короткой воде
/// время быстрее по устройству бассейна, — поэтому пары времён остаются раздельными, каждая
/// со своим разрывом.
/// </summary>
public sealed class SwimmerCompareRowDto
{
    /// <summary>стиль|дистанция — общий ключ строки; ни бассейну, ни полу здесь не место.</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("styleId")]
    public int StyleId { get; set; }

    [JsonPropertyName("stroke")]
    public string? Stroke { get; set; }

    [JsonPropertyName("distance")]
    public string Distance { get; set; } = string.Empty;

    /// <summary>
    /// Бассейны, где эту дистанцию плавал хотя бы один из двоих: 25м впереди 50м, пустых
    /// (никто не плавал) в списке нет.
    /// </summary>
    [JsonPropertyName("pools")]
    public List<SwimmerComparePoolDto> Pools { get; set; } = [];
}

/// <summary>Пара времён одной дистанции в ОДНОМ бассейне — единица сравнения.</summary>
public sealed class SwimmerComparePoolDto
{
    [JsonPropertyName("poolType")]
    public string PoolType { get; set; } = string.Empty;

    [JsonPropertyName("mine")]
    public SwimmerCompareSwimDto? Mine { get; set; }

    [JsonPropertyName("rival")]
    public SwimmerCompareSwimDto? Rival { get; set; }

    /// <summary>
    /// Разрыв в мс, «моё минус соперника»: отрицательный — хозяин страницы быстрее.
    /// null — в этом бассейне дистанцию плавал только один, сравнивать не с чем.
    /// </summary>
    [JsonPropertyName("deltaMs")]
    public int? DeltaMs { get; set; }
}

/// <summary>Лучший заплыв одной стороны на одной дистанции за период сравнения.</summary>
public sealed class SwimmerCompareSwimDto
{
    /// <summary>Время как напечатано в протоколе — его и показывает UI_SwimTime.</summary>
    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("timeMs")]
    public int? TimeMs { get; set; }

    /// <summary>
    /// Признак качества времени (инвариант И11). Сегодня он всегда пуст: в сравнение идут
    /// только зачётные заплывы, а помеченные отсекает <c>IsCountable</c>. Поле всё равно
    /// есть и заполняется из того же места, что везде, — ослабнет отбор, и признак поедет
    /// на витрину сам, а не «когда вспомнят».
    /// </summary>
    [JsonPropertyName("quality")]
    public SwimQualityDto? Quality { get; set; }

    [JsonPropertyName("points")]
    public int? Points { get; set; }

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("competition")]
    public SwimmerCompetitionRefDto? Competition { get; set; }

    [JsonPropertyName("resultId")]
    public long ResultId { get; set; }

    /// <summary>
    /// Бейдж SB: пловец быстрейший среди сверстников в этой дисциплине. Порог «хотя бы двое
    /// в группе» тот же, что на остальных экранах: «первый среди одного» — не достижение.
    /// </summary>
    [JsonPropertyName("isSeasonBest")]
    public bool IsSeasonBest { get; set; }

    /// <summary>
    /// Бейдж REC: время не медленнее официального рекорда страны для СВОЕЙ возрастной
    /// ступени. Определяется по времени, а не по имени держателя, — имена в справочнике
    /// строковые и у тёзок совпадают (то же правило, что в личниках).
    /// </summary>
    [JsonPropertyName("holdsRecord")]
    public bool HoldsRecord { get; set; }
}
