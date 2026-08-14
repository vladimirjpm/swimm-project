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
