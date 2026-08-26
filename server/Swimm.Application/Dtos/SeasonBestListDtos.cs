using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Запрос списка «лучшие в сезоне» — страница <c>/season-best</c>.
///
/// Отдельный объект, а не восемь параметров метода: фильтр тут ровно тот, что лежит в query
/// адреса страницы (см. <c>client/src/utils/routes.ts</c>, <c>routes.seasonBest</c>), и держать
/// его одной сущностью дешевле, чем синхронизировать длинную сигнатуру в трёх слоях.
/// </summary>
public sealed class SeasonBestListQuery
{
    /// <summary>Как в <c>Styles.Name</c>: freestyle / backstroke / …</summary>
    public string Style { get; set; } = "";

    /// <summary>Как в <c>Results.Distance</c> — без «m»: «50», «100».</summary>
    public string Distance { get; set; } = "";

    /// <summary>«25m» / «50m»; null — оба бассейна в одной выборке.</summary>
    public string? PoolType { get; set; }

    /// <summary>Год НАЧАЛА сезона; null — текущий.</summary>
    public int? Season { get; set; }

    /// <summary>Возраст В СЕЗОНЕ (нижняя граница, если задан <see cref="AgeTo"/>).</summary>
    public int? Age { get; set; }

    /// <summary>Верхняя граница возраста включительно; null — ровно <see cref="Age"/>.</summary>
    public int? AgeTo { get; set; }

    /// <summary>
    /// <c>true</c> — срез по МАСТЕРСКИМ стартам (<c>Competition.IsMasters</c>), <c>false</c> —
    /// по обычным. Две выборки не смешиваются никогда: у мастерсов свои соревнования и своя
    /// система возрастов, и общий список смешал бы 12-летних с 47-летними.
    /// </summary>
    public bool Masters { get; set; }

    /// <summary>
    /// Возрастная ГРУППА протокола («25-29»), только для <see cref="Masters"/>. У мастерсов
    /// ровесники — это группа, а не год: место «среди 34-летних» бессмысленно, потому что
    /// плывут и считаются они в пятилетках. При <see cref="Masters"/> поля
    /// <see cref="Age"/>/<see cref="AgeTo"/> игнорируются.
    /// </summary>
    public string? AgeGroup { get; set; }

    /// <summary>«male» / «female»; null — оба.</summary>
    public string? Gender { get; set; }

    /// <summary>Клуб пловца НА МОМЕНТ ЗАПЛЫВА (<c>Results.ClubId</c>); null — все.</summary>
    public int? ClubId { get; set; }

    /// <summary>
    /// <c>false</c> — все заплывы подряд (один пловец может занять и первое, и третье место);
    /// <c>true</c> — по одному лучшему заплыву на пловца.
    ///
    /// Умолчание — все заплывы (решение Влада: «если первые два результата у одного пловца,
    /// так и писать»).
    /// </summary>
    public bool BestPerSwimmer { get; set; }

    public int Limit { get; set; } = 50;

    public int Offset { get; set; }
}

/// <summary>
/// Ранжированный список одной дисциплины за сезон: кто быстрее всех в связке
/// «стиль × дистанция × бассейн», с фильтрами по возрасту, полу и клубу.
///
/// ⚠ Не путать с <see cref="SeasonBestNationalDto"/>: тот отвечает на вопрос «кто лидер в
/// каждой ступени возраста» (одна строка на пару пол × возраст) и питает таб рядом с
/// возрастными рекордами. Здесь — сам список внутри ОДНОЙ ступени.
/// </summary>
public sealed class SeasonBestListDto
{
    /// <summary>Год НАЧАЛА сезона: 2025 = сезон 2025/26.</summary>
    [JsonPropertyName("season")]
    public int Season { get; set; }

    [JsonPropertyName("season_label")]
    public string SeasonLabel { get; set; } = "";

    [JsonPropertyName("style")]
    public string Style { get; set; } = "";

