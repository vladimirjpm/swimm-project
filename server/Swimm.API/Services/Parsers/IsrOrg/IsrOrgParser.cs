using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Swimm.API.Services.Models;
using Swimm.API.Services.Helpers;

namespace Swimm.API.Services.Parsers.IsrOrg;

public class IsrOrgParser : IFormatParser
{
    public string FormatName => "IsrOrg";

    public IEnumerable<Result> Parse(ParseRequest request)
    {
        if (request.SecondaryStream == null || string.IsNullOrWhiteSpace(request.SecondaryFileName))
        {
            return ParseHebrewOnly(request.PrimaryStream, request.PrimaryFileName, request.IsAward);
        }

        return ParseBilingual(
            request.SecondaryStream, request.SecondaryFileName!,
            request.PrimaryStream, request.PrimaryFileName,
            request.IsAward);
    }

    public string GetDebugLog() => IsrOrgCompetitionParser.GetDebugLog();

    private static int DetermineAge(int eventYear, int birthYear, string? eventStyleAge)
    {
        if (birthYear > 0 && eventYear > 0)
        {
            return eventYear - birthYear;
        }

        if (!string.IsNullOrWhiteSpace(eventStyleAge))
        {
            var agePart = eventStyleAge.Split('-')[0];
            if (int.TryParse(agePart, out int parsedAge) && parsedAge > 0)
            {
                return parsedAge;
            }
        }

        return 0;
    }

    private static IEnumerable<Result> ParseHebrewOnly(Stream hebrewPdfStream, string hebrewFileName, bool isAward)
    {
        var heParts = Path.GetFileNameWithoutExtension(hebrewFileName)
                          .Split('_', StringSplitOptions.RemoveEmptyEntries);
        var country = heParts.Length >= 2 ? heParts[^2] : string.Empty;
        var langHe = heParts.Length >= 1 ? heParts[^1] : string.Empty;

        var isMastersFile = Path.GetFileNameWithoutExtension(hebrewFileName)
                            .Contains("masters", StringComparison.OrdinalIgnoreCase);

        foreach (var comp in IsrOrgCompetitionParser.ParseCompetitions(hebrewPdfStream, langHe))
        {
            foreach (var rHe in comp.Results)
            {
                int eventYear = AgeGroupHelper.ExtractYearFromDateString(comp.Date);

                if (rHe.IsRelay == true && rHe.RelaySwimmers?.Count > 0)
                {
                    yield return CreateRelayResult(rHe, comp, country, eventYear, isMastersFile, isAward,
                        lastNameEn: string.Empty, firstNameEn: string.Empty, clubEn: string.Empty);
                    continue;
                }

                var age = DetermineAge(eventYear, rHe.BirthYear, comp.EventStyleAge);
                var ageGroup = AgeGroupHelper.GetAgeGroup(age);

                yield return new Result(
                    Country: country,
                    Competition: comp.Competition,
                    IsMasters: (isMastersFile && age >= 25) ? "true" : "false",
                    IsAward: isAward,
                    AgeGroup: ageGroup,
                    Date: comp.Date,
                    Event: comp.Event,
                    EventStyleName: comp.EventStyleName,
                    EventStyleLen: comp.EventStyleLen,
                    EventStyleGender: comp.EventStyleGender,
                    EventStyleAge: age.ToString(),
                    PoolType: comp.PoolType,
                    Position: rHe.Position is int pi ? pi : null,
                    Heat: rHe.Heat,
                    Lane: rHe.Lane,
                    LastName: rHe.LastName,
                    FirstName: rHe.FirstName,
                    LastNameEn: string.Empty,
                    FirstNameEn: string.Empty,
                    BirthYear: rHe.BirthYear,
                    Club: rHe.Club,
                    ClubEn: string.Empty,
                    Time: rHe.Time ?? string.Empty,
                    TimeFail: rHe.Time == null,
                    TimeFailNote: rHe.TimeFailNote,
                    InternationalPoints: rHe.InternationalPoints,
                    Note: null,
                    IsRelay: rHe.IsRelay ?? false,
                    RelayTeamName: rHe.RelayTeamName,
                    RelaySwimmersName: null,
                    RelaySwimmers: rHe.RelaySwimmers
                );
            }
        }
    }

