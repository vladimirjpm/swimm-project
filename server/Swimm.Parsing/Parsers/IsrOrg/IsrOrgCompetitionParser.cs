// -*- coding: utf-8 -*-
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using Swimm.Parsing.Models;
using Swimm.Parsing.Helpers;

namespace Swimm.Parsing.Parsers.IsrOrg;

public static class IsrOrgCompetitionParser
{
    private static Regex? _headerRxHE;
    private static Regex? _headerRxEN;
    private static Regex? _headerRxHESimple;
    private static Regex? _genderAgeLineRxHE;
    private static Regex? _fullResultRx;
    private static Regex? _relayHeaderRxHE;
    private static Regex? _relayHeaderRxHE2;
    private static Regex? _relayTeamLineRxHE;
    private static Regex? _dateLineRx;

    private const string GenderPatternOriginal =
        "\u05D1\u05E0\u05D5\u05EA|\u05D1\u05E0\u05D9\u05DD|\u05E0\u05E9\u05D9\u05DD|\u05D2\u05D1\u05E8\u05D9\u05DD";

    private const string GenderPatternReversed =
        "\u05EA\u05D5\u05E0\u05D1|\u05DD\u05D9\u05E0\u05D1|\u05DD\u05D9\u05E9\u05E0|\u05DD\u05D9\u05E8\u05D1\u05D2";

    private const string HebrewMix = "\u05DE\u05D9\u05E7\u05E1";
    private const string HebrewMixReversed = "\u05E1\u05E7\u05D9\u05DE";

    private const string HebrewKlali = "\u05DB\u05DC\u05DC\u05D9";
    private const string HebrewKlaliReversed = "\u05D9\u05DC\u05DC\u05DB";

    private const string GenderPatternWithMix =
        GenderPatternOriginal + "|" + GenderPatternReversed + "|" + HebrewMix + "|" + HebrewMixReversed;

    private static Regex HeaderRxHE => _headerRxHE ??= new Regex(
        @"^(?<len>\d+)\s+(?<style>.+?)\s*-\s*(?<gender>" +
        GenderPatternOriginal + "|" + GenderPatternReversed +
        @")\s+(?<age>\d+(-\d+)?)$",
        RegexOptions.Compiled);

    private static Regex HeaderRxHESimple => _headerRxHESimple ??= new Regex(
        @"^(?<len>\d+)\s+(?<style>[\u0590-\u05FF\s]+)$",
        RegexOptions.Compiled);

    private static Regex GenderAgeLineRxHE => _genderAgeLineRxHE ??= new Regex(
        @"^(?<gender>" +
        GenderPatternOriginal + "|" + GenderPatternReversed +
        @")\s+(?<age>\d+(-\d+)?)$",
        RegexOptions.Compiled);

    private static Regex? _mastersAgeLineRxHE;
    private static Regex MastersAgeLineRxHE => _mastersAgeLineRxHE ??= new Regex(
        @"^\u05DE\u05D0\u05E1\u05D8\u05E8\u05E1\s+(?<gender>[\u05D0-\u05EA])\s+(?<age>\d+(?:-\d+)?)$",
        RegexOptions.Compiled);

    private static Regex? _mastersRelayAgeLineRxHE;
    private static Regex MastersRelayAgeLineRxHE => _mastersRelayAgeLineRxHE ??= new Regex(
        @"^\u05DE\u05D0\u05E1\u05D8\u05E8\u05E1\s+\u05E9\u05DC\u05D9\u05D7(?:\u05D5\u05EA|\u05D9\u05DD)?\s+(?<age>\d+(?:-\d+)?)$",
        RegexOptions.Compiled);

