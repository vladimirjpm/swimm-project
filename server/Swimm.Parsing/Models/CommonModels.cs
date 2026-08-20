using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Swimm.Parsing.Models;

public record Result(
    [property: JsonPropertyName("country")] string Country,
    [property: JsonPropertyName("competition")] string Competition,
    [property: JsonPropertyName("is_masters")] string IsMasters,
    [property: JsonPropertyName("is_award")] bool IsAward,
    [property: JsonPropertyName("age_group")] string AgeGroup,
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("event_style_name")] string EventStyleName,
    [property: JsonPropertyName("event_style_len")] string EventStyleLen,
    [property: JsonPropertyName("event_style_gender")] string EventStyleGender,
    [property: JsonPropertyName("event_style_age")] string EventStyleAge,
    [property: JsonPropertyName("pool_type")] string PoolType,
    [property: JsonPropertyName("position")] int? Position,
    [property: JsonPropertyName("heat")] int Heat,
    [property: JsonPropertyName("lane")] int Lane,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name_en")] string LastNameEn,
    [property: JsonPropertyName("first_name_en")] string FirstNameEn,
    [property: JsonPropertyName("birth_year")] int BirthYear,
    [property: JsonPropertyName("club")] string Club,
    [property: JsonPropertyName("club_en")] string ClubEn,
    [property: JsonPropertyName("time")] string Time,
    [property: JsonPropertyName("time_fail")] bool TimeFail,
    [property: JsonPropertyName("time_fail_note")] string? TimeFailNote,
    [property: JsonPropertyName("international_points")] int InternationalPoints,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("is_relay")] bool? IsRelay,
    [property: JsonPropertyName("relay_team_name")] string? RelayTeamName,
    [property: JsonPropertyName("relay_swimmers_name")] string? RelaySwimmersName,
    [property: JsonPropertyName("relay_swimmers")] List<RelaySwimmer>? RelaySwimmers,

    /// <summary>
    /// Категория заплыва, как она напечатана в заголовке протокола: <c>open</c> (Men/Women),
    /// <c>U17</c>, <c>para</c>, <c>mix</c>, либо возраст/группа ивритских протоколов
    /// («12», «25-29»). null — категория неизвестна.
    ///
    /// Отдельно от <c>EventStyleAge</c>/<c>AgeGroup</c>, потому что те производны от ГОДА
    /// РОЖДЕНИЯ пловца и категорию затирают: у Itsik Iaich из «50m Freestyle - Men Para»
    /// оставался только возраст 49 и группа «45-49». Без категории три разных заплыва
    /// протокола («Men», «U17 Boys», «Men Para») сливались в одну дисциплину «50 freestyle
    /// male», где оказывалось три первых места.
    /// </summary>
    [property: JsonPropertyName("event_category")] string? EventCategory = null,

    /// <summary>
    /// Тип заплыва: <c>prelim</c> / <c>final</c>; null — единственный заплыв дисциплины
    /// за день (timed final). В протоколах loglig слов «מוקדמות/גמר» нет — признак выводит
    /// <c>IsrOrgParser.AssignHeatTypes</c> по порядку сессий в документе: повтор дисциплины
    /// в один день ⇒ раннее событие prelim, позднее final.
    /// </summary>
    [property: JsonPropertyName("heat_type")] string? HeatType = null,

    /// <summary>
    /// Раунд зачёта из секции источника: <c>timed-final</c> (גמר ישיר) / <c>final</c> (גמר) /
    /// <c>prelim</c> (מוקדמות); null — источник раундов не различает. PDF-экспорт loglig
    /// не различает НИКОГДА (обе сессии печатаются одним списком), значение приходит только
    /// из пособытийного источника. Не путать с <c>heat_type</c>: тот — наш вывод об отборе,
    /// этот — факт из протокола. Подробности — docs/data-integrity.md §10.
    /// </summary>
    [property: JsonPropertyName("round")] string? Round = null,

    /// <summary>
    /// Клубные очки, начисленные САМИМ организатором за этот заплыв. Есть только у
    /// пособытийного источника loglig; у PDF-протоколов такой колонки нет — null.
    /// Хранится как эталон для сверки, зачёт считает наш движок правил.
    /// </summary>
    [property: JsonPropertyName("official_club_points")] int? OfficialClubPoints = null
);

public record RelaySwimmer(
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("birth_year")] int? BirthYear,
    [property: JsonPropertyName("club")] string? Club,
    [property: JsonPropertyName("split_time")] string? SplitTime
);