    private static IEnumerable<Result> ParseBilingual(
        Stream englishPdfStream, string englishFileName,
        Stream hebrewPdfStream, string hebrewFileName,
        bool isAward)
    {
        var enParts = Path.GetFileNameWithoutExtension(englishFileName)
                          .Split('_', StringSplitOptions.RemoveEmptyEntries);
        var countryEn = enParts.Length >= 2 ? enParts[^2] : string.Empty;
        var langEn = enParts.Length >= 1 ? enParts[^1] : string.Empty;

        var hePartsSync = Path.GetFileNameWithoutExtension(hebrewFileName)
                              .Split('_', StringSplitOptions.RemoveEmptyEntries);
        var langHeSync = hePartsSync.Length >= 1 ? hePartsSync[^1] : string.Empty;

        var isMastersFile = Path.GetFileNameWithoutExtension(hebrewFileName)
                            .Contains("masters", StringComparison.OrdinalIgnoreCase);

        var compsEn = IsrOrgCompetitionParser.ParseCompetitions(englishPdfStream, langEn).ToList();
        var compsHe = IsrOrgCompetitionParser.ParseCompetitions(hebrewPdfStream, langHeSync).ToList();

        for (int i = 0; i < compsEn.Count; i++)
        {
            var compEn = compsEn[i];
            var compHe = i < compsHe.Count
                ? compsHe[i]
                : throw new InvalidOperationException($"No matching HE event for '{compEn.Event}'");

            for (int j = 0; j < compEn.Results.Count; j++)
            {
                var rEn = compEn.Results[j];
                var rHe = j < compHe.Results.Count
                    ? compHe.Results[j]
                    : throw new InvalidOperationException(
                        $"No matching HE result for {compEn.Event} heat={rEn.Heat}, lane={rEn.Lane}");

                if (!string.IsNullOrEmpty(rEn.Time) && !string.IsNullOrEmpty(rHe.Time) && rEn.Time != rHe.Time)
                {
                    throw new InvalidOperationException($"Time mismatch EN='{rEn.Time}', HE='{rHe.Time}'");
                }

                int eventYear = AgeGroupHelper.ExtractYearFromDateString(compEn.Date);

                bool isRelay = rHe.IsRelay == true || rEn.IsRelay == true;
                var relaySwimmers = rHe.RelaySwimmers ?? rEn.RelaySwimmers;

                if (isRelay && relaySwimmers?.Count > 0)
                {
                    yield return CreateRelayResultBilingual(rEn, rHe, compEn, compHe, countryEn, eventYear, isMastersFile, isAward, relaySwimmers);
                    continue;
                }

                var age = DetermineAge(eventYear, rEn.BirthYear, compEn.EventStyleAge);
                var ageGroup = AgeGroupHelper.GetAgeGroup(age);

                yield return new Result(
                    Country: countryEn,
                    Competition: compHe.Competition,
                    IsMasters: (isMastersFile && age >= 25) ? "true" : "false",
                    IsAward: isAward,
                    AgeGroup: ageGroup,
                    Date: compEn.Date,
                    Event: compEn.Event,
                    EventStyleName: compEn.EventStyleName,
                    EventStyleLen: compEn.EventStyleLen,
                    EventStyleGender: compEn.EventStyleGender,
                    EventStyleAge: age.ToString(),
                    PoolType: compEn.PoolType,
                    Position: rEn.Position is int pi ? pi : null,
                    Heat: rEn.Heat,
                    Lane: rEn.Lane,
                    LastName: rHe.LastName,
                    FirstName: rHe.FirstName,
                    LastNameEn: !string.IsNullOrWhiteSpace(rEn.LastName) ? rEn.LastName : rHe.LastName,
                    FirstNameEn: !string.IsNullOrWhiteSpace(rEn.FirstName) ? rEn.FirstName : rHe.FirstName,
                    BirthYear: rEn.BirthYear,
                    Club: rHe.Club,
                    ClubEn: !string.IsNullOrWhiteSpace(rEn.Club) ? rEn.Club : rHe.Club,
                    Time: rEn.Time ?? string.Empty,
                    TimeFail: string.IsNullOrEmpty(rEn.Time),
                    TimeFailNote: rEn.TimeFailNote ?? rHe.TimeFailNote,
                    InternationalPoints: rEn.InternationalPoints,
                    Note: null,
                    IsRelay: rHe.IsRelay ?? rEn.IsRelay ?? false,
                    RelayTeamName: rHe.RelayTeamName ?? rEn.RelayTeamName,
                    RelaySwimmersName: null,
                    RelaySwimmers: rHe.RelaySwimmers ?? rEn.RelaySwimmers
                );
            }
        }
    }

