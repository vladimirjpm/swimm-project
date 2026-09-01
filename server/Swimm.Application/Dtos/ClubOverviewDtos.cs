using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Сборный ответ страницы клуба (K4.1): всё, что рисуется при первой отрисовке —
/// Hero, фильтры, грид «сезон × группа», таблица выбранного зачёта, история и топ пловцов.
/// Ростер и рекорды догружаются отдельно (<c>/api/clubs/{id}/roster</c>, <c>/records</c>):
/// они пагинируемые и у рекордов свой переключатель бассейна.
/// </summary>
public sealed class ClubOverviewDto
{
    [JsonPropertyName("club")]
    public ClubProfileDto Club { get; set; } = new();

    [JsonPropertyName("kpi")]
    public ClubKpiDto Kpi { get; set; } = new();

    /// <summary>Сезоны с данными (год начала), свежие первыми — для свайп-ряда фильтра.</summary>
    [JsonPropertyName("seasons")]
    public List<ClubSeasonOptionDto> Seasons { get; set; } = [];

    /// <summary>Зачётные группы клуба с рангами в выбранном сезоне — плитки фильтра.</summary>
    [JsonPropertyName("groups")]
    public List<ClubGroupTileDto> Groups { get; set; } = [];

    /// <summary>Грид «сезон × группа»: год → строки групп с рангами ❄/☀.</summary>
    [JsonPropertyName("grid")]
    public List<ClubGridYearDto> Grid { get; set; } = [];

    /// <summary>Таблица выбранного зачёта. null — зачёт не выбран или его нет.</summary>
    [JsonPropertyName("standings")]
    public ClubStandingsTableDto? Standings { get; set; }

    /// <summary>История выступлений: все соревнования клуба в выбранном скоупе, свежие первыми.</summary>
    [JsonPropertyName("timeline")]
    public List<ClubTimelineItemDto> Timeline { get; set; } = [];

    /// <summary>Топ пловцов клуба по принесённым очкам.</summary>
    [JsonPropertyName("top_swimmers")]
    public List<ClubTopSwimmerDto> TopSwimmers { get; set; } = [];
}

/// <summary>Идентичность клуба для Hero.</summary>
public sealed class ClubProfileDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Id, по которому пришёл запрос. Отличается от <see cref="Id"/>, если клуб склеен.</summary>
    [JsonPropertyName("requested_id")]
    public int RequestedId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("name_en")]
    public string NameEn { get; set; } = "";

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("country_name")]
    public string? CountryName { get; set; }

    /// <summary>Официальная hub-группа клуба (бейдж «Official group» + ссылка). null — нет.</summary>
    [JsonPropertyName("official_group_slug")]
    public string? OfficialGroupSlug { get; set; }

    [JsonPropertyName("official_group_name")]
    public string? OfficialGroupName { get; set; }

    /// <summary>Пловцов в справочнике клуба (Swimmer.ClubId).</summary>
    [JsonPropertyName("swimmer_count")]
    public int SwimmerCount { get; set; }

    /// <summary>Первый и последний сезон с результатами. null — результатов нет вовсе.</summary>
    [JsonPropertyName("first_season")]
    public int? FirstSeason { get; set; }

    [JsonPropertyName("last_season")]
    public int? LastSeason { get; set; }
}

/// <summary>KPI-ряд Hero в границах выбранного скоупа.</summary>
public sealed class ClubKpiDto
{
    /// <summary>⚠ Сумма очков по РАЗНЫМ правилам, если скоуп шире одного соревнования:
    /// у каждого старта своё правило. Сравнимая величина — только ранг.</summary>
    [JsonPropertyName("points")]
    public int Points { get; set; }

    [JsonPropertyName("gold")]
    public int Gold { get; set; }

    [JsonPropertyName("silver")]
    public int Silver { get; set; }

    [JsonPropertyName("bronze")]
    public int Bronze { get; set; }

