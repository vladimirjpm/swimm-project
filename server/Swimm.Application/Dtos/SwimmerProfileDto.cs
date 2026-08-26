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

    // ── Шапка страницы спортсмена (этап A2, docs/plans/athlete-page-plan.md) ──────
    // Поля добавлены к существующему DTO, а не вынесены во второй эндпоинт: попапу они не
    // мешают (лишние ключи он игнорирует), а странице иначе понадобился бы второй запрос
    // ради возраста и списка сезонов.

    /// <summary>
    /// Возраст В СЕЗОНЕ (год окончания сезона минус год рождения) для витринного сезона —
    /// подпись «12 year (2014)». null — год рождения не заполнен.
    /// ⚠ Это НЕ возраст на день заплыва: осенние и весенние старты одного пловца иначе
    /// разъезжаются по двум возрастным ступеням (SeasonMath.AgeInSeason).
    /// </summary>
    [JsonPropertyName("ageInSeason")]
    public int? AgeInSeason { get; set; }

    /// <summary>Зачётная группа лестницы; null — стартов в лестничных категориях не было.</summary>
    [JsonPropertyName("ageGroup")]
    public SwimmerAgeGroupDto? AgeGroup { get; set; }

    /// <summary>Программы: «pool», позже «open» (docs/plans/open-water-course-plan.md).</summary>
    [JsonPropertyName("programs")]
    public List<string> Programs { get; set; } = [];

    /// <summary>Официальных рекордов за пловцом; 0 — бейдж не рендерится.</summary>
    [JsonPropertyName("recordsHeld")]
    public int RecordsHeld { get; set; }

    /// <summary>
    /// Сами рекорды, которые он держит, — из них же считается <see cref="RecordsHeld"/>.
    /// Едут вместе со счётчиком, а не отдельным запросом: у подавляющего большинства
    /// пловцов список пуст, а разъехаться числу и списку нельзя.
    /// </summary>
    [JsonPropertyName("records")]
    public List<SwimmerHeldRecordDto> Records { get; set; } = [];

    /// <summary>Сезоны с заплывами, от свежих к старым; ровно один — isDisplayDefault.</summary>
    [JsonPropertyName("seasons")]
    public List<SwimmerSeasonOptionDto> Seasons { get; set; } = [];
}
