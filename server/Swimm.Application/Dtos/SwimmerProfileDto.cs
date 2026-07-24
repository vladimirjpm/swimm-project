using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Профиль спортсмена для самостоятельной страницы пловца (swimmer.html?swimmer=&lt;id&gt;).
/// Адресуемся по стабильному <see cref="Id"/>; карьерные (all-time) данные страница берёт
/// отдельно через /api/athletes/career по <see cref="FullName"/> (переиспользование логики
/// матчинга по имени, включая эстафеты). camelCase в JSON — как в остальных публичных DTO.
/// </summary>
public class SwimmerProfileDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Полное имя для карьерного запроса и заголовка: RU («Имя Фамилия»), либо EN, если RU пусто.</summary>
    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("lastName")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("firstNameEn")]
    public string FirstNameEn { get; set; } = string.Empty;

    [JsonPropertyName("lastNameEn")]
    public string LastNameEn { get; set; } = string.Empty;

    [JsonPropertyName("birthYear")]
    public int BirthYear { get; set; }

    /// <summary>Пол (M / F), если известен.</summary>
    [JsonPropertyName("gender")]
    public string? Gender { get; set; }

    [JsonPropertyName("clubId")]
    public int? ClubId { get; set; }

    [JsonPropertyName("clubName")]
    public string? ClubName { get; set; }

    /// <summary>alpha-3 код страны (ISR/GER/…); флаг маппит клиент.</summary>
    [JsonPropertyName("countryCode")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("countryName")]
    public string? CountryName { get; set; }

    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }

    /// <summary>isr | local (см. Swimmer.Origin).</summary>
    [JsonPropertyName("origin")]
    public string Origin { get; set; } = "isr";
}