    [JsonPropertyName("competitions")]
    public int Competitions { get; set; }

    /// <summary>Лучшее (наименьшее) место клуба в скоупе. null — зачётов нет.</summary>
    [JsonPropertyName("best_rank")]
    public int? BestRank { get; set; }

    // ── Витрина Hero (решение Влада 2026-08-09, уточнено 2026-08-13) ──────────
    // Плитки шапки считают НЕ то же, что поля выше: чемпионаты и первые места — за всю
    // историю клуба независимо от карусели сезонов, рекорды — действующие, season best —
    // за ВИТРИННЫЙ сезон (ShowcaseSeason: сезон целиком, но переключается на новый только
    // после последнего зимнего чемпионата).
    // Старые поля оставлены: они дешёвые и на них держатся тесты и прочие потребители.

    /// <summary>Чемпионатов за всю историю клуба (зачётных единиц, а не дней).</summary>
    [JsonPropertyName("championships")]
    public int Championships { get; set; }

    /// <summary>Из них выигранных (Rank = 1), за всю историю.</summary>
    [JsonPropertyName("championship_wins")]
    public int ChampionshipWins { get; set; }

    /// <summary>Действующих официальных рекордов за клубом (то же число, что в Record wall).</summary>
    [JsonPropertyName("records")]
    public int Records { get; set; }

    /// <summary>Лучших времён страны (season best) за витринный сезон.</summary>
    [JsonPropertyName("season_bests")]
    public int SeasonBests { get; set; }

    /// <summary>
    /// Метка витринного сезона («2025/26») — подпись к плитке season best. Раньше здесь
    /// стояла ДАТА последнего зимнего чемпионата: правило читалось как «сезон обрывается
    /// чемпионатом», и витрина показывала окно «с 27/02/2026», теряя декабрь–февраль.
    /// </summary>
    [JsonPropertyName("showcase_season")]
    public string? ShowcaseSeason { get; set; }

    /// <summary>
    /// Новый сезон уже идёт, но витрина держит прошлый — пояснение к плитке season bests
    /// (docs/season-boundary-rule.md). null — сезон открыт, объяснять нечего.
    /// </summary>
    [JsonPropertyName("season_notice")]
    public ShowcaseSeasonNoticeDto? SeasonNotice { get; set; }
}

/// <summary>Сезон для фильтра.</summary>
public sealed class ClubSeasonOptionDto
{
    [JsonPropertyName("season")]
    public int Season { get; set; }

    /// <summary>«2025/26».</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";
}

/// <summary>
/// Плитка зачётной группы: буква, название и ранги ❄/☀ в выбранном сезоне.
/// При «все сезоны» показывается ЛУЧШИЙ ранг группы за всю историю — иначе плитке нечего
/// показать; клиент обязан подписать это, а не выдавать за текущий сезон.
/// </summary>
public sealed class ClubGroupTileDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("badge")]
    public string? Badge { get; set; }

    [JsonPropertyName("winter_rank")]
    public int? WinterRank { get; set; }

    [JsonPropertyName("summer_rank")]
    public int? SummerRank { get; set; }

    [JsonPropertyName("open_water_rank")]
    public int? OpenWaterRank { get; set; }
}

/// <summary>Год грида: сезон + строка на каждую группу с зачётом.</summary>
public sealed class ClubGridYearDto
{
    [JsonPropertyName("season")]
    public int Season { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("rows")]
    public List<ClubGridRowDto> Rows { get; set; } = [];
}

/// <summary>Строка грида: группа и её зачёты за сезон.</summary>
public sealed class ClubGridRowDto
{
    [JsonPropertyName("group_key")]
    public string GroupKey { get; set; } = "";

    [JsonPropertyName("group_name")]
    public string GroupName { get; set; } = "";

    [JsonPropertyName("badge")]
    public string? Badge { get; set; }

    [JsonPropertyName("winter")]
    public ClubGridCellDto? Winter { get; set; }