    [JsonPropertyName("distance")]
    public string Distance { get; set; } = "";

    /// <summary>Бассейн фильтра: «25m» / «50m» / null — оба (тогда он свой у каждой строки).</summary>
    [JsonPropertyName("pool_type")]
    public string? PoolType { get; set; }

    [JsonPropertyName("gender")]
    public string? Gender { get; set; }

    [JsonPropertyName("age")]
    public int? Age { get; set; }

    [JsonPropertyName("age_to")]
    public int? AgeTo { get; set; }

    /// <summary>Срез по мастерским стартам (тогда ось возраста — группы, а не годы).</summary>
    [JsonPropertyName("masters")]
    public bool Masters { get; set; }

    /// <summary>Возрастная группа среза («25-29»); null — группа не выбрана.</summary>
    [JsonPropertyName("age_group")]
    public string? AgeGroup { get; set; }

    [JsonPropertyName("club_id")]
    public int? ClubId { get; set; }

    [JsonPropertyName("best_per_swimmer")]
    public bool BestPerSwimmer { get; set; }

    /// <summary>Сколько строк в срезе ВСЕГО (до <c>limit</c>/<c>offset</c>).</summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    /// <summary>Уникальных пловцов в срезе — в режиме «все заплывы» это не то же, что <c>total</c>.</summary>
    [JsonPropertyName("swimmers")]
    public int Swimmers { get; set; }

    /// <summary>
    /// Сколько соревнований вошло в срез. «Лучший в стране» у нас значит «лучший среди
    /// импортированного», и витрина обязана показывать это число рядом со списком.
    /// </summary>
    [JsonPropertyName("meets")]
    public int Meets { get; set; }

    /// <summary>
    /// Клубы, встречающиеся в срезе, — источник опций фильтра «Club». Считаются по ВСЕМУ
    /// срезу (до <c>limit</c>), иначе фильтр показывал бы только клубы первой страницы.
    /// Фильтр по клубу на состав этого списка не влияет — иначе, выбрав клуб, пользователь
    /// терял бы возможность выбрать другой.
    /// </summary>
    [JsonPropertyName("clubs")]
    public List<SeasonBestClubOptionDto> Clubs { get; set; } = new();

    [JsonPropertyName("data")]
    public List<SeasonBestListItemDto> Data { get; set; } = new();
}

/// <summary>Одна строка списка — один заплыв.</summary>
public sealed class SeasonBestListItemDto
{
    /// <summary>
    /// Место по времени внутри среза. Равные времена ДЕЛЯТ место, следующий за ними получает
    /// место по своему порядковому номеру (1, 2, 2, 4) — так же, как в протоколе.
    /// </summary>
    [JsonPropertyName("place")]
    public int Place { get; set; }

    /// <summary>
    /// Какой это по счёту заплыв ЭТОГО пловца в списке: 1 — его лучший, 2 — второй и т.д.
    /// Витрина этим отличает повтор от нового человека («первые два места у одного пловца»).
    /// В режиме <c>best_per_swimmer</c> всегда 1.
    /// </summary>
    [JsonPropertyName("attempt")]
    public int Attempt { get; set; }

    [JsonPropertyName("result_id")]
    public long ResultId { get; set; }

    [JsonPropertyName("time")]
    public string Time { get; set; } = "";

    [JsonPropertyName("time_ms")]
    public int? TimeMs { get; set; }

    /// <summary>
    /// Признак качества времени (И11). В этой выборке всегда null — помеченные ошибки
    /// протокола в season best не попадают вовсе; поле есть, чтобы время и его качество
    /// ездили вместе, как во всех DTO с временем заплыва (docs/data-integrity.md §И11).
    /// </summary>
    [JsonPropertyName("suspect_reason")]
    public string? SuspectReason { get; set; }

    /// <summary>Очки FINA из протокола. ⚠ Не монотонны времени: считались по разным таблицам.</summary>
    [JsonPropertyName("points")]
    public int Points { get; set; }

