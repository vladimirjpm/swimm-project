using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Swimm.API.Services.Models;

public record PDF_Result(
    [property: JsonPropertyName("country")] string Country,
    [property: JsonPropertyName("position")] object? Position,
    [property: JsonPropertyName("heat")] int Heat,
    [property: JsonPropertyName("lane")] int Lane,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("birth_year")] int BirthYear,
    [property: JsonPropertyName("club")] string Club,
    [property: JsonPropertyName("time")] string? Time,
    [property: JsonPropertyName("time_fail_note")] string? TimeFailNote,
    [property: JsonPropertyName("international_points")] int InternationalPoints,
    [property: JsonPropertyName("is_relay")] bool? IsRelay,
    [property: JsonPropertyName("relay_team_name")] string? RelayTeamName,
    [property: JsonPropertyName("relay_swimmers_name")] string? RelaySwimmersName,
    [property: JsonPropertyName("relay_swimmers")] List<RelaySwimmer>? RelaySwimmers
);

public record PDF_CompetitionResult(
    [property: JsonPropertyName("competition")] string Competition,
    [property: JsonPropertyName("age_group")] string AgeGroup,
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("event_style_name")] string EventStyleName,
    [property: JsonPropertyName("event_style_len")] string EventStyleLen,
    [property: JsonPropertyName("event_style_gender")] string EventStyleGender,
    [property: JsonPropertyName("event_style_age")] string EventStyleAge,
    [property: JsonPropertyName("pool_type")] string PoolType,
    [property: JsonPropertyName("results")] List<PDF_Result> Results
);

public record Result(
    [property: JsonPropertyName("country")] string Country,
    [property: JsonPropertyName("competition")] string Competition,
    [property: JsonPropertyName("is_masters")] string IsMasters,
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
    [property: JsonPropertyName("relay_swimmers")] List<RelaySwimmer>? RelaySwimmers
);

public record RelaySwimmer(
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("birth_year")] int? BirthYear,
    [property: JsonPropertyName("club")] string? Club,
    [property: JsonPropertyName("split_time")] string? SplitTime
);
