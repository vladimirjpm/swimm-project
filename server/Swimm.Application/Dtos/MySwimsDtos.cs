using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/* DTO страницы «My media v3» (swim-centric): GET /api/me/swims.
   Группировку по соревнованию и фильтры (кроме сезона) делает клиент —
   объём одного сезона favorite-пловцов мал. */

public class MySwimsResponseDto
{
    /// <summary>Favorite-пловцы юзера (чипы), primary первым.</summary>
    [JsonPropertyName("swimmers")]
    public List<MySwimmerDto> Swimmers { get; set; } = new();

    /// <summary>Сезоны, где у пловцов есть результаты (стартовые годы, по убыванию).</summary>
    [JsonPropertyName("seasons")]
    public List<int> Seasons { get; set; } = new();

    /// <summary>Выбранный сезон (стартовый год, сентябрь–август).</summary>
    [JsonPropertyName("season")]
    public int Season { get; set; }

    [JsonPropertyName("swims")]
    public List<MySwimDto> Swims { get; set; } = new();

    /// <summary>Медиа уровня «соревнование» (📎 в шапке группы) — все сезоны, клиент фильтрует по competition_id.</summary>
    [JsonPropertyName("competition_media")]
    public List<UserMediaDto> CompetitionMedia { get; set; } = new();

    /// <summary>Медиа уровня «пловец» — секция Unlinked.</summary>
    [JsonPropertyName("unlinked_media")]
    public List<UserMediaDto> UnlinkedMedia { get; set; } = new();
}

public class MySwimmerDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("is_primary")]
    public bool IsPrimary { get; set; }
}

public class MySwimDto
{
    [JsonPropertyName("result_id")]
    public long ResultId { get; set; }

    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    [JsonPropertyName("competition_id")]
    public int CompetitionId { get; set; }

    [JsonPropertyName("competition_name")]
    public string CompetitionName { get; set; } = string.Empty;

    /// <summary>dd/MM/yyyy (как у Competition.Date).</summary>
    [JsonPropertyName("competition_date")]
    public string CompetitionDate { get; set; } = string.Empty;

    [JsonPropertyName("pool_type")]
    public string PoolType { get; set; } = string.Empty;

    /// <summary>ISO-дата заплыва (день многодневного может отличаться от даты соревнования).</summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("distance")]
    public string Distance { get; set; } = string.Empty;

    [JsonPropertyName("style")]
    public string Style { get; set; } = string.Empty;

    [JsonPropertyName("style_id")]
    public int StyleId { get; set; }

    [JsonPropertyName("is_relay")]
    public bool IsRelay { get; set; }

    /// <summary>Внутреннее — для донасыщения членства эстафеты; наружу не сериализуется.</summary>
    [JsonIgnore]
    public int? RelayId { get; set; }

    /// <summary>
    /// SwimmerId всех ног эстафеты (для эстафет). Клиент матчит чип-фильтр и счётчики по
    /// членству, т.к. строка эстафеты привязана к одному «владельцу», но принадлежит всем.
    /// У индивидуальных заплывов пусто.
    /// </summary>
    [JsonPropertyName("member_swimmer_ids")]
    public List<int> MemberSwimmerIds { get; set; } = new();

    [JsonPropertyName("place")]
    public int? Place { get; set; }

    [JsonPropertyName("points")]
    public int Points { get; set; }

    [JsonPropertyName("time")]
    public string Time { get; set; } = string.Empty;

    /// <summary>Инвариант И11: DTO со временем несёт и качество. null — заплыв в порядке.</summary>
    [JsonPropertyName("suspect_reason")]
    public string? SuspectReason { get; set; }

    [JsonPropertyName("time_fail")]
    public bool TimeFail { get; set; }

    /// <summary>Личный рекорд: лучшее время пловца за всё время на (стиль, дистанция), индивидуальные заплывы.</summary>
    [JsonPropertyName("is_pb")]
    public bool IsPb { get; set; }

    [JsonPropertyName("congrats_count")]
    public int CongratsCount { get; set; }

    [JsonPropertyName("my_cheer")]
    public bool MyCheer { get; set; }

    /// <summary>Медиа юзера, привязанные к этому заплыву (видео и фото).</summary>
    [JsonPropertyName("media")]
    public List<UserMediaDto> Media { get; set; } = new();
}
