using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Swimm.Parser.Services.Models;
using Swimm.Parser.Services.Helpers;

namespace Swimm.Parser.Services.Parsers.IsrOrgMastersRecords;

public class IsrOrgMastersRecordsParser : IFormatParser
{
    public string FormatName => "IsrOrgMastersRecords";

    private readonly List<string> _debugLog = new();

    // ── Hebrew constants ──

    // Gender words — original
    private const string HebrewGvarim = "\u05D2\u05D1\u05E8\u05D9\u05DD";   // גברים (мужчины)
    private const string HebrewNashim = "\u05E0\u05E9\u05D9\u05DD";         // נשים (женщины)
    // Gender words — reversed (PdfPig often reverses Hebrew char order)
    private const string HebrewGvarimRev = "\u05DD\u05D9\u05E8\u05D1\u05D2";
    private const string HebrewNashimRev = "\u05DD\u05D9\u05E9\u05E0";

    // Style words — original
    private const string HebrewHofshi = "\u05D7\u05D5\u05E4\u05E9\u05D9";       // חופשי (freestyle)
    private const string HebrewHaze = "\u05D7\u05D6\u05D4";                      // חזה (breaststroke)
    private const string HebrewGav = "\u05D2\u05D1";                             // גב (backstroke)
    private const string HebrewParpar = "\u05E4\u05E8\u05E4\u05E8";             // פרפר (butterfly)
    private const string HebrewMeoravIshi = "\u05DE\u05E2\u05D5\u05E8\u05D1-\u05D0\u05D9\u05E9\u05D9"; // מעורב-אישי (individual medley)
    private const string HebrewMeoravIshi2 = "\u05DE\u05E2\u05D5\u05E8\u05D1 \u05D0\u05D9\u05E9\u05D9"; // מעורב אישי (with space)
    private const string HebrewMeorav = "\u05DE\u05E2\u05D5\u05E8\u05D1";       // מעורב (medley)

    // Meter symbol
    private const string HebrewMeter = "\u05DE\u05F3";   // מ׳
    private const string HebrewMeterRev = "\u05F3\u05DE"; // ׳מ (reversed)

    // Pool keyword
    private const string HebrewBrikha = "\u05D1\u05E8\u05D9\u05DB\u05D4";       // בריכה
    private const string HebrewBrikhaRev = "\u05D4\u05DB\u05D9\u05E8\u05D1";

    // Age keyword
    private const string HebrewGil = "\u05D2\u05D9\u05DC";       // גיל
    private const string HebrewGilRev = "\u05DC\u05D9\u05D2";   // ליג (reversed)

    // Update date keyword
    private const string HebrewTaarichUpdate = "\u05EA\u05D0\u05E8\u05D9\u05DA \u05E2\u05D9\u05D3\u05DB\u05D5\u05DF"; // תאריך עידכון
    private const string HebrewUpdate = "\u05E2\u05D9\u05D3\u05DB\u05D5\u05DF"; // עידכון

    // Table header words
    private const string HebrewShia = "\u05E9\u05D9\u05D0";         // שיא (record)
    private const string HebrewTotzaa = "\u05EA\u05D5\u05E6\u05D0\u05D4"; // תוצאה (result)
    private const string HebrewTaarichHashia = "\u05EA\u05D0\u05E8\u05D9\u05DA \u05D4\u05E9\u05D9\u05D0"; // תאריך השיא
    private const string HebrewShemSachyan = "\u05E9\u05DD \u05E9\u05D7\u05D9\u05D9\u05DF"; // שם שחיין
    private const string HebrewMdinaIgud = "\u05DE\u05D3\u05D9\u05E0\u05D4/\u05D0\u05D9\u05D2\u05D5\u05D3"; // מדינה/איגוד

    // Relay words
    private const string HebrewShlichim = "\u05E9\u05DC\u05D9\u05D7\u05D9\u05DD";   // שליחים
    private const string HebrewShlichot = "\u05E9\u05DC\u05D9\u05D7\u05D5\u05EA";   // שליחות

    // Maximum age upper bound (inclusive) we allow — age groups above 90-94 are skipped
    private const int MaxAgeUpperBound = 94;

