using System.Collections.Generic;
using Swimm.API.Services.Models;

namespace Swimm.API.Services.Parsers.IsrOrg;

public record IsrOrgResult(
    string Country,
    object? Position,
    int Heat,
    int Lane,
    string LastName,
    string FirstName,
    int BirthYear,
    string Club,
    string? Time,
    string? TimeFailNote,
    int InternationalPoints,
    bool? IsRelay,
    string? RelayTeamName,
    string? RelaySwimmersName,
    List<RelaySwimmer>? RelaySwimmers
);

public record IsrOrgCompetitionResult(
    string Competition,
    string AgeGroup,
    string Date,
    string Event,
    string EventStyleName,
    string EventStyleLen,
    string EventStyleGender,
    string EventStyleAge,
    string PoolType,
    List<IsrOrgResult> Results
);
