// -*- coding: utf-8 -*-
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using Swimm.API.Services.Models;
using Swimm.API.Services.Helpers;

namespace Swimm.API.Services.Parsers;

public static class CompetitionParser
{
    // Hebrew patterns using Unicode escapes to avoid encoding issues
    private static Regex? _headerRxHE;
    private static Regex? _headerRxEN;
    private static Regex? _headerRxHESimple;
    private static Regex? _genderAgeLineRxHE;
    private static Regex? _fullResultRx;
    private static Regex? _relayHeaderRxHE;
    private static Regex? _relayTeamLineRxHE;
    private static Regex? _dateLineRx;

    // Hebrew gender words - original order
    // ??????? = \u05D1\u05E0\u05D5\u05EA (girls)
    // ???????? = \u05D1\u05E0\u05D9\u05DD (boys)
    // ??????? = \u05E0\u05E9\u05D9\u05DD (women)
    // ??????? = \u05D2\u05D1\u05E8\u05D9\u05DD (men)
    
    // Hebrew gender words - reversed (after NormalizeHebrewLine)
    // ??????? = \u05EA\u05D5\u05E0\u05D1 (girls reversed)
    // ???????? = \u05DD\u05D9\u05E0\u05D1 (boys reversed)
    // ??????? = \u05DD\u05D9\u05E9\u05E0 (women reversed)
    // ??????? = \u05DD\u05D9\u05E8\u05D1\u05D2 (men reversed)

    private const string GenderPatternOriginal = 
        "\u05D1\u05E0\u05D5\u05EA|\u05D1\u05E0\u05D9\u05DD|\u05E0\u05E9\u05D9\u05DD|\u05D2\u05D1\u05E8\u05D9\u05DD";
    
    private const string GenderPatternReversed = 
        "\u05EA\u05D5\u05E0\u05D1|\u05DD\u05D9\u05E0\u05D1|\u05DD\u05D9\u05E9\u05E0|\u05DD\u05D9\u05E8\u05D1\u05D2";

    // ???? = "klali" = open/general category (no specific gender/age)
    // Original: \u05DB\u05DC\u05DC\u05D9
    // Reversed: \u05D9\u05DC\u05DC\u05DB
    private const string HebrewKlali = "\u05DB\u05DC\u05DC\u05D9";
    private const string HebrewKlaliReversed = "\u05D9\u05DC\u05DC\u05DB";

    // Format 1 (old): 400 ???????? (?????? ?? ??????) - ??????? 12-13
    private static Regex HeaderRxHE => _headerRxHE ??= new Regex(
        @"^(?<len>\d+)\s+(?<style>.+?)\s*-\s*(?<gender>" +
        GenderPatternOriginal + "|" + GenderPatternReversed +
        @")\s+(?<age>\d+(-\d+)?)$", 
        RegexOptions.Compiled);

    // Format 2 (new): 1500 (?????? ????????? ? ?????, ??? gender/age)
    private static Regex HeaderRxHESimple => _headerRxHESimple ??= new Regex(
        @"^(?<len>\d+)\s+(?<style>[\u0590-\u05FF\s]+)$",
        RegexOptions.Compiled);

    // Gender/age on separate line: ??????? 14 or ???????? 12-13 (works with both original and reversed)
    private static Regex GenderAgeLineRxHE => _genderAgeLineRxHE ??= new Regex(
        @"^(?<gender>" +
        GenderPatternOriginal + "|" + GenderPatternReversed +
        @")\s+(?<age>\d+(-\d+)?)$",
        RegexOptions.Compiled);

    private static Regex HeaderRxEN => _headerRxEN ??= new Regex(
        @"^(?<len>\d+m?)\s+(?<style>.+?)\s*-\s*(?<gender>female|male|girls|boys|women|men)\s+(?<age>\d+(-\d+)?)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Regex FullResultRx => _fullResultRx ??= new Regex(
        @"^(-|\d+)\s+\d+\s+\d+.*(\d{2}:\d{2}\.\d{2}|NS|DQ)\s+\d+$",
        RegexOptions.Compiled);

    private static Regex RelayHeaderRxHE => _relayHeaderRxHE ??= new Regex(
        @"^(?<legs>\d+)\s*[Xx]\s*(?<len>\d+)\s+(?<style>.+?)\s+" +
        "\u05E9\u05DC\u05D9\u05D7(?:\u05D9\u05DD|\u05D5\u05EA)" +
        @"\s*-\s*(?<gender>" +
        "\u05E0|\u05D6|" + GenderPatternOriginal + "|" + GenderPatternReversed +
        @")\s+(?<age>\d+(?:-\d+)?)$",
        RegexOptions.Compiled);

