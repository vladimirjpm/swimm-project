using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Национальный season best одной дисциплины: лучшее время сезона в каждой паре
/// «пол × возрастная ступень». Витрина показывает его вторым табом рядом со справочными
/// возрастными рекордами (design_handoff_age_records_sb).
///
/// ⚠ Ось возраста здесь СЕЗОННАЯ (<see cref="Swimm.Domain.SeasonMath.AgeInSeason"/>), в отличие
/// от справочника рекордов, где ось календарная (решение Влада 2026-08-22). Это осознанное
/// расхождение: рекорды — чужая таблица федерации, season best — наш собственный счёт.
/// </summary>
public sealed class SeasonBestNationalDto
{
    /// <summary>Год НАЧАЛА сезона (SeasonMath): 2025 = сезон 2025/26.</summary>
    [JsonPropertyName("season")]
    public int Season { get; set; }

    [JsonPropertyName("season_label")]
    public string SeasonLabel { get; set; } = "";

    [JsonPropertyName("style")]
    public string Style { get; set; } = "";

    /// <summary>Дистанция как в Results.Distance — без «m»: «50», «100», «1500».</summary>
    [JsonPropertyName("distance")]
    public string Distance { get; set; } = "";

    /// <summary>Бассейн фильтра: «25m» / «50m» / null — оба (тогда он у каждой записи свой).</summary>
    [JsonPropertyName("pool_type")]
    public string? PoolType { get; set; }

    /// <summary>
    /// Сколько соревнований вошло в расчёт. «Лучший в стране» у нас значит «лучший среди
    /// импортированного» — цифру видно на витрине, как и в клубной карточке season best.
    /// </summary>
    [JsonPropertyName("meets")]
    public int Meets { get; set; }

    [JsonPropertyName("data")]
    public List<SeasonBestNationalItemDto> Data { get; set; } = new();
}

/// <summary>Один лидер: пол × возраст в сезоне.</summary>
public sealed class SeasonBestNationalItemDto
{
    [JsonPropertyName("gender")]
    public string Gender { get; set; } = "";

    /// <summary>Возраст В СЕЗОНЕ (год окончания сезона − год рождения).</summary>
    [JsonPropertyName("age")]
    public int Age { get; set; }

    [JsonPropertyName("time")]
    public string Time { get; set; } = "";

    /// <summary>
    /// Признак качества времени (И11). В этой выборке всегда null — помеченные ошибки
    /// протокола в season best не попадают вовсе; поле есть, чтобы время и его качество
    /// ездили вместе, как во всех DTO с временем заплыва (docs/data-integrity.md §И11).
    /// </summary>
    [JsonPropertyName("suspect_reason")]
    public string? SuspectReason { get; set; }

    [JsonPropertyName("time_ms")]
    public int? TimeMs { get; set; }

    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("name_en")]
    public string? NameEn { get; set; }

    [JsonPropertyName("club")]
    public string? Club { get; set; }

    [JsonPropertyName("pool_type")]
    public string? PoolType { get; set; }

    [JsonPropertyName("competition")]
    public string? Competition { get; set; }

    /// <summary>Дата заплыва в формате справочника рекордов — DD/MM/YYYY (витрина рисует её тем же UI_DateIcon).</summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("points")]
    public int? Points { get; set; }
}
