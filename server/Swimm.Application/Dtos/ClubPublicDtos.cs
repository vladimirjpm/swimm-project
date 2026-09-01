using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>Строка ростера клуба — участник справочника (Swimmer.ClubId) с агрегатами за клуб.</summary>
public sealed class ClubRosterItemDto
{
    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = "";

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = "";

    [JsonPropertyName("last_name_en")]
    public string LastNameEn { get; set; } = "";

    [JsonPropertyName("first_name_en")]
    public string FirstNameEn { get; set; } = "";

    [JsonPropertyName("birth_year")]
    public int BirthYear { get; set; }

    /// <summary>
    /// Возраст В СЕЗОНЕ (сезон - BirthYear) — НЕ зачётная группа Category.
    /// null — год рождения не заполнен (в базе такие есть: 5 из 153 у клуба 438).
    /// Без этого возраст выходил равным номеру сезона («age 2025»).
    /// </summary>
    [JsonPropertyName("age")]
    public int? Age { get; set; }

    /// <summary>male | female | null (Swimmer.Gender не заполнен).</summary>
    [JsonPropertyName("gender")]
    public string? Gender { get; set; }

    /// <summary>Сколько РАЗНЫХ соревнований у пловца за этот клуб (в границах season, если задан).</summary>
    [JsonPropertyName("competitions")]
    public int Competitions { get; set; }

    /// <summary>Сколько заплывов у пловца за этот клуб (в границах season, если задан).</summary>
    [JsonPropertyName("swims")]
    public int Swims { get; set; }
}

/// <summary>Страница ростера клуба (догрузка по «Show all N»).</summary>
public sealed class ClubRosterPageDto
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("page_size")]
    public int PageSize { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("has_more")]
    public bool HasMore { get; set; }

    [JsonPropertyName("data")]
    public List<ClubRosterItemDto> Data { get; set; } = [];
}

/// <summary>
/// Клубный рекорд — лучшее время среди Results.ClubId клуба по оси
/// стиль × дистанция × бассейн × пол. 25m и 50m — РАЗНЫЕ рекорды, не объединяются.
/// </summary>
public sealed class ClubBestDto
{
    [JsonPropertyName("style_name")]
    public string StyleName { get; set; } = "";

    [JsonPropertyName("distance")]
    public string Distance { get; set; } = "";

    [JsonPropertyName("pool_type")]
    public string? PoolType { get; set; }

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = "";

    [JsonPropertyName("time_original")]
    public string TimeOriginal { get; set; } = "";

    /// <summary>Инвариант И11: DTO со временем несёт и качество. null — заплыв в порядке.</summary>
    [JsonPropertyName("suspect_reason")]
    public string? SuspectReason { get; set; }

    [JsonPropertyName("time_ms")]
    public int? TimeMs { get; set; }

    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    [JsonPropertyName("swimmer_name")]
    public string SwimmerName { get; set; } = "";

    [JsonPropertyName("swimmer_name_en")]
    public string SwimmerNameEn { get; set; } = "";

    [JsonPropertyName("competition_name")]
    public string CompetitionName { get; set; } = "";

    /// <summary>dd/MM/yyyy — формат дат соревнований во всех публичных ответах.</summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("points")]
    public int Points { get; set; }

    /// <summary>
    /// Ступень возраста: «8».. «18» по одному году (как в таблице возрастных рекордов
    /// федерации), «adults» — 19–24, «25-29».. — мастерс пятилетками, «n/a» — год
    /// рождения не заполнен. Возраст считается по-федерационному: год заплыва минус год
    /// рождения, а не по дате рождения.
    /// </summary>
    [JsonPropertyName("age_key")]
    public string AgeKey { get; set; } = "";

    /// <summary>Подпись ступени для UI: «age 10», «adults», «masters 45-49», «age n/a».</summary>
    [JsonPropertyName("age_label")]
    public string AgeLabel { get; set; } = "";

    /// <summary>Порядок ступени в ряду (по возрастанию возраста); «n/a» — в конец.</summary>
    [JsonPropertyName("age_order")]
    public int AgeOrder { get; set; }
}

/// <summary>
/// Секция карточки Season best: одна дисциплина (стиль × дистанция × бассейн × пол),
/// внутри — ступени, в которых пловец клуба ПЕРВЫЙ по стране в этом сезоне.
/// </summary>
public sealed class ClubSeasonBestGroupDto
{
    [JsonPropertyName("style_name")]
    public string StyleName { get; set; } = "";

    [JsonPropertyName("distance")]
    public string Distance { get; set; } = "";

    [JsonPropertyName("pool_type")]
    public string? PoolType { get; set; }

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = "";

    [JsonPropertyName("items")]
    public List<ClubBestDto> Items { get; set; } = [];
}

/// <summary>Ответ /api/clubs/{id}/season-best.</summary>
public sealed class ClubSeasonBestDto
{
    /// <summary>Год начала сезона, за который посчитано (карточка всегда про ОДИН сезон).</summary>
    [JsonPropertyName("season")]
    public int Season { get; set; }