    private static readonly Regex TimeRx = new(@"\d{1,2}:\d{2}\.\d{1,2}", RegexOptions.Compiled);
    private static readonly Regex DateRx = new(@"\d{1,2}/\d{1,2}/\d{4}", RegexOptions.Compiled);
    private static readonly Regex DateDotRx = new(@"\d{1,2}\.\d{1,2}\.\d{4}", RegexOptions.Compiled);
    private static readonly Regex AgeGroupRx = new(@"(\d+)\s*-\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex DistRx = new(@"\d+", RegexOptions.Compiled);

    public IEnumerable<Result> Parse(ParseRequest request)
    {
        _debugLog.Clear();
        return ParseAllStreams(request).ToList();
    }

    public object? ParseNormative(ParseRequest request)
    {
        _debugLog.Clear();
        var (records, updateDates) = ParseAllRecordsWithDates(request);
        Log($"Total records parsed: {records.Count}");
        return BuildNormativeOutput(records, updateDates);
    }

    public string GetDebugLog() => string.Join("\n", _debugLog);

    private void Log(string message)
    {
        _debugLog.Add($"[{_debugLog.Count + 1}] {message}");
    }

    // ── Parsing ──

    private IEnumerable<Result> ParseAllStreams(ParseRequest request)
    {
        var primaryPoolType = request.PoolType ?? "50m";
        Log($"Parsing primary file: {request.PrimaryFileName}, poolType={primaryPoolType}");
        foreach (var r in ParseStreamResults(request.PrimaryStream, primaryPoolType))
            yield return r;

        if (request.SecondaryStream != null)
        {
            Log($"Parsing secondary file: {request.SecondaryFileName}, poolType=25m");
            foreach (var r in ParseStreamResults(request.SecondaryStream, "25m"))
                yield return r;
        }
    }

    private IEnumerable<MastersRecord> ParseAllRecords(ParseRequest request)
    {
        var primaryPoolType = request.PoolType ?? "50m";
        Log($"Parsing primary file: {request.PrimaryFileName}, poolType={primaryPoolType}");
        var (primaryRecords, _) = ParseStreamRecordsWithDate(request.PrimaryStream, primaryPoolType);
        foreach (var r in primaryRecords)
            yield return r;

        if (request.SecondaryStream != null)
        {
            var secondaryPoolType = primaryPoolType == "50m" ? "25m" : "50m";
            Log($"Parsing secondary file: {request.SecondaryFileName}, poolType={secondaryPoolType}");
            var (secondaryRecords, _) = ParseStreamRecordsWithDate(request.SecondaryStream, secondaryPoolType);
            foreach (var r in secondaryRecords)
                yield return r;
        }
    }

    private (List<MastersRecord> records, Dictionary<string, string> updateDates) ParseAllRecordsWithDates(ParseRequest request)
    {
        var allRecords = new List<MastersRecord>();
        var updateDates = new Dictionary<string, string>();

        var primaryPoolType = request.PoolType ?? "50m";
        Log($"Parsing primary file: {request.PrimaryFileName}, poolType={primaryPoolType}");
        var (primaryRecords, primaryDate) = ParseStreamRecordsWithDate(request.PrimaryStream, primaryPoolType);
        allRecords.AddRange(primaryRecords);
        if (!string.IsNullOrEmpty(primaryDate))
            updateDates[primaryPoolType] = primaryDate;

        if (request.SecondaryStream != null)
        {
            var secondaryPoolType = primaryPoolType == "50m" ? "25m" : "50m";
            Log($"Parsing secondary file: {request.SecondaryFileName}, poolType={secondaryPoolType}");
            var (secondaryRecords, secondaryDate) = ParseStreamRecordsWithDate(request.SecondaryStream, secondaryPoolType);
            allRecords.AddRange(secondaryRecords);
            if (!string.IsNullOrEmpty(secondaryDate))
                updateDates[secondaryPoolType] = secondaryDate;
        }

        return (allRecords, updateDates);
    }

    /// <summary>
    /// Wrapper that parses a stream and also returns the extracted update date.
    /// </summary>
    private (List<MastersRecord> records, string updateDate) ParseStreamRecordsWithDate(Stream stream, string poolType)
    {
        string updateDate = "";
        var records = new List<MastersRecord>();
        foreach (var item in ParseStreamRecords(stream, poolType, d => updateDate = d))
            records.Add(item);
        return (records, updateDate);
    }

    private IEnumerable<Result> ParseStreamResults(Stream stream, string poolType)
    {
        foreach (var rec in ParseStreamRecords(stream, poolType, null))
        {
            yield return CreateResult(rec, poolType);
        }
    }

    /// <summary>
    /// Main parsing logic. Reads PDF rows, tracks current gender + event context,
    /// and yields data rows as MastersRecord.
    /// onUpdateDate callback is invoked when an update date row is found.
    /// </summary>
    private IEnumerable<MastersRecord> ParseStreamRecords(Stream stream, string poolTypeOverride, Action<string>? onUpdateDate)
    {
        Log($"Starting IsrOrgMastersRecords parse, poolTypeOverride={poolTypeOverride}");

        using var doc = PdfDocument.Open(stream);
        Log($"PDF opened, pages={doc.NumberOfPages}");

        string currentGender = "";
        string currentDistance = "";
        string currentStyle = "";
        bool currentIsRelay = false;
        string poolType = poolTypeOverride;
        int totalResults = 0;
        int skippedAge = 0;

        foreach (var page in doc.GetPages())
        {
            Log($"--- Page {page.Number} ---");
            var allWords = page.GetWords().ToList();
            Log($"Page {page.Number}: {allWords.Count} words");

            // Group words into rows by Y coordinate
            var rows = allWords
                .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 3.0) * 3.0)
                .OrderByDescending(g => g.Key) // top to bottom
                .Select(g => g.OrderBy(w => w.BoundingBox.Left).ToList())
                .ToList();

            Log($"Page {page.Number}: {rows.Count} rows");

            foreach (var rowWords in rows)
            {
                var rowText = NormalizeRowText(rowWords);
                var rowTextRaw = string.Join(' ', rowWords.Select(w => w.Text));

                // Debug first few rows
                if (rows.IndexOf(rowWords) < 3)
                    Log($"  RAW: '{(rowTextRaw.Length > 120 ? rowTextRaw[..120] : rowTextRaw)}'");

                // ── 1. Check for update date ──
                if (rowText.Contains(HebrewUpdate) || rowText.Contains("עידכון"))
                {
                    var extractedDate = TryExtractUpdateDate(rowTextRaw, rowText);
                    if (!string.IsNullOrEmpty(extractedDate))
                    {
                        onUpdateDate?.Invoke(extractedDate);
                        Log($"  Update date extracted: '{extractedDate}' from row: '{rowText}'");
                    }
                    else
                    {
                        Log($"  Update date row (no date found): '{rowText}'");
                    }
                    continue;
                }

                // ── 2. Check for table header row (skip) ──
                if (IsTableHeaderRow(rowText))
                {
                    Log($"  Table header row, skipping");
                    continue;
                }

                // ── 3. Check for gender context row ──
                var detectedGender = TryDetectGender(rowText);
                if (detectedGender != null && !HasTimeValue(rowTextRaw))
                {
                    currentGender = detectedGender;
                    Log($"  GENDER detected: {currentGender}");

                    // Gender row may also contain distance info (e.g. "גברים" on same line as "50 מ'")
                    var (dist, style, isRelay) = TryExtractEvent(rowText);
                    if (!string.IsNullOrEmpty(dist))
                    {
                        currentDistance = dist;
                        currentIsRelay = isRelay;
                        Log($"  DISTANCE (from gender row): {currentDistance}, relay={currentIsRelay}");
                    }
                    if (!string.IsNullOrEmpty(style))
                    {
                        currentStyle = style;
                        Log($"  STYLE (from gender row): {currentStyle}");
                    }
                    continue;
                }

                // ── 4. Check for event context row (distance + style) ──
                if (!HasTimeValue(rowTextRaw))
                {
                    var (dist, style, isRelay) = TryExtractEvent(rowText);
                    bool updated = false;
                    if (!string.IsNullOrEmpty(dist))
                    {
                        currentDistance = dist;
                        currentIsRelay = isRelay;
                        updated = true;
                    }
                    if (!string.IsNullOrEmpty(style))
                    {
                        currentStyle = style;
                        updated = true;
                    }
                    if (updated)
                    {
                        Log($"  EVENT context: dist={currentDistance}, style={currentStyle}, relay={currentIsRelay}");
                        continue;
                    }
                }

                // ── 5. Check for pool type hint ──
                if (ContainsPoolHint(rowText))
                {
                    if (rowText.Contains("25")) poolType = "25m";
                    else if (rowText.Contains("50")) poolType = "50m";
                    Log($"  Pool type hint: {poolType}");
                }

                // ── 6. Data row — must have a time value ──
                if (!HasTimeValue(rowTextRaw))
                    continue;

                if (string.IsNullOrEmpty(currentGender) || string.IsNullOrEmpty(currentDistance) || string.IsNullOrEmpty(currentStyle))
                {
                    Log($"  SKIPPED data row (no context): gender='{currentGender}', dist='{currentDistance}', style='{currentStyle}'");
                    continue;
                }

                // Parse the data row
                var record = ParseDataRow(rowWords, rowText, rowTextRaw,
                    currentGender, currentDistance, currentStyle, currentIsRelay, poolType);

                if (record == null)
                {
                    Log($"  SKIPPED data row (parse failed)");
                    continue;
                }

                // Check age group bounds — skip if age upper bound > MaxAgeUpperBound
                if (ShouldSkipAgeGroup(record.AgeGroup))
                {
                    skippedAge++;
                    Log($"  SKIPPED age group '{record.AgeGroup}' (above {MaxAgeUpperBound})");
                    continue;
                }

                totalResults++;
                Log($"  RESULT #{totalResults}: {record.Gender} {record.Distance}m {record.StyleName} age={record.AgeGroup} " +
                    $"time={record.Time} name='{record.SwimmerName}' club='{record.Club}'");

                yield return record;
            }
        }

