using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Строка истории клубов пловца: ОДИН клуб в ОДНОМ сезоне
/// (docs/plans/swimmer-club-change-plan.md, Ф2, решение К5). Пловец, сменивший клуб посреди
/// сезона, получает в этом сезоне две строки.
///
/// Строится из <c>Results.ClubId</c> (клуб на момент заплыва), а не из карточки пловца:
/// <c>Swimmer.ClubId</c> — это «текущий» клуб, истории он не помнит. Псевдоклубы (страна,
/// сборная) выкинуты, склеенный клуб сведён к приёмнику, эстафеты не считаются — у них в
/// SwimmerId бывает «пловец-тень» (то же правило, что у подписки группы на клуб).
/// </summary>
public sealed class SwimmerClubSeasonDto
{
    /// <summary>Сезон — год НАЧАЛА (SeasonMath): 2025 = «2025/26».</summary>
    [JsonPropertyName("season")]
    public int Season { get; set; }

    /// <summary>Подпись сезона «2025/26» — тем же <c>SeasonMath.Label</c>, что карусель.</summary>
    [JsonPropertyName("seasonLabel")]
    public string SeasonLabel { get; set; } = string.Empty;

    [JsonPropertyName("clubId")]
    public int ClubId { get; set; }

    /// <summary>Название как в базе (иврит) — витрина показывает его по умолчанию.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Английское название — фоллбек, когда ивритского нет. null — его нет или оно то же.</summary>
    [JsonPropertyName("nameEn")]
    public string? NameEn { get; set; }

    /// <summary>Индивидуальных заплывов за клуб в этом сезоне.</summary>
    [JsonPropertyName("swims")]
    public int Swims { get; set; }

    /// <summary>Первый старт за клуб в сезоне, yyyy-MM-dd.</summary>
    [JsonPropertyName("firstDate")]
    public string FirstDate { get; set; } = string.Empty;

    /// <summary>Последний старт за клуб в сезоне, yyyy-MM-dd. По нему выбирается «клуб сезона».</summary>
    [JsonPropertyName("lastDate")]
    public string LastDate { get; set; } = string.Empty;

    /// <summary>
    /// Части сезона, где он плыл за клуб: <c>winter</c> — короткая вода (25м), <c>summer</c> —
    /// длинная (50м). Значок ❄️/☀️ в окне истории: видно, что клуб сменился между зимой и летом.
    /// </summary>
    [JsonPropertyName("courses")]
    public List<string> Courses { get; set; } = [];
}