    /// <summary>Метка сезона для заголовка: «2025/26».</summary>
    [JsonPropertyName("season_label")]
    public string SeasonLabel { get; set; } = "";

    /// <summary>Сколько всего плиток (сумма по секциям) — для бейджа счётчика.</summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }

    /// <summary>
    /// Сколько соревнований сезона вошло в расчёт лидерства. ⚠ Карточка ОБЯЗАНА показать
    /// эту цифру: «первый в Израиле» у нас означает «первый среди импортированного», а
    /// юниорских и взрослых чемпионатов в базе может не быть вовсе.
    /// </summary>
    [JsonPropertyName("meets")]
    public int Meets { get; set; }


    /// <summary>
    /// Новый сезон уже идёт, но витрина держит прошлый — пояснение «season best откроется
    /// после зимнего чемпионата» (docs/season-boundary-rule.md). null — объяснять нечего.
    /// </summary>
    [JsonPropertyName("season_notice")]
    public ShowcaseSeasonNoticeDto? SeasonNotice { get; set; }

    [JsonPropertyName("data")]
    public List<ClubSeasonBestGroupDto> Data { get; set; } = [];
}

/// <summary>Ответ /api/clubs/{id}/records.</summary>
public sealed class ClubRecordsDto
{
    [JsonPropertyName("data")]
    public List<ClubBestDto> Data { get; set; } = [];
}

/// <summary>
/// ОФИЦИАЛЬНЫЙ рекорд (таблица <c>Records</c>, импорт с isr.org.il / World Aquatics),
/// который числится за этим клубом. Это НЕ лучшее время клуба по нашим протоколам
/// (<see cref="ClubBestDto"/>) — здесь строка из внешнего справочника рекордов.
///
/// ⚠ У <c>Record</c> нет ни SwimmerId, ни ClubId — только текстовые <c>HolderName</c> и
/// <c>Club</c>. Поэтому связь с нашим клубом — совпадение НАЗВАНИЯ (см.
/// <c>ClubPublicRepository.GetRecordWallAsync</c>), ссылки на карточку пловца тут нет
/// и возраст держателя известен только как ступень (<see cref="AgeKey"/>).
/// </summary>
public sealed class ClubOfficialRecordDto
{
    /// <summary>world | continent | country — территория рекорда.</summary>
    [JsonPropertyName("region_type")]
    public string RegionType { get; set; } = "";

    /// <summary>ISR и т.п.; пусто для мировых.</summary>
    [JsonPropertyName("region_code")]
    public string RegionCode { get; set; } = "";

    /// <summary>open | age | masters (junior в данных не встречается).</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    /// <summary>
    /// Ступень: возраст «10».. «18» либо «adults» для age, «25-29».. для masters,
    /// пусто для open. Заменяет возраст пловца — точного возраста держателя у нас нет.
    /// </summary>
    [JsonPropertyName("age_key")]
    public string AgeKey { get; set; } = "";

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = "";

    [JsonPropertyName("pool_type")]
    public string PoolType { get; set; } = "";

    /// <summary>Ключ стиля строкой (в Records он не FK на Styles).</summary>
    [JsonPropertyName("style")]
    public string Style { get; set; } = "";

    /// <summary>С суффиксом «m»: «100m», «4X50m» (эстафеты тут есть).</summary>
    [JsonPropertyName("distance")]
    public string Distance { get; set; } = "";

    /// <summary>Время строкой как в источнике («21.08», «01:43.45») — миллисекунд в Records нет.</summary>
    [JsonPropertyName("time")]
    public string Time { get; set; } = "";

    /// <summary>
    /// Открытая претензия к записи справочника (<c>Sys_RecordIssues</c>): код причины,
    /// null — не оспаривается. Источник не правим, помечаем.
    /// </summary>
    [JsonPropertyName("issue_reason")]
    public string? IssueReason { get; set; }

    /// <summary>Держатель; у эстафет — четыре имени через запятую.</summary>
    [JsonPropertyName("holder_name")]
    public string HolderName { get; set; } = "";

    /// <summary>Название клуба КАК В ИСТОЧНИКЕ — по нему и склеивали (бывает с суффиксом).</summary>
    [JsonPropertyName("club")]
    public string Club { get; set; } = "";

    /// <summary>Дата из источника; формат смешанный, бывает пустой.</summary>
    [JsonPropertyName("record_date")]
    public string RecordDate { get; set; } = "";
}

/// <summary>Ответ /api/clubs/{id}/record-wall.</summary>
public sealed class ClubRecordWallDto
{
    /// <summary>Названия, по которым искали (клуб + его склеенные дубли) — для отладки и подписи.</summary>
    [JsonPropertyName("matched_names")]
    public List<string> MatchedNames { get; set; } = [];

    [JsonPropertyName("data")]
    public List<ClubOfficialRecordDto> Data { get; set; } = [];
}
