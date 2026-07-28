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

    /// <summary>
    /// Категория заплыва из заголовка протокола — то, что нельзя вывести из года рождения
    /// пловца. <c>EventStyleAge</c>/<c>AgeGroup</c> для этого не годятся: они считаются по
    /// возрасту и категорию затирают (у «50m Freestyle - Men Para» оставался возраст 49).
    ///
    /// Нормализация: <c>open</c> — взрослые Men/Women, <c>para</c> — паралимпийская
    /// программа, <c>mix</c> — смешанная, <c>U</c>+число — юниорская («U17»), возраст или
    /// группа — как в ивритских протоколах («12», «25-29»).
    /// </summary>
    internal static string? NormalizeEventCategory(string? raw, string? gender = null)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var v = raw.Trim();

        if (v.Equals("para", StringComparison.OrdinalIgnoreCase)) return "para";

        // Юниорские заголовки приходят как "U17" либо уже как "17" (ParseEnCategory срезает U).
        // Различить «U17 Boys» и ивритский «גיל 17» по одному числу нельзя, и не нужно:
        // в обоих случаях это возрастная категория заплыва с этим числом.
        var age = v.Equals("open", StringComparison.OrdinalIgnoreCase)
            ? "open"
            : v.TrimStart('U', 'u');

        // Смешанные заплывы: пол в них "none" (ParseEnCategory), а возрастная часть остаётся
        // своя — «MIX 18-99» и «MIX U17» это РАЗНЫЕ программы. Поэтому префикс, а не просто
        // "mix": иначе две программы схлопнулись бы в одну категорию.
        bool isMix = v.StartsWith("mix", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(gender, "none", StringComparison.OrdinalIgnoreCase)
                     || (gender?.StartsWith("mix", StringComparison.OrdinalIgnoreCase) ?? false);
        if (isMix)
        {
            var mixAge = v.StartsWith("mix", StringComparison.OrdinalIgnoreCase)
                ? v[3..].Trim().TrimStart('U', 'u')
                : age;
            return string.IsNullOrEmpty(mixAge) || mixAge == "open" ? "mix" : $"mix-{mixAge}";
        }

        return age;
    }

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
                    RelaySwimmers: rHe.RelaySwimmers,
                    EventCategory: NormalizeEventCategory(comp.EventStyleAge, comp.EventStyleGender)
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

        return MergeBilingual(compsEn, compsHe, countryEn, isMastersFile, isAward, poolOverride);
    }

    /// <summary>
    /// Склейка распарсенных EN- и HE-версий одного протокола. Вынесена из
    /// <see cref="ParseBilingual"/>, чтобы тестировать ресинк без PDF-фикстур.
    /// </summary>
    internal static IEnumerable<Result> MergeBilingual(
        IReadOnlyList<IsrOrgCompetitionResult> compsEn,
        IReadOnlyList<IsrOrgCompetitionResult> compsHe,
        string countryEn, bool isMastersFile, bool isAward, string? poolOverride)
    {
        for (int i = 0; i < compsEn.Count; i++)
        {
            var compEn = compsEn[i];
            var compHe = i < compsHe.Count
                ? compsHe[i]
                : throw new InvalidOperationException($"No matching HE event for '{compEn.Event}'");

            // Толерантный merge вместо жёсткой склейки по индексу: loglig иногда ТЕРЯЕТ
            // строку в одном из языковых рендеров того же протокола (реальный кейс —
            // безвременной результат, выпавший только из EN-версии). Пара строк
            // опознаётся по (Heat, Lane) — уникальны внутри события; при рассинхроне
            // одна сторона пропускается вперёд (осиротевшая строка идёт одноязычной),
            // и только если ресинк на соседней строке не находится — это настоящий
            // рассинхрон файлов, кидаем ошибку как раньше.
            static bool SameSlot(IsrOrgResult a, IsrOrgResult b) => a.Heat == b.Heat && a.Lane == b.Lane;

            var listEn = compEn.Results;
            var listHe = compHe.Results;
            int ie = 0, ih = 0;
            while (ie < listEn.Count || ih < listHe.Count)
            {
                bool hasEn = ie < listEn.Count;
                bool hasHe = ih < listHe.Count;
                IsrOrgResult rEn, rHe;

                if (hasEn && hasHe && SameSlot(listEn[ie], listHe[ih]))
                {
                    rEn = listEn[ie++];
                    rHe = listHe[ih++];
                }
                else if (hasHe && (!hasEn || (ih + 1 < listHe.Count && SameSlot(listEn[ie], listHe[ih + 1]))))
                {
                    // Строка есть только в HE-версии — берём её за обе стороны
                    // (EN-имена сфоллбечатся на ивритские ниже).
                    rEn = rHe = listHe[ih++];
                }
                else if (hasEn && (!hasHe || (ie + 1 < listEn.Count && SameSlot(listEn[ie + 1], listHe[ih]))))
                {
                    // Строка есть только в EN-версии.
                    rEn = rHe = listEn[ie++];
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Results diverged in '{compEn.Event}': EN heat={listEn[ie].Heat} lane={listEn[ie].Lane} " +
                        $"vs HE heat={listHe[ih].Heat} lane={listHe[ih].Lane} — файлы не совпадают, ресинк не найден.");
                }

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
                    RelaySwimmers: rHe.RelaySwimmers ?? rEn.RelaySwimmers,
                    EventCategory: NormalizeEventCategory(compEn.EventStyleAge, compEn.EventStyleGender)
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
            RelaySwimmers: rHe.RelaySwimmers,
            EventCategory: NormalizeEventCategory(comp.EventStyleAge, comp.EventStyleGender)
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
            RelaySwimmers: relaySwimmers,
            EventCategory: NormalizeEventCategory(compEn.EventStyleAge, compEn.EventStyleGender)
        );
    }
}