    private static Regex RelayTeamLineRxHE => _relayTeamLineRxHE ??= new Regex(
        @"^(?<heat>\d+)\s+(?<lane>\d+)\s+(?<team>.+?)\s+(?<time>\d{2}:\d{2}\.\d{2}|DQ|NS)\s+" +
        "\u05DE\u05D9\u05E7\u05D5\u05DD" +
        @"\s+(?<pos>\d+)\s*$",
        RegexOptions.Compiled);

    private static Regex DateLineRx => _dateLineRx ??= new Regex(
        @"(?<date>\d{2}/\d{2}/\d{4})$",
        RegexOptions.Compiled);

    private const string HebrewRelay = "\u05E9\u05DC\u05D9\u05D7";

    // Debug log storage
    private static List<string> _debugLog = new();

    /// <summary>
    /// ???????? debug ??? ?? ?????????? ???????
    /// </summary>
    public static string GetDebugLog()
    {
        return string.Join("\n", _debugLog);
    }

    public static IEnumerable<PDF_CompetitionResult> ParseCompetitions(Stream pdfStream, string language)
    {
        var results = new List<PDF_CompetitionResult>();
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
            // Include debug log in exception
            var debugInfo = string.Join("\n", _debugLog.TakeLast(50));
            throw new InvalidOperationException(
                $"Error in ParseCompetitions (language={language}): {ex.Message}\n\n--- DEBUG LOG (last 50 lines) ---\n{debugInfo}", ex);
        }

        // If no results, throw with debug info
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

    private static IEnumerable<PDF_CompetitionResult> ParseCompetitionsInternal(Stream pdfStream, string language)
    {
        bool isHE = language.Equals("HE", StringComparison.OrdinalIgnoreCase);
        var headerRx = isHE ? HeaderRxHE : HeaderRxEN;

        Log($"Starting parse, language={language}, isHE={isHE}");

        using var doc = PdfDocument.Open(pdfStream);
        Log($"PDF opened, pages={doc.NumberOfPages}");

        PDF_CompetitionResult? current = null;

        bool currentIsRelay = false;
        int currentRelayLegs = 0;
        string dat_relay = "";
        
        string? pendingEventLen = null;
        string? pendingEventStyle = null;
        string? pendingEventLine = null;

        PDF_Result? pendingRelayResult = null;
        List<RelaySwimmer>? pendingSwimmers = null;
        int pendingSwimmersOrder = 1;

        foreach (var page in doc.GetPages())
        {
            Log($"--- Page {page.Number} ---");
            
            var words = page.GetWords();
            var lines = words
                .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 2.0) * 2.0)
                .OrderByDescending(g => g.Key)
                .Select(g => string.Join(' ', g.OrderBy(w => w.BoundingBox.Left)
                    .Select(w => w.Text)))
                .ToList();

            Log($"Page {page.Number}: {lines.Count} lines extracted");