    [JsonPropertyName("summer")]
    public ClubGridCellDto? Summer { get; set; }

    [JsonPropertyName("open_water")]
    public ClubGridCellDto? OpenWater { get; set; }
}

/// <summary>Клетка грида: место клуба в зачёте + id соревнования, чтобы открыть таблицу.</summary>
public sealed class ClubGridCellDto
{
    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("points")]
    public int Points { get; set; }

    [JsonPropertyName("clubs")]
    public int Clubs { get; set; }

    /// <summary>Зачётная единица (соревнование или первый день события) — ключ для standings.</summary>
    [JsonPropertyName("competition_id")]
    public int CompetitionId { get; set; }
}

/// <summary>Таблица выбранного зачёта.</summary>
public sealed class ClubStandingsTableDto
{
    [JsonPropertyName("competition_id")]
    public int CompetitionId { get; set; }

    [JsonPropertyName("competition_name")]
    public string CompetitionName { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("season")]
    public int Season { get; set; }

    /// <summary>winter | summer | openwater | null (обычный старт).</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("group_key")]
    public string? GroupKey { get; set; }

    [JsonPropertyName("group_name")]
    public string? GroupName { get; set; }

    [JsonPropertyName("club_count")]
    public int ClubCount { get; set; }

    /// <summary>Строки таблицы: лидеры + окно вокруг нашего клуба.</summary>
    [JsonPropertyName("rows")]
    public List<ClubStandingRowDto> Rows { get; set; } = [];
}

/// <summary>Строка таблицы зачёта.</summary>
public sealed class ClubStandingRowDto
{
    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("club_id")]
    public int ClubId { get; set; }

    [JsonPropertyName("club_name")]
    public string ClubName { get; set; } = "";

    [JsonPropertyName("club_name_en")]
    public string ClubNameEn { get; set; } = "";

    [JsonPropertyName("points")]
    public int Points { get; set; }

    [JsonPropertyName("gold")]
    public int Gold { get; set; }

    [JsonPropertyName("silver")]
    public int Silver { get; set; }

    [JsonPropertyName("bronze")]
    public int Bronze { get; set; }

    [JsonPropertyName("swimmer_count")]
    public int SwimmerCount { get; set; }

    [JsonPropertyName("scoring_swims")]
    public int ScoringSwims { get; set; }

    /// <summary>Это наш клуб — подсветка «us» в макете.</summary>
    [JsonPropertyName("is_us")]
    public bool IsUs { get; set; }
}

/// <summary>Соревнование в истории клуба.</summary>
public sealed class ClubTimelineItemDto
{
    [JsonPropertyName("competition_id")]
    public int CompetitionId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("season")]
    public int Season { get; set; }

    /// <summary>winter | summer | openwater | null — бейдж ❄/☀ рисуется только когда есть.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("group_key")]
    public string? GroupKey { get; set; }

    [JsonPropertyName("group_name")]
    public string? GroupName { get; set; }

    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("points")]
    public int Points { get; set; }

    [JsonPropertyName("gold")]
    public int Gold { get; set; }

    [JsonPropertyName("silver")]
    public int Silver { get; set; }

    [JsonPropertyName("bronze")]
    public int Bronze { get; set; }

    [JsonPropertyName("swimmer_count")]
    public int SwimmerCount { get; set; }
}

/// <summary>Пловец в топе клуба.</summary>
public sealed class ClubTopSwimmerDto
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

    [JsonPropertyName("gender")]
    public string? Gender { get; set; }

    [JsonPropertyName("age")]
    public int? Age { get; set; }

    /// <summary>Очки, принесённые клубу (по правилу каждого соревнования).</summary>
    [JsonPropertyName("points")]
    public int Points { get; set; }

    [JsonPropertyName("gold")]
    public int Gold { get; set; }

    [JsonPropertyName("silver")]
    public int Silver { get; set; }

    [JsonPropertyName("bronze")]
    public int Bronze { get; set; }
}