        Log($"Parse complete. Total results: {totalResults}, skipped (age): {skippedAge}");
    }

    // ── Row classification helpers ──

    /// <summary>
    /// Extract update date from a row like "תאריך עידכון: 6.4.2025".
    /// Tries both raw text and normalized text for d.M.yyyy and d/M/yyyy patterns.
    /// </summary>
    private string? TryExtractUpdateDate(string rawText, string normalizedText)
    {
        // Try d.M.yyyy or d/M/yyyy in raw text first
        var dotMatch = DateDotRx.Match(rawText);
        if (dotMatch.Success)
            return NormalizeDate(dotMatch.Value, '.');

        var slashMatch = DateRx.Match(rawText);
        if (slashMatch.Success)
            return NormalizeDate(slashMatch.Value, '/');

        // Try in normalized text
        dotMatch = DateDotRx.Match(normalizedText);
        if (dotMatch.Success)
            return NormalizeDate(dotMatch.Value, '.');

        slashMatch = DateRx.Match(normalizedText);
        if (slashMatch.Success)
            return NormalizeDate(slashMatch.Value, '/');

        return null;
    }

    private static string NormalizeRowText(List<Word> words)
    {
        // Normalize each word (reverse Hebrew chars), then reverse word order for RTL
        var parts = words.Select(w => NormalizeWordChars(w.Text)).ToList();
        parts.Reverse();
        return string.Join(' ', parts).Trim();
    }

    private static string NormalizeWordChars(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;
        word = word.Replace('\'', '\u05F3')
                   .Replace('\u2019', '\u05F3')
                   .Replace('\u2018', '\u05F3')
                   .Replace('`', '\u05F3');
        if (Regex.IsMatch(word, @"[\u0590-\u05FF]"))
            return new string(word.Reverse().ToArray());
        return word;
    }

    private static bool HasTimeValue(string text) => TimeRx.IsMatch(text);

    private static bool ContainsAny(string text, params string[] candidates)
    {
        foreach (var c in candidates)
            if (text.Contains(c)) return true;
        return false;
    }

    private static bool IsTableHeaderRow(string text)
    {
        // Header contains words like שיא, תוצאה, תאריך, שם, מדינה, בריכה
        int hits = 0;
        if (ContainsAny(text, HebrewShia, "שיא")) hits++;
        if (ContainsAny(text, HebrewTotzaa, "תוצאה")) hits++;
        if (ContainsAny(text, "תאריך")) hits++;
        if (ContainsAny(text, "שם")) hits++;
        if (ContainsAny(text, "מדינה", "איגוד")) hits++;
        if (ContainsAny(text, HebrewBrikha, HebrewBrikhaRev, "בריכה")) hits++;
        return hits >= 3;
    }

    private static string? TryDetectGender(string text)
    {
        if (ContainsAny(text, HebrewGvarim, HebrewGvarimRev, "גברים")) return "male";
        if (ContainsAny(text, HebrewNashim, HebrewNashimRev, "נשים")) return "female";
        return null;
    }

    /// <summary>
    /// Try to extract distance and style from an event context row.
    /// Examples: "50 מ'" + "פרפר", "100 מ'" + "חופשי", "4X50 מ'" + "מעורב שליחים"
    /// </summary>
    private (string distance, string style, bool isRelay) TryExtractEvent(string text)
    {
        string distance = "";
        string style = "";
        bool isRelay = false;

        // Detect relay keywords
        if (ContainsAny(text, HebrewShlichim, HebrewShlichot, "שליחים", "שליחות"))
            isRelay = true;

        // Extract distance — look for number followed by מ' or just a standalone number
        var relayMatch = Regex.Match(text, @"(\d+)\s*[xX]\s*(\d+)");
        if (relayMatch.Success)
        {
            distance = $"{relayMatch.Groups[1].Value}X{relayMatch.Groups[2].Value}";
            isRelay = true;
        }
        else
        {
            // Look for number near מ' or standalone distance number
            var meterMatch = Regex.Match(text, @"(\d+)\s*" + HebrewMeter);
            if (!meterMatch.Success)
                meterMatch = Regex.Match(text, @"(\d+)\s*" + HebrewMeterRev);
            if (!meterMatch.Success)
                meterMatch = Regex.Match(text, @"(\d+)\s*מ");

            if (meterMatch.Success)
                distance = meterMatch.Groups[1].Value;
            else
            {
                // Standalone typical swim distances
                var distMatch = Regex.Match(text, @"\b(25|50|100|200|400|800|1500)\b");
                if (distMatch.Success && !Regex.IsMatch(text, @"\d+:\d+\.\d+")) // not a time
                    distance = distMatch.Value;
            }
        }

        // Extract style
        style = DetectStyleFromText(text);

        return (distance, style, isRelay);
    }

    private static string DetectStyleFromText(string text)
    {
        if (ContainsAny(text, HebrewMeoravIshi, HebrewMeoravIshi2, "מעורב-אישי", "מעורב אישי"))
            return "individual_medley";
        if (ContainsAny(text, HebrewMeorav, "מעורב"))
            return "medley";
        if (ContainsAny(text, HebrewParpar, "פרפר")) return "butterfly";
        if (ContainsAny(text, HebrewGav, "גב")) return "backstroke";
        if (ContainsAny(text, HebrewHaze, "חזה")) return "breaststroke";
        if (ContainsAny(text, HebrewHofshi, "חופשי")) return "freestyle";
        return "";
    }

    private static bool ContainsPoolHint(string text)
    {
        return ContainsAny(text, HebrewBrikha, HebrewBrikhaRev, "בריכה");
    }

    /// <summary>
    /// Check if age group upper bound exceeds the maximum allowed.
    /// </summary>
    private static bool ShouldSkipAgeGroup(string ageGroup)
    {
        var m = AgeGroupRx.Match(ageGroup);
        if (m.Success && int.TryParse(m.Groups[2].Value, out int upper))
            return upper > MaxAgeUpperBound;
        // Single number (e.g., "100")
        if (int.TryParse(ageGroup, out int single))
            return single > MaxAgeUpperBound;
        return false;
    }

    // ── Data row parsing ──

    /// <summary>
    /// Parse a data row. Columns (RTL, right-to-left on screen):
    /// pool | club | swimmer name | record date | time | age-group
    /// In the PDF words are ordered left-to-right by X coordinate.
    /// We extract time, date, and age group by regex, then split remaining words
    /// into swimmer name and club using positional heuristics.
    /// </summary>
    private MastersRecord? ParseDataRow(
        List<Word> rowWords, string normalizedText, string rawText,
        string gender, string distance, string style, bool isRelay, string poolType)
    {
        // Extract time
        var timeMatch = TimeRx.Match(rawText);
        if (!timeMatch.Success) return null;
        string time = NormalizeTime(timeMatch.Value);

        // Extract date (may be in d/M/yyyy or d.M.yyyy format)
        string recordDate = "";
        var dateMatch = DateRx.Match(rawText);
        if (dateMatch.Success)
        {
            recordDate = NormalizeDate(dateMatch.Value, '/');
        }
        else
        {
            var dateDotMatch = DateDotRx.Match(rawText);
            if (dateDotMatch.Success)
                recordDate = NormalizeDate(dateDotMatch.Value, '.');
        }

        // Extract age group
        string ageGroup = "";
        var ageMatch = AgeGroupRx.Match(rawText);
        if (ageMatch.Success)
            ageGroup = $"{ageMatch.Groups[1].Value}-{ageMatch.Groups[2].Value}";

        // Now extract name and club from the remaining words.
        // Strategy: remove words that are part of time, date, age group, pool keywords.
        // The remaining Hebrew words are sorted RTL: rightmost = name (first), then club.
        var nameClubWords = new List<(string text, double x)>();
        string poolValue = "";

        foreach (var w in rowWords)
        {
            var wText = w.Text;
            double cx = (w.BoundingBox.Left + w.BoundingBox.Right) / 2.0;

            // Skip words that are part of known extracted values
            if (TimeRx.IsMatch(wText)) continue;
            if (DateRx.IsMatch(wText) || DateDotRx.IsMatch(wText)) continue;
            if (AgeGroupRx.IsMatch(wText)) continue;
            if (Regex.IsMatch(wText, @"^\d+$"))
            {
                // Standalone number — could be part of age group
                if (ageGroup.Contains(wText)) continue;
                continue;
            }

            var norm = NormalizeWordChars(wText);

            // Pool value detection
            if (ContainsAny(norm, HebrewBrikha, HebrewBrikhaRev, "בריכה"))
            {
                poolValue += " " + norm;
                continue;
            }

            // Skip table artifacts
            if (norm == HebrewGil || norm == HebrewGilRev) continue;

            // Remaining words are name/club
            if (Regex.IsMatch(wText, @"[\u0590-\u05FF]") || Regex.IsMatch(wText, @"[a-zA-Z]"))
                nameClubWords.Add((norm, cx));
        }

        // Sort by X descending (RTL): rightmost words are typically the swimmer name,
        // leftmost words are club. We split roughly in half or use word count heuristics.
        nameClubWords.Sort((a, b) => b.x.CompareTo(a.x));

        string swimmerName;
        string club;

        if (nameClubWords.Count <= 2)
        {
            // Probably just the name
            swimmerName = string.Join(' ', nameClubWords.Select(w => w.text));
            club = "";
        }
        else if (nameClubWords.Count <= 4)
        {
            // First 2 words = name, rest = club
            swimmerName = string.Join(' ', nameClubWords.Take(2).Select(w => w.text));
            club = string.Join(' ', nameClubWords.Skip(2).Select(w => w.text));
        }
        else
        {
            // First 2 words = name, next words = club
            swimmerName = string.Join(' ', nameClubWords.Take(2).Select(w => w.text));
            club = string.Join(' ', nameClubWords.Skip(2).Select(w => w.text));
        }

        // Remove pool-related text from club if leaked
        club = Regex.Replace(club, @"\b\d{2,3}\b", "").Trim();
        if (ContainsAny(club, HebrewBrikha, HebrewBrikhaRev))
            club = club.Replace(HebrewBrikha, "").Replace(HebrewBrikhaRev, "").Trim();

        return new MastersRecord(
            StyleName: style,
            Distance: distance,
            Gender: gender,
            AgeGroup: ageGroup,
            Time: time,
            RecordDate: recordDate,
            SwimmerName: swimmerName.Trim(),
            Club: club.Trim(),
            Pool: poolValue.Trim(),
            PoolType: poolType,
            IsRelay: isRelay
        );
    }

    // ── Normalization helpers ──

    /// <summary>
    /// Normalize time to MM:SS.hh format (ensure 2-digit minutes).
    /// </summary>
    private static string NormalizeTime(string time)
    {
        // If format is like "0:25.81" -> "00:25.81"
        var parts = time.Split(':');
        if (parts.Length == 2 && parts[0].Length == 1)
            time = "0" + parts[0] + ":" + parts[1];
        // Ensure hundredths have 2 digits
        var dotParts = time.Split('.');
        if (dotParts.Length == 2 && dotParts[1].Length == 1)
            time = dotParts[0] + "." + dotParts[1] + "0";
        return time;
    }

    /// <summary>
    /// Normalize date from d/M/yyyy or d.M.yyyy to dd/MM/yyyy (ParserConstants.DateFormat).
    /// </summary>
    private static string NormalizeDate(string dateStr, char separator)
    {
        var parts = dateStr.Split(separator);
        if (parts.Length != 3) return dateStr;

        if (int.TryParse(parts[0], out int day) &&
            int.TryParse(parts[1], out int month) &&
            int.TryParse(parts[2], out int year))
        {
            try
            {
                var dt = new DateTime(year, month, day);
                return dt.ToString(ParserConstants.DateFormat, CultureInfo.InvariantCulture);
            }
            catch
            {
                return dateStr;
            }
        }
        return dateStr;
    }

    // ── Result building ──

    private static Result CreateResult(MastersRecord rec, string poolType)
    {
        var nameParts = rec.SwimmerName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts.Length > 0 ? nameParts[0] : "";
        var lastName = nameParts.Length > 1 ? nameParts[1] : "";

        var eventStr = rec.IsRelay
            ? $"{rec.Distance} {rec.StyleName} relay - {rec.Gender} {rec.AgeGroup}"
            : $"{rec.Distance} {rec.StyleName} - {rec.Gender} {rec.AgeGroup}";

        return new Result(
            Country: "ISR",
            Competition: "Israeli Masters Records",
            IsMasters: "true",
            IsAward: false,
            AgeGroup: rec.AgeGroup,
            Date: rec.RecordDate,
            Event: eventStr,
            EventStyleName: rec.StyleName,
            EventStyleLen: rec.Distance,
            EventStyleGender: rec.Gender,
            EventStyleAge: ExtractAgeMidpoint(rec.AgeGroup).ToString(),
            PoolType: poolType,
            Position: 1,
            Heat: 0,
            Lane: 0,
            LastName: lastName,
            FirstName: firstName,
            LastNameEn: "",
            FirstNameEn: "",
            BirthYear: 0,
            Club: rec.Club,
            ClubEn: "",
            Time: rec.Time,
            TimeFail: false,
            TimeFailNote: null,
            InternationalPoints: 0,
            Note: $"Masters Record {rec.AgeGroup}",
            IsRelay: rec.IsRelay,
            RelayTeamName: rec.IsRelay ? rec.Club : null,
            RelaySwimmersName: null,
            RelaySwimmers: null
        );
    }

    private static int ExtractAgeMidpoint(string ageGroup)
    {
        var m = AgeGroupRx.Match(ageGroup);
        if (m.Success && int.TryParse(m.Groups[1].Value, out int lo) && int.TryParse(m.Groups[2].Value, out int hi))
            return (lo + hi) / 2;
        return 0;
    }

    // ── Normative output ──

    private MastersNormativeOutput BuildNormativeOutput(List<MastersRecord> records, Dictionary<string, string> updateDates)
    {
        var fallbackDate = DateTime.UtcNow.ToString(ParserConstants.DateFormat, CultureInfo.InvariantCulture);
        string lastUpdate50m = updateDates.GetValueOrDefault("50m", "");
        string lastUpdate25m = updateDates.GetValueOrDefault("25m", "");

        Log($"Update dates: 50m='{lastUpdate50m}', 25m='{lastUpdate25m}'");

        var normatives = new Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, MastersNormativeEntry>>>>>();

        foreach (var rec in records)
        {
            var gender = rec.Gender;
            var poolKey = rec.PoolType + "_pool";
            var style = rec.StyleName;
            var distKey = rec.Distance + "m";
            var ageKey = rec.AgeGroup;

            if (!normatives.ContainsKey(gender))
                normatives[gender] = new();
            if (!normatives[gender].ContainsKey(poolKey))
                normatives[gender][poolKey] = new();
            if (!normatives[gender][poolKey].ContainsKey(style))
                normatives[gender][poolKey][style] = new();
            if (!normatives[gender][poolKey][style].ContainsKey(distKey))
                normatives[gender][poolKey][style][distKey] = new();

            var entry = new MastersNormativeEntry(
                Time: FormatTime(rec.Time),
                Name: rec.SwimmerName,
                Club: rec.Club,
                RecordDate: rec.RecordDate
            );

            // If duplicate age key, keep the faster time
            if (normatives[gender][poolKey][style][distKey].TryGetValue(ageKey, out var existing))
            {
                if (string.Compare(rec.Time, existing.Time, StringComparison.Ordinal) < 0)
                    normatives[gender][poolKey][style][distKey][ageKey] = entry;
            }
            else
            {
                normatives[gender][poolKey][style][distKey][ageKey] = entry;
            }
        }

        Log($"Normative output: {normatives.Count} genders");
        return new MastersNormativeOutput(lastUpdate50m, lastUpdate25m, normatives);
    }

    private static string FormatTime(string time)
    {
        if (time.StartsWith("00:"))
            return time[3..];
        return time;
    }
}
