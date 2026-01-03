using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Swimm.API.Services.Models;
using Swimm.API.Services.Helpers;
using Swimm.API.Services.Parsers;

namespace Swimm.API.Services;

public static class Parser
{
    public static IEnumerable<Result> Parse(
        Stream? englishPdfStream, string? englishFileName,
        Stream hebrewPdfStream, string hebrewFileName)
    {
        // Если нет английской версии
        if (englishPdfStream == null || string.IsNullOrWhiteSpace(englishFileName))
        {
            return ParseHebrewOnly(hebrewPdfStream, hebrewFileName);
        }

        // Синхронизация EN + HE
        return ParseBilingual(englishPdfStream, englishFileName, hebrewPdfStream, hebrewFileName);
    }

    /// <summary>
    /// Определяет возраст участника.
    /// 
    /// Логика 1 (приоритетная): Если есть год рождения (שנת לידה) - вычисляем возраст как (год события - год рождения).
    /// Логика 2 (fallback): Если года рождения нет - берём возраст из заголовка события (например, "16" из "בנים 16").
    /// </summary>
    /// <param name="eventYear">Год проведения события</param>
    /// <param name="birthYear">Год рождения участника (0 если неизвестен)</param>
    /// <param name="eventStyleAge">Возраст из заголовка события (например, "16" или "12-13")</param>
    /// <returns>Возраст участника</returns>
    private static int DetermineAge(int eventYear, int birthYear, string? eventStyleAge)
    {
        // Логика 1: Если есть год рождения - вычисляем точный возраст
        if (birthYear > 0 && eventYear > 0)
        {
            return eventYear - birthYear;
        }

        // Логика 2: Если года рождения нет - пытаемся извлечь из заголовка события
        if (!string.IsNullOrWhiteSpace(eventStyleAge))
        {
            // Если формат "12-13", берём первое число
            var agePart = eventStyleAge.Split('-')[0];
            if (int.TryParse(agePart, out int parsedAge) && parsedAge > 0)
            {
                return parsedAge;
            }
        }

        // Не удалось определить возраст
        return 0;
    }

    private static IEnumerable<Result> ParseHebrewOnly(Stream hebrewPdfStream, string hebrewFileName)
    {
        var heParts = Path.GetFileNameWithoutExtension(hebrewFileName)
                          .Split('_', StringSplitOptions.RemoveEmptyEntries);
        var country = heParts.Length >= 2 ? heParts[^2] : string.Empty;
        var langHe = heParts.Length >= 1 ? heParts[^1] : string.Empty;

        var isMastersFile = Path.GetFileNameWithoutExtension(hebrewFileName)
                            .Contains("masters", StringComparison.OrdinalIgnoreCase);

        foreach (var comp in CompetitionParser.ParseCompetitions(hebrewPdfStream, langHe))
        {
            foreach (var rHe in comp.Results)
            {
                int eventYear = AgeGroupHelper.ExtractYearFromDateString(comp.Date);

                // Relay: создаём одну запись на команду
                if (rHe.IsRelay == true && rHe.RelaySwimmers?.Count > 0)
                {
                    yield return CreateRelayResult(rHe, comp, country, eventYear, isMastersFile,
                        lastNameEn: string.Empty, firstNameEn: string.Empty, clubEn: string.Empty);
                    continue;
                }

                // Определяем возраст по году рождения (приоритет) или по заголовку события (fallback)
                var age = DetermineAge(eventYear, rHe.BirthYear, comp.EventStyleAge);
                var ageGroup = AgeGroupHelper.GetAgeGroup(age);

                yield return new Result(
                    Country: country,
                    Competition: comp.Competition,
                    IsMasters: (isMastersFile && age >= 25) ? "true" : "false",
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
        Stream hebrewPdfStream, string hebrewFileName)
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

        var compsEn = CompetitionParser.ParseCompetitions(englishPdfStream, langEn).ToList();
        var compsHe = CompetitionParser.ParseCompetitions(hebrewPdfStream, langHeSync).ToList();

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

                // Relay
                bool isRelay = rHe.IsRelay == true || rEn.IsRelay == true;
                var relaySwimmers = rHe.RelaySwimmers ?? rEn.RelaySwimmers;

                if (isRelay && relaySwimmers?.Count > 0)
                {
                    yield return CreateRelayResultBilingual(rEn, rHe, compEn, compHe, countryEn, eventYear, isMastersFile, relaySwimmers);
                    continue;
                }

                // Определяем возраст по году рождения (приоритет) или по заголовку события (fallback)
                var age = DetermineAge(eventYear, rEn.BirthYear, compEn.EventStyleAge);
                var ageGroup = AgeGroupHelper.GetAgeGroup(age);

                yield return new Result(
                    Country: countryEn,
                    Competition: compHe.Competition,
                    IsMasters: (isMastersFile && age >= 25) ? "true" : "false",
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
        PDF_Result rHe, PDF_CompetitionResult comp, string country,
        int eventYear, bool isMastersFile,
        string lastNameEn, string firstNameEn, string clubEn)
    {
        var firstSwimmer = rHe.RelaySwimmers!.First();
        
        // Для эстафеты: возраст по году рождения первого участника или из заголовка
        var swimmerAge = DetermineAge(eventYear, firstSwimmer.BirthYear ?? 0, comp.EventStyleAge);
        var swimmerAgeGroup = AgeGroupHelper.GetAgeGroup(swimmerAge);
        var swimmerNames = string.Join(", ", rHe.RelaySwimmers!.Select(s => $"{s.FirstName} {s.LastName}".Trim()));

        return new Result(
            Country: country,
            Competition: comp.Competition,
            IsMasters: (isMastersFile && swimmerAge >= 25) ? "true" : "false",
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
            InternationalPoints: rHe.InternationalPoints,
            Note: null,
            IsRelay: true,
            RelayTeamName: rHe.RelayTeamName ?? rHe.Club,
            RelaySwimmersName: swimmerNames,
            RelaySwimmers: rHe.RelaySwimmers
        );
    }

    private static Result CreateRelayResultBilingual(
        PDF_Result rEn, PDF_Result rHe,
        PDF_CompetitionResult compEn, PDF_CompetitionResult compHe,
        string country, int eventYear, bool isMastersFile,
        List<RelaySwimmer> relaySwimmers)
    {
        var firstSwimmer = relaySwimmers.First();
        
        // Для эстафеты: возраст по году рождения первого участника или из заголовка
        var swimmerAge = DetermineAge(eventYear, firstSwimmer.BirthYear ?? 0, compEn.EventStyleAge);
        var swimmerAgeGroup = AgeGroupHelper.GetAgeGroup(swimmerAge);
        var swimmerNames = string.Join(", ", relaySwimmers.Select(s => $"{s.FirstName} {s.LastName}".Trim()));

        return new Result(
            Country: country,
            Competition: compHe.Competition,
            IsMasters: (isMastersFile && swimmerAge >= 25) ? "true" : "false",
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
            InternationalPoints: rEn.InternationalPoints,
            Note: null,
            IsRelay: true,
            RelayTeamName: rHe.RelayTeamName ?? rEn.RelayTeamName ?? rHe.Club,
            RelaySwimmersName: swimmerNames,
            RelaySwimmers: relaySwimmers
        );
    }
}