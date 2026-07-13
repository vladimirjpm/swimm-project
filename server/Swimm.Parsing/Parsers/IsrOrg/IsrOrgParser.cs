using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Swimm.Parsing.Models;
using Swimm.Parsing.Helpers;

namespace Swimm.Parsing.Parsers.IsrOrg;

public class IsrOrgParser : IFormatParser
{
    public string FormatName => "IsrOrg";

    public IEnumerable<Result> Parse(ParseRequest request)
    {
        // Бассейн выбирается в UI импорта и имеет приоритет над дефолтом парсера
        // (протокол PDF длину бассейна не указывает — парсер ставит "25m" условно).
        var poolOverride = string.IsNullOrWhiteSpace(request.PoolType) ? null : request.PoolType.Trim();

        // Страна и язык тоже выбираются в UI и имеют приоритет над легаси-конвенцией
        // имени файла «*_{COUNTRY}_{LANG}.pdf» (она остаётся fallback-ом).
        var countryOverride = string.IsNullOrWhiteSpace(request.Country)
            ? null : request.Country.Trim().ToUpperInvariant();
        var langOverride = string.IsNullOrWhiteSpace(request.Language)
            ? null : request.Language.Trim().ToLowerInvariant();

        if (request.SecondaryStream == null || string.IsNullOrWhiteSpace(request.SecondaryFileName))
        {
            return ParseHebrewOnly(request.PrimaryStream, request.PrimaryFileName, request.IsAward,
                poolOverride, countryOverride, langOverride);
        }

        // Двуязычная пара: роли файлов определяет язык основного (из UI);
        // без выбора — легаси-конвенция «основной = HE, дополнительный = EN».
        if (langOverride == "en")
        {
            return ParseBilingual(
                request.PrimaryStream, request.PrimaryFileName,
                request.SecondaryStream, request.SecondaryFileName!,
                request.IsAward, poolOverride, countryOverride,
                englishLang: "en", hebrewLang: "he");
        }

        return ParseBilingual(
            request.SecondaryStream, request.SecondaryFileName!,
            request.PrimaryStream, request.PrimaryFileName,
            request.IsAward, poolOverride, countryOverride,
            englishLang: langOverride != null ? "en" : null,
            hebrewLang: langOverride);
    }

    /// <summary>Страна: выбор из UI приоритетнее предпоследнего сегмента имени файла.</summary>
    internal static string ResolveCountry(string fileName, string? countryOverride)
    {
        if (!string.IsNullOrWhiteSpace(countryOverride)) return countryOverride.Trim().ToUpperInvariant();
        var parts = Path.GetFileNameWithoutExtension(fileName).Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^2] : string.Empty;
    }

    /// <summary>Язык: выбор из UI приоритетнее последнего сегмента имени файла.</summary>
    internal static string ResolveLanguage(string fileName, string? languageOverride)
    {
        if (!string.IsNullOrWhiteSpace(languageOverride)) return languageOverride.Trim().ToLowerInvariant();
        var parts = Path.GetFileNameWithoutExtension(fileName).Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 1 ? parts[^1] : string.Empty;
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

    private static IEnumerable<Result> ParseHebrewOnly(Stream hebrewPdfStream, string hebrewFileName, bool isAward,
        string? poolOverride, string? countryOverride, string? langOverride)
    {
        var country = ResolveCountry(hebrewFileName, countryOverride);
        var langHe = ResolveLanguage(hebrewFileName, langOverride);

        var isMastersFile = Path.GetFileNameWithoutExtension(hebrewFileName)
                            .Contains("masters", StringComparison.OrdinalIgnoreCase);

        var comps = IsrOrgCompetitionParser.ParseCompetitions(hebrewPdfStream, langHe);
        return MapHebrewOnly(comps, country, isMastersFile, isAward, poolOverride);
    }

    /// <summary>
    /// Маппинг распарсенных соревнований (HE-only) в результаты импорта. Вынесено из
    /// <see cref="ParseHebrewOnly"/>, чтобы тестировать без PDF. <paramref name="poolOverride"/>
    /// (бассейн из UI) имеет приоритет над дефолтом парсера <c>comp.PoolType</c>.
    /// </summary>
    internal static IEnumerable<Result> MapHebrewOnly(
        IEnumerable<IsrOrgCompetitionResult> comps, string country, bool isMastersFile,
        bool isAward, string? poolOverride)
    {
        foreach (var comp in comps)
        {
            foreach (var rHe in comp.Results)
            {
                int eventYear = AgeGroupHelper.ExtractYearFromDateString(comp.Date);

                if (rHe.IsRelay == true && rHe.RelaySwimmers?.Count > 0)
                {
                    yield return CreateRelayResult(rHe, comp, country, eventYear, isMastersFile, isAward,
                        lastNameEn: string.Empty, firstNameEn: string.Empty, clubEn: string.Empty, poolOverride);
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
                    PoolType: poolOverride ?? comp.PoolType,
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
        bool isAward, string? poolOverride, string? countryOverride = null,
        string? englishLang = null, string? hebrewLang = null)
    {
        var countryEn = ResolveCountry(englishFileName, countryOverride);
        var langEn = ResolveLanguage(englishFileName, englishLang);
        var langHeSync = ResolveLanguage(hebrewFileName, hebrewLang);

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
                    yield return CreateRelayResultBilingual(rEn, rHe, compEn, compHe, countryEn, eventYear, isMastersFile, isAward, relaySwimmers, poolOverride);
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
                    PoolType: poolOverride ?? compEn.PoolType,
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
        string lastNameEn, string firstNameEn, string clubEn, string? poolOverride)
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
            PoolType: poolOverride ?? comp.PoolType,
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
        List<RelaySwimmer> relaySwimmers, string? poolOverride)
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
            PoolType: poolOverride ?? compEn.PoolType,
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