    /// <summary>Отставание от лидера среза в миллисекундах; 0 у самого лидера.</summary>
    [JsonPropertyName("gap_ms")]
    public int GapMs { get; set; }

    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("name_en")]
    public string? NameEn { get; set; }

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = "";

    /// <summary>Возраст В СЕЗОНЕ (год окончания сезона − год рождения).</summary>
    [JsonPropertyName("age")]
    public int Age { get; set; }

    /// <summary>
    /// Возрастная группа протокола («25-29»). Есть только у мастерских заплывов: там это и
    /// есть круг ровесников, внутри которого имеет смысл место.
    /// </summary>
    [JsonPropertyName("age_group")]
    public string? AgeGroup { get; set; }

    [JsonPropertyName("club_id")]
    public int ClubId { get; set; }

    [JsonPropertyName("club")]
    public string? Club { get; set; }

    /// <summary>Английское имя клуба; null, если у клуба его нет (в базе оно тогда — копия ивритского).</summary>
    [JsonPropertyName("club_en")]
    public string? ClubEn { get; set; }

    [JsonPropertyName("competition_id")]
    public int CompetitionId { get; set; }

    [JsonPropertyName("competition")]
    public string? Competition { get; set; }

    [JsonPropertyName("pool_type")]
    public string? PoolType { get; set; }

    /// <summary>Дата заплыва в формате витрины — DD/MM/YYYY.</summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";
}

/// <summary>Клуб в опциях фильтра — с числом заплывов, чтобы список можно было отсортировать по весу.</summary>
public sealed class SeasonBestClubOptionDto
{
    [JsonPropertyName("club_id")]
    public int ClubId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("name_en")]
    public string? NameEn { get; set; }

    [JsonPropertyName("swims")]
    public int Swims { get; set; }
}

/// <summary>
/// Опции страницы <c>/season-best</c>: чем наполнять карусель сезонов и селектор дисциплины.
///
/// Отдельный запрос, а не поля в списке: список зависит от выбранной дисциплины, а опции —
/// нет, и таскать их в каждом ответе значит гонять один и тот же килобайт на каждый чих
/// фильтра.
/// </summary>
public sealed class SeasonBestOptionsDto
{
    [JsonPropertyName("seasons")]
    public List<SeasonBestSeasonOptionDto> Seasons { get; set; } = new();

    [JsonPropertyName("events")]
    public List<SeasonBestEventOptionDto> Events { get; set; } = new();

    [JsonPropertyName("pools")]
    public List<string> Pools { get; set; } = new();

    /// <summary>
    /// Возрастные группы мастерских протоколов («19-24», «25-29», …) по возрастанию. Пусто —
    /// мастерских стартов в базе нет, и витрине нечего показывать во второй шкале возраста.
    /// </summary>
    [JsonPropertyName("age_groups")]
    public List<string> AgeGroups { get; set; } = new();
}

/// <summary>Сезон для карусели.</summary>
public sealed class SeasonBestSeasonOptionDto
{
    [JsonPropertyName("season")]
    public int Season { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("meets")]
    public int Meets { get; set; }

    /// <summary>
    /// Сезон, который витрина открывает по умолчанию. Сейчас это просто самый свежий сезон
    /// с данными; правило «до зимнего чемпионата держим прошлый» живёт на странице спортсмена
    /// и сюда пока не перенесено (docs/season-boundary-rule.md).
    /// </summary>
    [JsonPropertyName("is_display_default")]
    public bool IsDisplayDefault { get; set; }
}

/// <summary>Дисциплина: стиль и дистанции, которые в нём реально плавали.</summary>
public sealed class SeasonBestEventOptionDto
{
    [JsonPropertyName("style")]
    public string Style { get; set; } = "";

    /// <summary>Дистанции без «m», по возрастанию. Эстафетных («4X50») здесь нет.</summary>
    [JsonPropertyName("distances")]
    public List<string> Distances { get; set; } = new();
}