    private static Result CreateRelayResult(
        IsrOrgResult rHe, IsrOrgCompetitionResult comp, string country,
        int eventYear, bool isMastersFile, bool isAward,
        string lastNameEn, string firstNameEn, string clubEn)
    {
        var firstSwimmer = rHe.RelaySwimmers!.First();

        var swimmerAge = DetermineAge(eventYear, firstSwimmer.BirthYear ?? 0, comp.EventStyleAge);
        var swimmerAgeGroup = AgeGroupHelper.GetAgeGroup(swimmerAge);
        var swimmerNames = string.Join(", ", rHe.RelaySwimmers!.Select(s => $"{s.FirstName} {s.LastName}".Trim()));

        return new Result(
            Country: country,
            Competition: comp.Competition,
            IsMasters: (isMastersFile && swimmerAge >= 25) ? "true" : "false",
            IsAward: isAward,
            AgeGroup: swimmerAgeGroup,
            Date: comp.Date,
            Event: comp.Event,
            EventStyleName: comp.EventStyleName,
            EventStyleLen: comp.EventStyleLen,
            EventStyleGender: comp.EventStyleGender,
            EventStyleAge: swimmerAge.ToString(),
            PoolType: comp.PoolType,
            Position: rHe.Position is int pi ? pi : null,
            Heat: rHe.Heat,
            Lane: rHe.Lane,
            LastName: firstSwimmer.LastName,
            FirstName: firstSwimmer.FirstName,
            LastNameEn: lastNameEn,
            FirstNameEn: firstNameEn,
            BirthYear: firstSwimmer.BirthYear ?? 0,
            Club: firstSwimmer.Club ?? rHe.Club,
            ClubEn: clubEn,
            Time: rHe.Time ?? string.Empty,
            TimeFail: rHe.Time == null,
            TimeFailNote: rHe.TimeFailNote,
            InternationalPoints: rHe.InternationalPoints,
            Note: null,
            IsRelay: true,
            RelayTeamName: rHe.RelayTeamName ?? rHe.Club,
            RelaySwimmersName: swimmerNames,
            RelaySwimmers: rHe.RelaySwimmers
        );
    }

    private static Result CreateRelayResultBilingual(
        IsrOrgResult rEn, IsrOrgResult rHe,
        IsrOrgCompetitionResult compEn, IsrOrgCompetitionResult compHe,
        string country, int eventYear, bool isMastersFile,
        bool isAward,
        List<RelaySwimmer> relaySwimmers)
    {
        var firstSwimmer = relaySwimmers.First();

        var swimmerAge = DetermineAge(eventYear, firstSwimmer.BirthYear ?? 0, compEn.EventStyleAge);
        var swimmerAgeGroup = AgeGroupHelper.GetAgeGroup(swimmerAge);
        var swimmerNames = string.Join(", ", relaySwimmers.Select(s => $"{s.FirstName} {s.LastName}".Trim()));

        return new Result(
            Country: country,
            Competition: compHe.Competition,
            IsMasters: (isMastersFile && swimmerAge >= 25) ? "true" : "false",
            IsAward: isAward,
            AgeGroup: swimmerAgeGroup,
            Date: compEn.Date,
            Event: compEn.Event,
            EventStyleName: compEn.EventStyleName,
            EventStyleLen: compEn.EventStyleLen,
            EventStyleGender: compEn.EventStyleGender,
            EventStyleAge: swimmerAge.ToString(),
            PoolType: compEn.PoolType,
            Position: rEn.Position is int pi ? pi : null,
            Heat: rEn.Heat,
            Lane: rEn.Lane,
            LastName: firstSwimmer.LastName,
            FirstName: firstSwimmer.FirstName,
            LastNameEn: string.Empty,
            FirstNameEn: string.Empty,
            BirthYear: firstSwimmer.BirthYear ?? 0,
            Club: firstSwimmer.Club ?? rHe.Club,
            ClubEn: !string.IsNullOrWhiteSpace(rEn.Club) ? rEn.Club : rHe.Club,
            Time: rEn.Time ?? string.Empty,
            TimeFail: string.IsNullOrEmpty(rEn.Time),
            TimeFailNote: rEn.TimeFailNote ?? rHe.TimeFailNote,
            InternationalPoints: rEn.InternationalPoints,
            Note: null,
            IsRelay: true,
            RelayTeamName: rHe.RelayTeamName ?? rEn.RelayTeamName ?? rHe.Club,
            RelaySwimmersName: swimmerNames,
            RelaySwimmers: relaySwimmers
        );
    }
}