            for (int i = 0; i < lines.Count; i++)
            {
                var raw = lines[i].Trim();
                var line = isHE ? HebrewTextHelper.NormalizeHebrewLine(raw) : raw;

                // Log every line for debugging
                Log($"L{i}: raw='{raw.Substring(0, Math.Min(60, raw.Length))}...' norm='{line.Substring(0, Math.Min(60, line.Length))}...'");

                // Handle pending relay
                if (pendingRelayResult != null && pendingSwimmers != null && current != null)
                {
                    bool isNewHeader = RelayHeaderRxHE.IsMatch(line) || headerRx.IsMatch(line) || 
                                       (isHE && HeaderRxHESimple.IsMatch(line));
                    bool isNewTeam = RelayTeamLineRxHE.IsMatch(line);

                    if (!isNewHeader && !isNewTeam && pendingSwimmers.Count < currentRelayLegs)
                    {
                        if (Regex.IsMatch(line, @"\b\d{4}\b"))
                        {
                            pendingSwimmers.Add(ResultLineParser.ParseRelaySwimmerLine(line, pendingSwimmersOrder));
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

                // Check for gender/age line (Format 2 continuation)
                if (isHE && pendingEventLen != null)
                {
                    Log($"  -> Checking for gender/age (pending: len={pendingEventLen}, style={pendingEventStyle})");
                    var genderAgeMatch = GenderAgeLineRxHE.Match(line);
                    if (genderAgeMatch.Success)
                    {
                        Log($"  -> MATCH GenderAge: gender={genderAgeMatch.Groups["gender"].Value}, age={genderAgeMatch.Groups["age"].Value}");
                        
                        if (current != null)
                        {
                            Log($"  -> Yielding previous event: {current.Event}");
                            yield return current;
                        }

                        var genderNorm = HebrewTextHelper.NormalizeGenderHE(genderAgeMatch.Groups["gender"].Value.Trim());
                        var styleNorm = HebrewTextHelper.StyleMapHE.GetValueOrDefault(pendingEventStyle!, pendingEventStyle!);

                        current = new PDF_CompetitionResult(
                            Competition: HebrewTextHelper.NormalizeHebrewLine(lines[0].Trim()),
                            AgeGroup: genderAgeMatch.Groups["age"].Value,
                            Date: dat_relay,
                            Event: $"{pendingEventLen} {pendingEventStyle} - {genderAgeMatch.Groups["gender"].Value} {genderAgeMatch.Groups["age"].Value}",
                            EventStyleName: styleNorm,
                            EventStyleLen: pendingEventLen,
                            EventStyleGender: genderNorm,
                            EventStyleAge: genderAgeMatch.Groups["age"].Value,
                            PoolType: "25m",
                            Results: new List<PDF_Result>()
                        );

                        Log($"  -> NEW EVENT (Format2): {current.Event}");
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
                
                // Check for standalone "????" line - switches gender to "none" until next gender/age line
                if (isHE && current != null && pendingEventLen == null)
                {
                    // Check for ???? (klali = open category) - standalone line
                    if (line.Trim() == HebrewKlali || line.Trim() == HebrewKlaliReversed ||
                        line.Contains(HebrewKlali) || line.Contains(HebrewKlaliReversed))
                    {
                        Log($"  -> MATCH Klali (open category) - switching gender to none");
                        
                        yield return current;

                        current = new PDF_CompetitionResult(
                            Competition: current.Competition,
                            AgeGroup: "open",
                            Date: current.Date,
                            Event: $"{current.EventStyleLen} {current.EventStyleName} - {HebrewKlali}",
                            EventStyleName: current.EventStyleName,
                            EventStyleLen: current.EventStyleLen,
                            EventStyleGender: "none",
                            EventStyleAge: "0",
                            PoolType: current.PoolType,
                            Results: new List<PDF_Result>()
                        );

                        Log($"  -> NEW EVENT (Klali/Open): {current.Event}, gender=none");
                        continue;
                    }
                    
                    // Check for gender/age line that changes category (e.g. ???? 14 -> ???? 15)
                    var genderAgeMatch = GenderAgeLineRxHE.Match(line);
                    if (genderAgeMatch.Success)
                    {
                        var newAge = genderAgeMatch.Groups["age"].Value;
                        var newGender = genderAgeMatch.Groups["gender"].Value.Trim();
                        var newGenderNorm = HebrewTextHelper.NormalizeGenderHE(newGender);
                        
                        // Only create new event if age or gender changed
                        if (newAge != current.EventStyleAge || newGenderNorm != current.EventStyleGender)
                        {
                            Log($"  -> MATCH GenderAge (category change): gender={newGender}, age={newAge}");
                            
                            yield return current;

                            current = new PDF_CompetitionResult(
                                Competition: current.Competition,
                                AgeGroup: newAge,
                                Date: current.Date,
                                Event: $"{current.EventStyleLen} {current.EventStyleName} - {newGender} {newAge}",
                                EventStyleName: current.EventStyleName,
                                EventStyleLen: current.EventStyleLen,
                                EventStyleGender: newGenderNorm,
                                EventStyleAge: newAge,
                                PoolType: current.PoolType,
                                Results: new List<PDF_Result>()
                            );

                            Log($"  -> NEW EVENT (category change): {current.Event}, gender={newGenderNorm}");
                            continue;
                        }
                    }
                }

                // Date detection
                var rm_date = DateLineRx.Match(line);
                if (rm_date.Success)
                {
                    dat_relay = rm_date.Groups["date"].Value;
                    Log($"  -> DATE found: {dat_relay}");
                }

                // Relay header (HE)
                if (isHE)
                {
                    var rm = RelayHeaderRxHE.Match(line);
                    if (rm.Success)
                    {
                        Log($"  -> MATCH RelayHeader: legs={rm.Groups["legs"].Value}, len={rm.Groups["len"].Value}");
                        pendingEventLen = null;
                        currentIsRelay = true;
                        currentRelayLegs = int.Parse(rm.Groups["legs"].Value);

                        if (current != null) yield return current;

                        var next = i + 1 < lines.Count ? lines[i + 1].Trim() : string.Empty;
                        var dateParts = next.Split(' ');
                        var date = dateParts.Length > 1 ? dateParts[1] : string.Empty;

                        if (!Regex.IsMatch(date, @"^\d{2}/\d{2}/\d{4}$"))
                        {
                            date = dat_relay;
                        }

                        var genderNorm = HebrewTextHelper.NormalizeGenderHE(rm.Groups["gender"].Value.Trim());
                        int legs = currentRelayLegs;
                        int legLen = int.Parse(rm.Groups["len"].Value);
                        string lenRelay = $"{legs}X{legLen}";
                        var styleHe = rm.Groups["style"].Value.Trim();
                        var styleNorm = HebrewTextHelper.StyleMapHE.GetValueOrDefault(styleHe, styleHe);

                        current = new PDF_CompetitionResult(
                            Competition: HebrewTextHelper.NormalizeHebrewLine(lines[0].Trim()),
                            AgeGroup: "9-11",
                            Date: date,
                            Event: line,
                            EventStyleName: styleNorm,
                            EventStyleLen: lenRelay,
                            EventStyleGender: genderNorm,
                            EventStyleAge: "9-11",
                            PoolType: "25m",
                            Results: new List<PDF_Result>()
                        );
                        Log($"  -> NEW RELAY EVENT: {current.Event}");
                        continue;
                    }
                }

                // Regular event header - Format 1 (old)
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

                    current = new PDF_CompetitionResult(
                        Competition: isHE ? HebrewTextHelper.NormalizeHebrewLine(lines[0].Trim()) : lines[0].Trim(),
                        AgeGroup: m.Groups["age"].Value,
                        Date: date,
                        Event: line,
                        EventStyleName: isHE
                            ? HebrewTextHelper.StyleMapHE.GetValueOrDefault(m.Groups["style"].Value, m.Groups["style"].Value)
                            : m.Groups["style"].Value,
                        EventStyleLen: len,
                        EventStyleGender: genderNorm,
                        EventStyleAge: m.Groups["age"].Value,
                        PoolType: "25m",
                        Results: new List<PDF_Result>()
                    );
                    Log($"  -> NEW EVENT (Format1): {current.Event}");
                    continue;
                }

                // Format 2 (new): Simple header like "1500 ..."
                if (isHE)
                {
                    var simpleMatch = HeaderRxHESimple.Match(line);
                    if (simpleMatch.Success)
                    {
                        var styleCheck = simpleMatch.Groups["style"].Value.Trim();
                        Log($"  -> SimpleHeader candidate: len={simpleMatch.Groups["len"].Value}, style='{styleCheck}'");
                        
                        // Check it's not a table header line
                        if (!styleCheck.Contains("\u05DE\u05D9\u05E7\u05D5\u05DD") && // ????
                            !styleCheck.Contains("\u05DE\u05E7\u05E6\u05D4") &&       // ?????
                            !styleCheck.Contains("\u05EA\u05D5\u05E6\u05D0\u05D5\u05EA")) // ??????????
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

                // Relay results
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
                        if (Regex.IsMatch(timeTok, @"^\d{2}:\d{2}\.\d{2}$") && timeTok != "00:00.00")
                        {
                            time = timeTok;
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
                                swimmers.Add(ResultLineParser.ParseRelaySwimmerLine(sLine, order));
                                order++;
                            }

                            k++;
                        }

                        i = k - 1;

                        if (swimmers.Count >= currentRelayLegs)
                        {
                            current.Results.Add(new PDF_Result(
                                Country: "",
                                Position: pos,
                                Heat: heat,
                                Lane: lane,
                                LastName: "",
                                FirstName: "",
                                BirthYear: 0,
                                Club: team,
                                Time: time,
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
                            pendingRelayResult = new PDF_Result(
                                Country: "",
                                Position: pos,
                                Heat: heat,
                                Lane: lane,
                                LastName: "",
                                FirstName: "",
                                BirthYear: 0,
                                Club: team,
                                Time: time,
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

                // Regular results
                if (current != null && Regex.IsMatch(line, @"^(-|\d+)\s+\d+\s+\d+"))
                {
                    Log($"  -> Result line candidate");
                    var entry = line;
                    if (!FullResultRx.IsMatch(entry) && i + 1 < lines.Count)
                    {
                        var nxtRaw = lines[i + 1].Trim();
                        var nxtLine = isHE ? HebrewTextHelper.NormalizeHebrewLine(nxtRaw) : nxtRaw;
                        entry += " " + nxtLine;
                        i++;
                    }

                    try
                    {
                        var res = ResultLineParser.ParseResultLine(entry);
                        current.Results.Add(res);
                        Log($"  -> Added result: {res.LastName} {res.FirstName}, time={res.Time}");
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"Parse error on page {page.Number}, line '{entry}': {ex.Message}", ex);
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

    private static PDF_Result CreateRelayResult(PDF_Result pending, List<RelaySwimmer> swimmers)
    {
        return new PDF_Result(
            Country: pending.Country,
            Position: pending.Position,
            Heat: pending.Heat,
            Lane: pending.Lane,
            LastName: pending.LastName,
            FirstName: pending.FirstName,
            BirthYear: pending.BirthYear,
            Club: pending.Club,
            Time: pending.Time,
            InternationalPoints: pending.InternationalPoints,
            IsRelay: true,
            RelayTeamName: pending.RelayTeamName,
            RelaySwimmersName: string.Join(", ", swimmers.Select(s => $"{s.FirstName} {s.LastName}".Trim())),
            RelaySwimmers: swimmers
        );
    }
}