    private static Regex HeaderRxEN => _headerRxEN ??= new Regex(
        @"^(?<len>\d+m?)\s+(?<style>.+?)\s*-\s*(?<gender>female|male|girls|boys|women|men)\s+(?<age>\d+(-\d+)?)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Regex? _headerRxENinHE;
    // Двуязычные PDF (Маккабиада и пр.): «обвязка» страницы на иврите, поэтому файл
    // разбирается по HE-ветке, но заголовок заплыва — на английском в порядке
    // "50m Freestyle - U17 Girls" (<len> <style> - <ageband> <gender>).
    // Матчим по СЫРОЙ строке (до RTL-реверса), т.к. содержимое латиницей.
    private static Regex HeaderRxENinHE => _headerRxENinHE ??= new Regex(
        @"^(?<len>\d+)m?\s+(?<style>.+?)\s*-\s*(?<age>U?\d+(?:-\d+)?|Open)\s+(?<gender>Girls|Boys|Women|Men|Female|Male|Mixed)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Английский заголовок заплыва БЕЗ пола/возраста в строке (masters-экспорт
    // Maccabiah ARENA): "400m Freestyle" / "200m Individual Medley". Пол и возраст
    // приходят отдельной ивритской строкой "מאסטרס <пол> <возраст>". Матчим по СЫРОЙ.
    private const string EnStylePattern =
        @"Freestyle|Backstroke|Breaststroke|Butterfly|Individual\s+Medley|Medley";

    private static Regex? _headerEnNoGenderInHE;
    private static Regex HeaderEnNoGenderInHE => _headerEnNoGenderInHE ??= new Regex(
        @"^(?<len>\d+)m?\s+(?<style>" + EnStylePattern + @")$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Английский заголовок ЭСТАФЕТЫ без пола/возраста: "4X50m Medley Relay".
    private static Regex? _relayHeaderEnInHE;
    private static Regex RelayHeaderEnInHE => _relayHeaderEnInHE ??= new Regex(
        @"^(?<legs>\d+)\s*[Xx]\s*(?<len>\d+)m?\s+(?<style>" + EnStylePattern + @")\s+Relay$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Regex FullResultRx => _fullResultRx ??= new Regex(
        @"^(-|\d+)\s+\d+\s+\d+.*(\d{2}:\d{2}\.\d{2}|NS|DQ)\s+\d+$",
        RegexOptions.Compiled);

    // EN-строка команды эстафеты: "<heat> <lane> <team> <time> Rank <pos>".
    // Команда бывает пустой (перенос названия на соседние строки) → опциональна.
    private static Regex? _relayTeamLineEn;
    private static Regex RelayTeamLineEn => _relayTeamLineEn ??= new Regex(
        @"^(?<heat>\d+)\s+(?<lane>\d+)\s+(?:(?<team>.+?)\s+)?(?<time>\d{2}:\d{2}\.\d{1,2}|DQ|NS)\s+Rank\s+(?<pos>\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Каноничное имя английского стиля → к нижнему регистру + individual_medley.
    private static string CanonEnStyle(string style)
    {
        var s = Regex.Replace(style.Trim(), @"\s+", " ").ToLowerInvariant();
        return s switch
        {
            "individual medley" => "individual_medley",
            "medley" => "individual_medley",
            _ => s
        };
    }

    // Чистый EN-экспорт (Maccabiah 2026, файл _IL_EN): заголовок заплыва —
    // "<len>m <style> - <категория>", категория варьируется: "U17 Girls",
    // "Women", "Men Para", а у эстафет "<legs>X<len>m <style> Relay|Mix - MIX 18-99".
    private static Regex? _headerEnFull;
    private static Regex HeaderEnFull => _headerEnFull ??= new Regex(
        @"^(?<len>\d+)m?\s+(?<style>" + EnStylePattern + @")\s*-\s*(?<cat>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Regex? _relayHeaderEnFull;
    private static Regex RelayHeaderEnFull => _relayHeaderEnFull ??= new Regex(
        @"^(?<legs>\d+)\s*[Xx]\s*(?<len>\d+)m?\s+(?<style>" + EnStylePattern + @")\s+(?:Relay|Mix)\s*-\s*(?<cat>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Разбор правой части EN-заголовка ("U17 Girls" / "Women" / "MIX 18-99" /
    // "Men Para") в нормализованные пол и возраст. Para/open без числа → возраст
    // "para"/"open" (чтобы Men и Men Para не схлопнулись в одно событие).
    private static (string gender, string age) ParseEnCategory(string cat)
    {
        cat = Regex.Replace(cat.Trim(), @"\s+", " ");
        bool para = Regex.IsMatch(cat, @"\bPara\b", RegexOptions.IgnoreCase);

        string gender = "none";
        string? age = null;
        foreach (var t in cat.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (t.ToLowerInvariant())
            {
                case "girls" or "women" or "female": gender = "female"; break;
                case "boys" or "men" or "male": gender = "male"; break;
                case "mix" or "mixed": gender = "none"; break;
                default:
                    if (Regex.IsMatch(t, @"^U?\d+(-\d+)?$", RegexOptions.IgnoreCase))
                        age = t.TrimStart('U', 'u');
                    break;
            }
        }

        return (gender, age ?? (para ? "para" : "open"));
    }

    private static Regex RelayHeaderRxHE => _relayHeaderRxHE ??= new Regex(
        @"^(?<legs>\d+)\s*[Xx]\s*(?<len>\d+)\s+(?<style>.+?)\s+" +
        "\u05E9\u05DC\u05D9\u05D7(?:\u05D9\u05DD|\u05D5\u05EA)?\\s*" +
        "(?:" + HebrewMix + "|" + HebrewMixReversed + ")?\\s*" +
        @"-\s*(?<gender>" +
        "\u05E0|\u05D6|" + GenderPatternWithMix +
        @")\s+(?<age>\d+(?:-\d+)?)$",
        RegexOptions.Compiled);

    private static Regex RelayHeaderRxHE2 => _relayHeaderRxHE2 ??= new Regex(
        @"^(?<len>\d+)\s*[Xx]\s*(?<legs>\d+)\s+(?<style>.+?)\s+" +
        "\u05E9\u05DC\u05D9\u05D7(?:\u05D9\u05DD|\u05D5\u05EA)?\\s*" +
        "(?:" + HebrewMix + "|" + HebrewMixReversed + ")?\\s*" +
        @"-\s*(?<gender>" +
        "\u05E0|\u05D6|" + GenderPatternWithMix +
        @")\s+(?<age>\d+(?:-\d+)?)$",
        RegexOptions.Compiled);

    private static Regex RelayTeamLineRxHE => _relayTeamLineRxHE ??= new Regex(
        @"^(?<heat>\d+)\s+(?<lane>\d+)\s+(?<team>.+?)\s+(?<time>\d{2}:\d{2}\.\d{1,2}|DQ|NS)\s+" +
        "\u05DE\u05D9\u05E7\u05D5\u05DD" +
        @"\s+(?<pos>\d+)\s*$",
        RegexOptions.Compiled);

    private static Regex DateLineRx => _dateLineRx ??= new Regex(
        @"(?<date>\d{2}/\d{2}/\d{4})$",
        RegexOptions.Compiled);

    private const string HebrewRelay = "\u05E9\u05DC\u05D9\u05D7";

    private static List<string> _debugLog = new();

    public static string GetDebugLog()
    {
        return string.Join("\n", _debugLog);
    }

    public static IEnumerable<IsrOrgCompetitionResult> ParseCompetitions(Stream pdfStream, string language)
    {
        var results = new List<IsrOrgCompetitionResult>();
        _debugLog.Clear();

        try
        {
            foreach (var result in ParseCompetitionsInternal(pdfStream, language))
            {
                results.Add(result);
            }
        }
        catch (Exception ex)
        {
            var debugInfo = string.Join("\n", _debugLog.TakeLast(50));
            throw new InvalidOperationException(
                $"Error in ParseCompetitions (language={language}): {ex.Message}\n\n--- DEBUG LOG (last 50 lines) ---\n{debugInfo}", ex);
        }

        if (results.Count == 0)
        {
            var debugInfo = string.Join("\n", _debugLog);
            throw new InvalidOperationException(
                $"No competitions found in PDF (language={language}).\n\n--- DEBUG LOG ---\n{debugInfo}");
        }

        return results;
    }

    private static void Log(string message)
    {
        _debugLog.Add($"[{_debugLog.Count + 1}] {message}");
    }

    private static string NormalizeIfHe(string s, bool isHE) =>
        isHE ? HebrewTextHelper.NormalizeHebrewLine(s) : s;

    // Строка-фрагмент перенесённой фамилии: одно "слово" из букв (латиница/иврит),
    // без цифр и пробелов — т.е. не заголовок, не строка результата, не дата.
    private static bool IsNameFragment(string s)
    {
        s = s.Trim();
        return s.Length > 0 && Regex.IsMatch(s, @"^[\p{L}][\p{L}'\-]*$");
    }

    private static IEnumerable<IsrOrgCompetitionResult> ParseCompetitionsInternal(Stream pdfStream, string language)
    {
        Log($"Starting parse, language={language}");

        using var doc = PdfDocument.Open(pdfStream);
        Log($"PDF opened, pages={doc.NumberOfPages}");

        // Извлекаем текст страниц в строки (RTL-порядок восстанавливается позже, при нормализации).
        // Вынесено отдельно, чтобы ядро разбора (ParseLines) можно было тестировать на голых строках.
        var pages = new List<IReadOnlyList<string>>();
        foreach (var page in doc.GetPages())
        {
            var words = page.GetWords();
            var lines = words
                .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 2.0) * 2.0)
                .OrderByDescending(g => g.Key)
                .Select(g => string.Join(' ', g.OrderBy(w => w.BoundingBox.Left)
                    .Select(w => w.Text)))
                .ToList();
            pages.Add(lines);
        }

        return ParseLines(pages, language);
    }

    /// <summary>
    /// Ядро разбора: принимает уже извлечённые из PDF строки (по странице на список).
    /// Тестируется напрямую на строках — см. Swimm.Tests/IsrOrgCompetitionParserTests.cs.
    /// </summary>
    internal static IEnumerable<IsrOrgCompetitionResult> ParseLines(
        IReadOnlyList<IReadOnlyList<string>> pages, string language)
    {
        bool isHE = language.Equals("HE", StringComparison.OrdinalIgnoreCase);
        var headerRx = isHE ? HeaderRxHE : HeaderRxEN;

        Log($"ParseLines start, language={language}, isHE={isHE}, pages={pages.Count}");

        IsrOrgCompetitionResult? current = null;

        bool currentIsRelay = false;
        int currentRelayLegs = 0;
        string dat_relay = "";

        string? pendingEventLen = null;
        string? pendingEventStyle = null;
        string? pendingEventLine = null;

        string? pendingRelayStyleHe = null;
        string? pendingRelayLen = null;
        int pendingRelayLegs = 0;

        IsrOrgResult? pendingRelayResult = null;
        List<RelaySwimmer>? pendingSwimmers = null;
        int pendingSwimmersOrder = 1;

        for (int pageIdx = 0; pageIdx < pages.Count; pageIdx++)
        {
            int pageNumber = pageIdx + 1;
            var lines = pages[pageIdx];

            Log($"--- Page {pageNumber} ---");
            Log($"Page {pageNumber}: {lines.Count} lines extracted");

            for (int i = 0; i < lines.Count; i++)
            {
                var raw = lines[i].Trim();
                var line = isHE ? HebrewTextHelper.NormalizeHebrewLine(raw) : raw;

                Log($"L{i}: raw='{raw.Substring(0, Math.Min(60, raw.Length))}...' norm='{line.Substring(0, Math.Min(60, line.Length))}...'");

                if (isHE && pendingRelayLen != null)
                {
                    var mastersAgeMatch = MastersAgeLineRxHE.Match(line);
                    var mastersRelayAgeMatch = MastersRelayAgeLineRxHE.Match(line);

                    if (mastersAgeMatch.Success || mastersRelayAgeMatch.Success)
                    {
                        string ageGroup;
                        string genderNorm;

                        if (mastersAgeMatch.Success)
                        {
                            ageGroup = mastersAgeMatch.Groups["age"].Value;
                            var genderRaw = mastersAgeMatch.Groups["gender"].Value;
                            genderNorm = HebrewTextHelper.NormalizeGenderHE(genderRaw.Trim());
                            Log($"  -> MATCH MastersAge for pending relay: gender={genderRaw}, age={ageGroup}");
                        }
                        else
                        {
                            ageGroup = mastersRelayAgeMatch.Groups["age"].Value;
                            genderNorm = "none";
                            Log($"  -> MATCH MastersRelayAge for pending relay: age={ageGroup}, gender=none (mixed)");
                        }

                        if (current != null) yield return current;

                        var styleNorm = HebrewTextHelper.StyleMapHE.GetValueOrDefault(pendingRelayStyleHe!, pendingRelayStyleHe!);
                        styleNorm = HebrewTextHelper.NormalizeStyleName(styleNorm);

                        currentIsRelay = true;
                        currentRelayLegs = pendingRelayLegs;

                        current = new IsrOrgCompetitionResult(
                            Competition: HebrewTextHelper.NormalizeHebrewLine(lines[0].Trim()),
                            AgeGroup: ageGroup,
                            Date: dat_relay,
                            Event: pendingEventLine ?? string.Empty,
                            EventStyleName: styleNorm,
                            EventStyleLen: pendingRelayLen,
                            EventStyleGender: genderNorm,
                            EventStyleAge: ageGroup,
                            PoolType: "25m",
                            Results: new List<IsrOrgResult>()
                        );

                        Log($"  -> NEW RELAY EVENT (masters continuation): {current.Event}, gender={genderNorm}");

                        pendingRelayStyleHe = null;
                        pendingRelayLen = null;
                        pendingRelayLegs = 0;
                        pendingEventLine = null;
                        continue;
                    }
                }

                if (pendingRelayResult != null && pendingSwimmers != null && current != null)
                {
                    bool isNewHeader = RelayHeaderRxHE.IsMatch(line) || RelayHeaderRxHE2.IsMatch(line) ||
                                       headerRx.IsMatch(line) || (isHE && HeaderRxHESimple.IsMatch(line));
                    bool isNewTeam = RelayTeamLineRxHE.IsMatch(line);

                    if (!isNewHeader && !isNewTeam && pendingSwimmers.Count < currentRelayLegs)
                    {
                        if (Regex.IsMatch(line, @"\b\d{4}\b"))
                        {
                            pendingSwimmers.Add(IsrOrgResultLineParser.ParseRelaySwimmerLine(line, pendingSwimmersOrder));
                            pendingSwimmersOrder++;

                            if (pendingSwimmers.Count >= currentRelayLegs)
                            {
                                current.Results.Add(CreateRelayResult(pendingRelayResult, pendingSwimmers));
                                pendingRelayResult = null;
                                pendingSwimmers = null;
                            }
                            continue;
                        }
                    }
                    else if (isNewHeader || isNewTeam)
                    {
                        if (pendingSwimmers.Count > 0)
                        {
                            current.Results.Add(CreateRelayResult(pendingRelayResult, pendingSwimmers));
                        }
                        pendingRelayResult = null;
                        pendingSwimmers = null;
                    }
                }

                if (isHE && pendingEventLen != null)
                {
                    Log($"  -> Checking for gender/age (pending: len={pendingEventLen}, style={pendingEventStyle})");
                    var genderAgeMatch = GenderAgeLineRxHE.Match(line);
                    var mastersAgeMatch = MastersAgeLineRxHE.Match(line);
                    if (genderAgeMatch.Success || mastersAgeMatch.Success)
                    {
                        var ageGroupVal = genderAgeMatch.Success
                            ? genderAgeMatch.Groups["age"].Value
                            : mastersAgeMatch.Groups["age"].Value;
                        var genderRaw = genderAgeMatch.Success
                            ? genderAgeMatch.Groups["gender"].Value
                            : mastersAgeMatch.Groups["gender"].Value;

                        Log($"  -> MATCH GenderAge: gender={genderRaw}, age={ageGroupVal}");

                        if (current != null)
                        {
                            Log($"  -> Yielding previous event: {current.Event}");
                            yield return current;
                        }

                        var genderNorm = HebrewTextHelper.NormalizeGenderHE(genderRaw.Trim());
                        var styleNorm = HebrewTextHelper.StyleMapHE.GetValueOrDefault(pendingEventStyle!, pendingEventStyle!);
                        styleNorm = HebrewTextHelper.NormalizeStyleName(styleNorm);

                        current = new IsrOrgCompetitionResult(
                            Competition: HebrewTextHelper.NormalizeHebrewLine(lines[0].Trim()),
                            AgeGroup: ageGroupVal,
                            Date: dat_relay,
                            Event: $"{pendingEventLen} {pendingEventStyle} - {genderRaw} {ageGroupVal}",
                            EventStyleName: styleNorm,
                            EventStyleLen: pendingEventLen,
                            EventStyleGender: genderNorm,
                            EventStyleAge: ageGroupVal,
                            PoolType: "25m",
                            Results: new List<IsrOrgResult>()
                        );

                        Log($"  -> NEW EVENT (Format2): {current.Event}, gender={genderNorm}");
                        currentIsRelay = false;
                        currentRelayLegs = 0;
                        pendingEventLen = null;
                        pendingEventStyle = null;
                        pendingEventLine = null;
                        continue;
                    }
                    else
                    {
                        Log($"  -> GenderAge NOT matched for line: '{line}'");
                    }
                }

                if (isHE && current != null && pendingEventLen == null)
                {
                    if (line.Trim() == HebrewKlali || line.Trim() == HebrewKlaliReversed ||
                        line.Contains(HebrewKlali) || line.Contains(HebrewKlaliReversed))
                    {
                        Log($"  -> MATCH Klali (open category) - switching gender to none");

                        yield return current;

                        current = new IsrOrgCompetitionResult(
                            Competition: current.Competition,
                            AgeGroup: "open",
                            Date: current.Date,
                            Event: $"{current.EventStyleLen} {current.EventStyleName} - {HebrewKlali}",
                            EventStyleName: current.EventStyleName,
                            EventStyleLen: current.EventStyleLen,
                            EventStyleGender: "none",
                            EventStyleAge: "0",
                            PoolType: current.PoolType,
                            Results: new List<IsrOrgResult>()
                        );

                        Log($"  -> NEW EVENT (Klali/Open): {current.Event}, gender=none");
                        continue;
                    }

                    var genderAgeMatch = GenderAgeLineRxHE.Match(line);
                    var mastersAgeMatch = MastersAgeLineRxHE.Match(line);

                    if (genderAgeMatch.Success || mastersAgeMatch.Success)
                    {
                        var newAge = genderAgeMatch.Success
                            ? genderAgeMatch.Groups["age"].Value
                            : mastersAgeMatch.Groups["age"].Value;
                        var newGender = genderAgeMatch.Success
                            ? genderAgeMatch.Groups["gender"].Value.Trim()
                            : mastersAgeMatch.Groups["gender"].Value.Trim();
                        var newGenderNorm = HebrewTextHelper.NormalizeGenderHE(newGender);

                        if (newAge != current.EventStyleAge || newGenderNorm != current.EventStyleGender)
                        {
                            Log($"  -> MATCH GenderAge (category change): gender={newGender}, age={newAge}, genderNorm={newGenderNorm}");

                            yield return current;

                            current = new IsrOrgCompetitionResult(
                                Competition: current.Competition,
                                AgeGroup: newAge,
                                Date: current.Date,
                                Event: $"{current.EventStyleLen} {current.EventStyleName} - {newGender} {newAge}",
                                EventStyleName: current.EventStyleName,
                                EventStyleLen: current.EventStyleLen,
                                EventStyleGender: newGenderNorm,
                                EventStyleAge: newAge,
                                PoolType: current.PoolType,
                                Results: new List<IsrOrgResult>()
                            );

                            Log($"  -> NEW EVENT (category change): {current.Event}, gender={newGenderNorm}");
                            continue;
                        }
                    }
                }

                var rm_date = DateLineRx.Match(line);
                if (rm_date.Success)
                {
                    dat_relay = rm_date.Groups["date"].Value;
                    Log($"  -> DATE found: {dat_relay}");
                }

                if (isHE)
                {
                    // Masters-экспорт Maccabiah ARENA: английский заголовок заплыва без
                    // пола/возраста в строке. Только выставляем pending — сам заплыв
                    // (current) создаст следующая строка "מאסטרס <пол> <возраст>" через
                    // существующий механизм pendingEventLen / masters-continuation выше.
                    var enRelayHead = RelayHeaderEnInHE.Match(raw);
                    if (enRelayHead.Success)
                    {
                        pendingRelayLen = enRelayHead.Groups["len"].Value;
                        pendingRelayLegs = int.Parse(enRelayHead.Groups["legs"].Value);
                        pendingRelayStyleHe = CanonEnStyle(enRelayHead.Groups["style"].Value);
                        pendingEventLine = raw;
                        pendingEventLen = null;
                        pendingEventStyle = null;
                        Log($"  -> PENDING RelayEN-in-HE: legs={pendingRelayLegs}, len={pendingRelayLen}, style={pendingRelayStyleHe}");
                        continue;
                    }

                    var enNoGenderHead = HeaderEnNoGenderInHE.Match(raw);
                    if (enNoGenderHead.Success)
                    {
                        pendingEventLen = enNoGenderHead.Groups["len"].Value;
                        pendingEventStyle = CanonEnStyle(enNoGenderHead.Groups["style"].Value);
                        pendingEventLine = raw;
                        pendingRelayLen = null;
                        pendingRelayStyleHe = null;
                        pendingRelayLegs = 0;
                        Log($"  -> PENDING HeaderEN-noGender-in-HE: len={pendingEventLen}, style={pendingEventStyle}");
                        continue;
                    }

                    var enHead = HeaderRxENinHE.Match(raw);
                    if (enHead.Success)
                    {
                        var ageClean = enHead.Groups["age"].Value.TrimStart('U', 'u');
                        var genderNorm = HebrewTextHelper.NormalizeGenderEN(enHead.Groups["gender"].Value);
                        // Приводим к нижнему регистру, чтобы стиль совпадал с ивритскими
                        // результатами того же соревнования ("freestyle", а не "Freestyle").
                        var styleNorm = HebrewTextHelper.NormalizeStyleName(
                            enHead.Groups["style"].Value.Trim().ToLowerInvariant());

                        Log($"  -> MATCH HeaderEN-in-HE: len={enHead.Groups["len"].Value}, style={styleNorm}, gender={genderNorm}, age={ageClean}");

                        pendingEventLen = null;
                        currentIsRelay = false;
                        currentRelayLegs = 0;

                        if (current != null) yield return current;

                        var nextEn = i + 1 < lines.Count ? lines[i + 1].Trim() : string.Empty;
                        var dmEn = Regex.Match(nextEn, @"\d{2}/\d{2}/\d{4}");
                        var dateEn = dmEn.Success ? dmEn.Value : dat_relay;

                        current = new IsrOrgCompetitionResult(
                            Competition: lines[0].Trim(),
                            AgeGroup: ageClean,
                            Date: dateEn,
                            Event: raw,
                            EventStyleName: styleNorm,
                            EventStyleLen: enHead.Groups["len"].Value,
                            EventStyleGender: genderNorm,
                            EventStyleAge: ageClean,
                            PoolType: "25m",
                            Results: new List<IsrOrgResult>()
                        );
                        Log($"  -> NEW EVENT (EN-in-HE): {current.Event}, gender={genderNorm}");
                        continue;
                    }

                    var rm = RelayHeaderRxHE.Match(line);
                    var rm2 = RelayHeaderRxHE2.Match(line);

                    if (rm.Success || rm2.Success)
                    {
                        var match = rm.Success ? rm : rm2;
                        int legs = int.Parse(match.Groups["legs"].Value);
                        int legLen = int.Parse(match.Groups["len"].Value);

                        Log($"  -> MATCH RelayHeader: legs={legs}, len={legLen}, format={(rm.Success ? "1 (legsXlen)" : "2 (lenXlegs)")}");
                        pendingEventLen = null;
                        currentIsRelay = true;
                        currentRelayLegs = legs;

                        if (current != null) yield return current;

                        var next = i + 1 < lines.Count ? lines[i + 1].Trim() : string.Empty;
                        var dateParts = next.Split(' ');
                        var date = dateParts.Length > 1 ? dateParts[1] : string.Empty;

                        if (!Regex.IsMatch(date, @"^\d{2}/\d{2}/\d{4}$"))
                        {
                            date = dat_relay;
                        }

                        var genderNorm = HebrewTextHelper.NormalizeGenderHE(match.Groups["gender"].Value.Trim());
                        string lenRelay = $"{legs}X{legLen}";
                        var styleHe = match.Groups["style"].Value.Trim();
                        var styleNorm = HebrewTextHelper.StyleMapHE.GetValueOrDefault(styleHe, styleHe);
                        styleNorm = HebrewTextHelper.NormalizeStyleName(styleNorm);
                        var ageGroup = match.Groups["age"].Value;

                        current = new IsrOrgCompetitionResult(
                            Competition: HebrewTextHelper.NormalizeHebrewLine(lines[0].Trim()),
                            AgeGroup: ageGroup,
                            Date: date,
                            Event: line,
                            EventStyleName: styleNorm,
                            EventStyleLen: lenRelay,
                            EventStyleGender: genderNorm,
                            EventStyleAge: ageGroup,
                            PoolType: "25m",
                            Results: new List<IsrOrgResult>()
                        );
                        Log($"  -> NEW RELAY EVENT: {current.Event}, gender={genderNorm}");
                        continue;
                    }

                    if (Regex.IsMatch(line, @"^(?<legs>\d+)\s*[Xx]\s*(?<len>\d+)\s+(?<style>.+?)\s+\u05E9\u05DC\u05D9\u05D7(?:\u05D9\u05DD|\u05D5\u05EA)?\s*$"))
                    {
                        var mm = Regex.Match(line, @"^(?<legs>\d+)\s*[Xx]\s*(?<len>\d+)\s+(?<style>.+?)\s+\u05E9\u05DC\u05D9\u05D7(?:\u05D9\u05DD|\u05D5\u05EA)?\s*$");
                        int legs = int.Parse(mm.Groups["legs"].Value);
                        int legLen = int.Parse(mm.Groups["len"].Value);
                        var styleHe = mm.Groups["style"].Value.Trim();

                        Log($"  -> MATCH RelayHeader (masters, no age): legs={legs}, len={legLen}, style={styleHe}");

                        if (current != null) yield return current;

                        currentIsRelay = true;
                        currentRelayLegs = legs;

                        pendingRelayLegs = legs;
                        pendingRelayLen = $"{legs}X{legLen}";
                        pendingRelayStyleHe = styleHe;
                        pendingEventLine = line;

                        continue;
                    }
                }

                // Чистый EN-файл: заголовок с гибкой категорией после тире.
                if (!isHE)
                {
                    var enRelayFull = RelayHeaderEnFull.Match(line);
                    var enIndivFull = enRelayFull.Success ? Match.Empty : HeaderEnFull.Match(line);
                    if (enRelayFull.Success || enIndivFull.Success)
                    {
                        bool relay = enRelayFull.Success;
                        var mm = relay ? enRelayFull : enIndivFull;
                        var (genderNorm, age) = ParseEnCategory(mm.Groups["cat"].Value);
                        var styleNorm = CanonEnStyle(mm.Groups["style"].Value);
                        var len = relay
                            ? $"{mm.Groups["legs"].Value}X{mm.Groups["len"].Value}"
                            : mm.Groups["len"].Value;

                        if (current != null) yield return current;

                        currentIsRelay = relay;
                        currentRelayLegs = relay ? int.Parse(mm.Groups["legs"].Value) : 0;
                        pendingEventLen = null;
                        pendingEventStyle = null;

                        // Дата события — из строки времени под заголовком ("05/07/2026 17:00").
                        var nextEn = i + 1 < lines.Count ? lines[i + 1].Trim() : string.Empty;
                        var dmEn = Regex.Match(nextEn, @"\d{2}/\d{2}/\d{4}");
                        var dateEn = dmEn.Success ? dmEn.Value : dat_relay;

                        current = new IsrOrgCompetitionResult(
                            Competition: lines[0].Trim(),
                            AgeGroup: age,
                            Date: dateEn,
                            Event: line,
                            EventStyleName: styleNorm,
                            EventStyleLen: len,
                            EventStyleGender: genderNorm,
                            EventStyleAge: age,
                            PoolType: "25m",
                            Results: new List<IsrOrgResult>()
                        );
                        Log($"  -> NEW EN EVENT: {line} => style={styleNorm}, gender={genderNorm}, age={age}, relay={relay}");
                        continue;
                    }
                }

                var m = headerRx.Match(line);
                if (m.Success)
                {
                    Log($"  -> MATCH HeaderFormat1: len={m.Groups["len"].Value}, style={m.Groups["style"].Value}, gender={m.Groups["gender"].Value}, age={m.Groups["age"].Value}");
                    pendingEventLen = null;

                    var styleVal = m.Groups["style"].Value;
                    bool isRelayHeader =
                        (!isHE && styleVal.Contains("Relay", StringComparison.OrdinalIgnoreCase)) ||
                        (isHE && styleVal.Contains(HebrewRelay, StringComparison.OrdinalIgnoreCase));

                    currentIsRelay = isRelayHeader;
                    currentRelayLegs = isRelayHeader ? 4 : 0;

                    if (current != null)
                    {
                        yield return current;
                    }

                    var next = i + 1 < lines.Count ? lines[i + 1].Trim() : string.Empty;
                    var dateParts = next.Split(' ');
                    var date = dateParts.Length > 1 ? dateParts[1] : string.Empty;

                    if (!Regex.IsMatch(date, @"^\d{2}/\d{2}/\d{4}$"))
                    {
                        date = dat_relay;
                    }

                    var rawLen = m.Groups["len"].Value;
                    var len = rawLen.EndsWith("m", StringComparison.OrdinalIgnoreCase)
                        ? rawLen[..^1]
                        : rawLen;

                    string genderNorm = isHE
                        ? HebrewTextHelper.NormalizeGenderHE(m.Groups["gender"].Value)
                        : HebrewTextHelper.NormalizeGenderEN(m.Groups["gender"].Value);

                    current = new IsrOrgCompetitionResult(
                        Competition: isHE ? HebrewTextHelper.NormalizeHebrewLine(lines[0].Trim()) : lines[0].Trim(),
                        AgeGroup: m.Groups["age"].Value,
                        Date: date,
                        Event: line,
                        EventStyleName: HebrewTextHelper.NormalizeStyleName(
                            isHE
                                ? HebrewTextHelper.StyleMapHE.GetValueOrDefault(m.Groups["style"].Value, m.Groups["style"].Value)
                                : m.Groups["style"].Value),
                        EventStyleLen: len,
                        EventStyleGender: genderNorm,
                        EventStyleAge: m.Groups["age"].Value,
                        PoolType: "25m",
                        Results: new List<IsrOrgResult>()
                    );
                    Log($"  -> NEW EVENT (Format1): {current.Event}");
                    continue;
                }

                if (isHE)
                {
                    var simpleMatch = HeaderRxHESimple.Match(line);
                    if (simpleMatch.Success)
                    {
                        var styleCheck = simpleMatch.Groups["style"].Value.Trim();
                        Log($"  -> SimpleHeader candidate: len={simpleMatch.Groups["len"].Value}, style='{styleCheck}'");

                        if (!styleCheck.Contains("\u05DE\u05D9\u05E7\u05D5\u05DD") &&
                            !styleCheck.Contains("\u05DE\u05E7\u05E6\u05D4") &&
                            !styleCheck.Contains("\u05EA\u05D5\u05E6\u05D0\u05D5\u05EA"))
                        {
                            pendingEventLen = simpleMatch.Groups["len"].Value;
                            pendingEventStyle = styleCheck;
                            pendingEventLine = line;
                            Log($"  -> PENDING SimpleHeader: len={pendingEventLen}, style={pendingEventStyle}");
                            continue;
                        }
                        else
                        {
                            Log($"  -> SimpleHeader REJECTED (table header)");
                        }
                    }
                }

                if (current != null && currentIsRelay)
                {
                    var tm = RelayTeamLineRxHE.Match(line);
                    if (tm.Success)
                    {
                        Log($"  -> MATCH RelayTeam: pos={tm.Groups["pos"].Value}, heat={tm.Groups["heat"].Value}");
                        int pos = int.Parse(tm.Groups["pos"].Value);
                        int heat = int.Parse(tm.Groups["heat"].Value);
                        int lane = int.Parse(tm.Groups["lane"].Value);
                        string team = tm.Groups["team"].Value.Trim();

                        string timeTok = tm.Groups["time"].Value.Trim();
                        string? time = null;
                        string? timeFailNote = null;

                        if (Regex.IsMatch(timeTok, @"^\d{2}:\d{2}\.\d{1,2}$"))
                        {
                            if (timeTok != "00:00.00" && timeTok != "00:00.0")
                            {
                                time = timeTok;
                            }
                        }
                        else if (timeTok == "DQ" || timeTok == "NS")
                        {
                            timeFailNote = timeTok;
                        }

                        var swimmers = new List<RelaySwimmer>();
                        int k = i + 1;
                        int order = 1;

                        while (k < lines.Count && swimmers.Count < currentRelayLegs)
                        {
                            var sRaw = lines[k].Trim();
                            var sLine = HebrewTextHelper.NormalizeHebrewLine(sRaw);

                            if (Regex.IsMatch(sLine, @"\b\d{4}\b"))
                            {
                                swimmers.Add(IsrOrgResultLineParser.ParseRelaySwimmerLine(sLine, order));
                                order++;
                            }

                            k++;
                        }

                        i = k - 1;

                        if (swimmers.Count >= currentRelayLegs)
                        {
                            current.Results.Add(new IsrOrgResult(
                                Country: "",
                                Position: pos,
                                Heat: heat,
                                Lane: lane,
                                LastName: "",
                                FirstName: "",
                                BirthYear: 0,
                                Club: team,
                                Time: time,
                                TimeFailNote: timeFailNote,
                                InternationalPoints: 0,
                                IsRelay: true,
                                RelayTeamName: team,
                                RelaySwimmersName: string.Join(", ", swimmers.Select(s => $"{s.FirstName} {s.LastName}".Trim())),
                                RelaySwimmers: swimmers
                            ));
                            Log($"  -> Added relay result: team={team}");
                        }
                        else
                        {
                            pendingRelayResult = new IsrOrgResult(
                                Country: "",
                                Position: pos,
                                Heat: heat,
                                Lane: lane,
                                LastName: "",
                                FirstName: "",
                                BirthYear: 0,
                                Club: team,
                                Time: time,
                                TimeFailNote: timeFailNote,
                                InternationalPoints: 0,
                                IsRelay: true,
                                RelayTeamName: team,
                                RelaySwimmersName: null,
                                RelaySwimmers: null
                            );
                            pendingSwimmers = swimmers;
                            pendingSwimmersOrder = order;
                        }

                        continue;
                    }
                }

                // EN-эстафета: командный результат (место/команда/время). Имена
                // пловцов в PDF раздроблены на 2–3 строки с непоследовательным
                // регистром — надёжно реконструировать ноги нельзя, поэтому берём
                // только командный результат; строки пловцов/шапки таблицы ниже
                // ничему не матчатся и просто пропускаются.
                if (!isHE && current != null && currentIsRelay)
                {
                    var tmEn = RelayTeamLineEn.Match(line);
                    if (tmEn.Success)
                    {
                        int pos = int.Parse(tmEn.Groups["pos"].Value);
                        int heat = int.Parse(tmEn.Groups["heat"].Value);
                        int lane = int.Parse(tmEn.Groups["lane"].Value);
                        string team = tmEn.Groups["team"].Value.Trim();
                        string timeTok = tmEn.Groups["time"].Value.Trim();

                        string? time = null;
                        string? timeFailNote = null;
                        if (Regex.IsMatch(timeTok, @"^\d{2}:\d{2}\.\d{1,2}$"))
                        {
                            if (timeTok != "00:00.00" && timeTok != "00:00.0") time = timeTok;
                        }
                        else if (timeTok is "DQ" or "NS")
                        {
                            timeFailNote = timeTok;
                        }

                        current.Results.Add(new IsrOrgResult(
                            Country: "",
                            Position: pos,
                            Heat: heat,
                            Lane: lane,
                            LastName: "",
                            FirstName: "",
                            BirthYear: 0,
                            Club: team,
                            Time: time,
                            TimeFailNote: timeFailNote,
                            InternationalPoints: 0,
                            IsRelay: true,
                            RelayTeamName: team,
                            RelaySwimmersName: null,
                            RelaySwimmers: null
                        ));
                        Log($"  -> Added EN relay team result: pos={pos}, team='{team}', time={time}");
                        continue;
                    }
                }

                // ==== Разбор строки результата с авто-детектом ориентации токенов ====
                // Латиница строк результатов в двуязычных экспортах Maccabiah приходит
                // в РАЗНОМ порядке: в одних файлах уже нормальном (rank heat lane FAMILY
                // name year club time points), в других — перевёрнутом (восстанавливается
                // RTL-реверсом). Строки-места из трёх чисел ("2 7 3") матчат стартовый
                // детектор в ОБЕИХ ориентациях, поэтому выбираем ту, чья склейка со
                // следующей строкой реально совпала с FullResultRx. Норм (перевёрнутая)
                // имеет приоритет при равенстве → старые файлы не задеты.
                if (current != null)
                {
                    bool startNorm = Regex.IsMatch(line, @"^(-|\d+)\s+\d+\s+\d+");
                    bool startRaw = isHE && Regex.IsMatch(raw, @"^(-|\d+)\s+\d+\s+\d+");

                    if (startNorm || startRaw)
                    {
                        int resultLineIdx = i;

                        // Собирает entry в заданной ориентации, при необходимости
                        // подклеивая следующую строку (место и данные бывают разнесены).
                        (string entry, bool full, bool consumed) BuildEntry(bool useNorm)
                        {
                            string Prep(string s) => useNorm && isHE ? HebrewTextHelper.NormalizeHebrewLine(s) : s;
                            var e = Prep(raw);
                            bool cons = false;
                            if (!FullResultRx.IsMatch(e) && i + 1 < lines.Count)
                            {
                                e += " " + Prep(lines[i + 1].Trim());
                                cons = true;
                            }
                            return (e, FullResultRx.IsMatch(e), cons);
                        }

                        var normC = startNorm ? BuildEntry(true) : (entry: "", full: false, consumed: false);
                        var rawC = startRaw ? BuildEntry(false) : (entry: "", full: false, consumed: false);

                        // Приоритет — ориентация, реально совпавшая с FullResultRx.
                        // Если ни одна, но норм-стартовала — легаси-путь (склейка+парс
                        // без полного совпадения) для сохранения прежнего поведения.
                        // Стартовый детектор из трёх чисел не привязан к границам
                        // токенов, поэтому ловит "1 4 02" внутри relay-строки
                        // "1 4 02:34.24 Rank 9". Чтобы такие строки не уходили в парсер,
                        // СЫРАЯ ориентация берётся ТОЛЬКО при реальном совпадении с
                        // FullResultRx. Легаси-парс-без-совпадения — лишь для norm.
                        bool useNormFinal;
                        bool proceed = true;
                        if (normC.full) useNormFinal = true;
                        else if (rawC.full) useNormFinal = false;
                        else if (startNorm) useNormFinal = true;
                        else { useNormFinal = true; proceed = false; }

                        if (proceed)
                        {
                            var chosen = useNormFinal ? normC : rawC;
                            var entry = chosen.entry;
                            if (chosen.consumed) i++;

                            Log($"  -> Result line candidate ({(useNormFinal ? "reversed/norm" : "raw")} orientation)");

                            try
                            {
                                var res = IsrOrgResultLineParser.ParseResultLine(entry);

                                // Перенос длинной фамилии: PDF иногда разносит её на соседние
                                // строки (напр. "TSCHERKOWS" / данные / "KI"), и в строке данных
                                // фамилия пустая. Восстанавливаем из соседних строк-фрагментов
                                // (только буквы) в той же ориентации, склеивая prev+next.
                                if (string.IsNullOrWhiteSpace(res.LastName))
                                {
                                    string Prep(string s) => useNormFinal ? NormalizeIfHe(s, isHE) : s;
                                    var prevFrag = resultLineIdx - 1 >= 0 ? Prep(lines[resultLineIdx - 1].Trim()) : string.Empty;
                                    var nextFrag = i + 1 < lines.Count ? Prep(lines[i + 1].Trim()) : string.Empty;

                                    var recovered = string.Empty;
                                    if (IsNameFragment(prevFrag)) recovered += prevFrag;
                                    if (IsNameFragment(nextFrag)) recovered += nextFrag;

                                    if (recovered.Length > 0)
                                    {
                                        res = res with { LastName = recovered };
                                        Log($"  -> Recovered wrapped surname: '{recovered}' (prev='{prevFrag}', next='{nextFrag}')");
                                    }
                                }

                                current.Results.Add(res);
                                Log($"  -> Added result: {res.LastName} {res.FirstName}, time={res.Time}");
                            }
                            catch (Exception ex)
                            {
                                throw new InvalidOperationException(
                                    $"Parse error on page {pageNumber}, line '{entry}': {ex.Message}", ex);
                            }
                        }
                    }
                }
            }
        }

        if (pendingRelayResult != null && pendingSwimmers != null && current != null && pendingSwimmers.Count > 0)
        {
            current.Results.Add(CreateRelayResult(pendingRelayResult, pendingSwimmers));
        }

        if (current != null)
        {
            Log($"Yielding final event: {current.Event} with {current.Results.Count} results");
            yield return current;
        }

        Log($"Parse complete. Total events yielded.");
    }

    private static IsrOrgResult CreateRelayResult(IsrOrgResult pending, List<RelaySwimmer> swimmers)
    {
        return new IsrOrgResult(
            Country: pending.Country,
            Position: pending.Position,
            Heat: pending.Heat,
            Lane: pending.Lane,
            LastName: pending.LastName,
            FirstName: pending.FirstName,
            BirthYear: pending.BirthYear,
            Club: pending.Club,
            Time: pending.Time,
            TimeFailNote: pending.TimeFailNote,
            InternationalPoints: pending.InternationalPoints,
            IsRelay: true,
            RelayTeamName: pending.RelayTeamName,
            RelaySwimmersName: string.Join(", ", swimmers.Select(s => $"{s.FirstName} {s.LastName}".Trim())),
            RelaySwimmers: swimmers
        );
    }
}
