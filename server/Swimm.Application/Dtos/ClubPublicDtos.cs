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

    /// <summary>Возраст В СЕЗОНЕ (сезон - BirthYear) — НЕ зачётная группа Category.</summary>
    [JsonPropertyName("age")]
    public int Age { get; set; }

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
}

/// <summary>Ответ /api/clubs/{id}/records.</summary>
public sealed class ClubRecordsDto
{
    [JsonPropertyName("data")]
    public List<ClubBestDto> Data { get; set; } = [];
}
