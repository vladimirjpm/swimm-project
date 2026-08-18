using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Карьерные (all-time) данные спортсмена для карточки в попапе
/// (переключатель «Соревнование / All-time», см. design_handoff_athlete_alltime).
/// Имена полей в JSON — camelCase (клиентский <c>AthleteCareer</c> в useAthleteCareer.ts);
/// CachedJson сериализует дефолтными опциями (PascalCase), поэтому маппинг задаём явно.
/// </summary>
public class AthleteCareerDto
{
    /// <summary>Всего соревнований (distinct CompetitionId).</summary>
    [JsonPropertyName("competitions")]
    public int Competitions { get; set; }

    /// <summary>Всего заплывов.</summary>
    [JsonPropertyName("races")]
    public int Races { get; set; }

    /// <summary>Год первого результата (0 — данных нет).</summary>
    [JsonPropertyName("since")]
    public int Since { get; set; }

    /// <summary>Сумма international points по всем заплывам.</summary>
    [JsonPropertyName("totalPoints")]
    public int TotalPoints { get; set; }

    [JsonPropertyName("gold")]
    public int Gold { get; set; }

    [JsonPropertyName("silver")]
    public int Silver { get; set; }

    [JsonPropertyName("bronze")]
    public int Bronze { get; set; }

    [JsonPropertyName("bestByStyle")]
    public List<CareerBestDto> BestByStyle { get; set; } = new();

    /// <summary>Разбивка медалей по конкретным заплывам (для тултипа "за что" на карточке).</summary>
    [JsonPropertyName("medals")]
    public List<MedalDetailDto> Medals { get; set; } = new();
}

/// <summary>Один медальный заплыв за карьеру — за что дали место 1/2/3.</summary>
public class MedalDetailDto
{
    /// <summary>1, 2 или 3.</summary>
    [JsonPropertyName("position")]
    public int Position { get; set; }

    /// <summary>Например "Freestyle 50м".</summary>
    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;

    [JsonPropertyName("competition")]
    public string Competition { get; set; } = string.Empty;

    /// <summary>Дата соревнования в исходном формате "DD/MM/YYYY".</summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;
}

/// <summary>Лучшее время по (стиль × дистанция) за карьеру.</summary>
public class CareerBestDto
{
    [JsonPropertyName("stroke")]
    public string Stroke { get; set; } = string.Empty;

    [JsonPropertyName("distance")]
    public string Distance { get; set; } = string.Empty;

    [JsonPropertyName("time")]
    public string Time { get; set; } = string.Empty;

    /// <summary>Инвариант И11: DTO со временем несёт и качество. null — заплыв в порядке.</summary>
    [JsonPropertyName("suspect_reason")]
    public string? SuspectReason { get; set; }

    [JsonPropertyName("points")]
    public int Points { get; set; }

    [JsonPropertyName("pool")]
    public string Pool { get; set; } = string.Empty;

    [JsonPropertyName("competition")]
    public string Competition { get; set; } = string.Empty;

    /// <summary>Дата соревнования в исходном формате "DD/MM/YYYY" (как в ResultDto.Date).</summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    /// <summary>Место на этом заплыве (для карточки-строки в стиле результатов соревнования).</summary>
    [JsonPropertyName("position")]
    public int? Position { get; set; }

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = string.Empty;

    [JsonPropertyName("eventStyleAge")]
    public string EventStyleAge { get; set; } = string.Empty;

    [JsonPropertyName("ageGroup")]
    public string AgeGroup { get; set; } = string.Empty;

    [JsonPropertyName("isMasters")]
    public bool IsMasters { get; set; }

    /// <summary>Award-eligible ли то соревнование (Competition.IsAward) — место 1/2/3 без этого не медаль.</summary>
    [JsonPropertyName("isAward")]
    public bool IsAward { get; set; }
}
