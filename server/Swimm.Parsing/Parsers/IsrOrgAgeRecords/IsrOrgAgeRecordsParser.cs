using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Swimm.Parsing.Models;
using Swimm.Parsing.Helpers;

namespace Swimm.Parsing.Parsers.IsrOrgAgeRecords;

public class IsrOrgAgeRecordsParser : IFormatParser
{
    public string FormatName => "IsrOrgAgeRecords";

    private readonly List<string> _debugLog = new();

    // Hebrew constants for style detection
    private const string HebrewMeter = "\u05DE\u05F3";       // ?? (????)
    private const string HebrewMeterRev = "\u05F3\u05DE";    // ?? (????, ??????)
    private const string HebrewHofshi = "\u05D7\u05D5\u05E4\u05E9\u05D9";       // ????? (??????? ?????)
    private const string HebrewHaze = "\u05D7\u05D6\u05D4";                      // ??? (?????)
    private const string HebrewGav = "\u05D2\u05D1";                             // ?? (?? ?????)
    private const string HebrewParpar = "\u05E4\u05E8\u05E4\u05E8";             // ???? (??????????)
    private const string HebrewMeoravIshi = "\u05DE\u05E2\u05D5\u05E8\u05D1-\u05D0\u05D9\u05E9\u05D9"; // ?????-???? (???????? ??????????????)
    private const string HebrewMeorav = "\u05DE\u05E2\u05D5\u05E8\u05D1";       // ????? (????????)
    private const string HebrewShlichim = "\u05E9\u05DC\u05D9\u05D7\u05D9\u05DD";   // ?????? (????????, ???.)
    private const string HebrewShlichot = "\u05E9\u05DC\u05D9\u05D7\u05D5\u05EA";   // ?????? (????????, ???.)
    private const string HebrewShlichimRev = "\u05DD\u05D9\u05D7\u05D9\u05DC\u05E9"; // ?????? (???????? ???., ??????)
    private const string HebrewShlichotRev = "\u05EA\u05D5\u05D7\u05D9\u05DC\u05E9"; // ?????? (???????? ???., ??????)

    // Gender words - original
    private const string HebrewGvarim = "\u05D2\u05D1\u05E8\u05D9\u05DD";       // ????? (???????)
    private const string HebrewNashim = "\u05E0\u05E9\u05D9\u05DD";             // ???? (???????)
    private const string HebrewMix = "\u05DE\u05D9\u05E7\u05E1";                // ???? (????)
    // Gender words - reversed
    private const string HebrewGvarimRev = "\u05DD\u05D9\u05E8\u05D1\u05D2";    // ????? (???????, ??????)
    private const string HebrewNashimRev = "\u05DD\u05D9\u05E9\u05E0";          // ???? (???????, ??????)

    // Age/category words
    private const string HebrewIsrael = "\u05D9\u05E9\u05E8\u05D0\u05DC";       // ????? (???????)
    private const string HebrewIsraelRev = "\u05DC\u05D0\u05E8\u05E9\u05D9";    // ????? (???????, ??????)
    private const string HebrewGil = "\u05D2\u05D9\u05DC";                       // ??? (???????)
    private const string HebrewBogrim = "\u05D1\u05D5\u05D2\u05E8\u05D9\u05DD"; // ?????? (???????? ???.)
    private const string HebrewBogrimRev = "\u05DD\u05D9\u05E8\u05D2\u05D5\u05D1"; // ?????? (???????? ???., ??????)
    private const string HebrewBogrot = "\u05D1\u05D5\u05D2\u05E8\u05D5\u05EA"; // ?????? (???????? ???.)
    private const string HebrewBogrotRev = "\u05EA\u05D5\u05E8\u05D2\u05D5\u05D1"; // ?????? (???????? ???., ??????)

    // Venue marker
    private const string HebrewBrikha = "\u05D1\u05E8\u05D9\u05DB\u05D4";       // ????? (???????)
    private const string HebrewBrikhaRev = "\u05D4\u05DB\u05D9\u05E8\u05D1";    // ????? (???????, ??????)

    // Table header words (as they appear in PDF - reversed char order)
    private const string HebrewMerhakRev = "\u05E7\u05D7\u05E8\u05DE";     // ???? (?????????, ??????)
    private const string HebrewSignonRev = "\u05DF\u05D5\u05E0\u05D2\u05E1"; // ????? (?????, ??????)
    private const string HebrewMinRev = "\u05DF\u05D9\u05DE";               // ??? (???, ??????)
    private const string HebrewGilRev = "\u05DC\u05D9\u05D2";               // ??? (???????, ??????)
    private const string HebrewShiRev = "\u05D0\u05D9\u05E9";               // ??? (??????, ??????)
    private const string HebrewTotzaaRev = "\u05D4\u05D0\u05E6\u05D5\u05EA"; // ????? (?????????, ??????)
    private const string HebrewTaarichRev = "\u05DA\u05D9\u05E8\u05D0\u05EA"; // ????? (????, ??????)
    private const string HebrewShemRev = "\u05DD\u05E9";                     // ?? (???, ??????)
    private const string HebrewMdinaRev = "\u05D4\u05E0\u05D9\u05D3\u05DE"; // ????? (??????, ??????)
    private const string HebrewAgudaRev = "\u05D4\u05D3\u05D5\u05D2\u05D0"; // ????? (????, ??????)
    private const string HebrewMakomRev = "\u05DD\u05D5\u05E7\u05DE";       // ???? (?????, ??????)
    // Header words - original
    private const string HebrewMerhak = "\u05DE\u05E8\u05D7\u05E7";   // ???? (?????????)
    private const string HebrewSignon = "\u05E1\u05D2\u05E0\u05D5\u05DF"; // ????? (?????)
    private const string HebrewMin = "\u05DE\u05D9\u05DF";             // ??? (???)
    private const string HebrewShi = "\u05E9\u05D9\u05D0";             // ??? (??????)
    private const string HebrewTotzaa = "\u05EA\u05D5\u05E6\u05D0\u05D4"; // ????? (?????????)
    private const string HebrewTaarich = "\u05EA\u05D0\u05E8\u05D9\u05DA"; // ????? (????)
    private const string HebrewShem = "\u05E9\u05DD";                   // ?? (???)
    private const string HebrewMdina = "\u05DE\u05D3\u05D9\u05E0\u05D4"; // ????? (??????)
    private const string HebrewAguda = "\u05D0\u05D2\u05D5\u05D3\u05D4"; // ????? (????)
    private const string HebrewMakom = "\u05DE\u05E7\u05D5\u05DD";     // ???? (?????)
    // Swimmer header word
    private const string HebrewSachyan = "\u05D4\u05E9\u05D7\u05D9\u05D9\u05DF";   // ?????? (??????)
    private const string HebrewSachyanRev = "\u05DF\u05D9\u05D9\u05D7\u05E9\u05D4"; // ?????? (??????, ??????)

    // Multi-word header: "????" — ?????? ????? ?????????? ?????????
    // Original: \u05D4\u05E9\u05D9\u05D0  Reversed: \u05D0\u05D9\u05E9\u05D4
    private const string HebrewHaShi = "\u05D4\u05E9\u05D9\u05D0";       // ???? (??????, ? ?????????)
    private const string HebrewHaShiRev = "\u05D0\u05D9\u05E9\u05D4";   // ???? ? ???????? ???????

    private static readonly Regex TimeRx = new(@"\d{2}:\d{2}\.\d{1,2}", RegexOptions.Compiled);
    private static readonly Regex DateRx = new(@"\d{2}/\d{2}/\d{4}", RegexOptions.Compiled);

    public IEnumerable<Result> Parse(ParseRequest request)
    {
        _debugLog.Clear();
        return ParseAllStreams(request).ToList();
    }

    public object? ParseNormative(ParseRequest request)
    {
        _debugLog.Clear();
        var results = ParseAllStreams(request).ToList();
        return BuildNormativeOutput(results);
    }

    public string GetDebugLog() => string.Join("\n", _debugLog);

    private void Log(string message)
    {
        _debugLog.Add($"[{_debugLog.Count + 1}] {message}");
    }

    private IEnumerable<Result> ParseAllStreams(ParseRequest request)
    {
        var primaryPoolType = request.PoolType ?? "25m";
        Log($"Parsing primary file: {request.PrimaryFileName}, poolType={primaryPoolType}");
        foreach (var r in ParseStream(request.PrimaryStream, primaryPoolType))
            yield return r;

        if (request.SecondaryStream != null)
        {
            Log($"Parsing secondary file: {request.SecondaryFileName}, poolType=50m");
            foreach (var r in ParseStream(request.SecondaryStream, "50m"))
                yield return r;
        }
    }

    /// <summary>
    /// Column layout detected from the table header row.
    /// Stores the center X coordinates of each column header.
    /// </summary>
    private class ColumnLayout
    {
        public double DistanceX { get; set; } = -1;
        public double StyleX { get; set; } = -1;
        public double GenderX { get; set; } = -1;
        public double AgeX { get; set; } = -1;
        public double TimeX { get; set; } = -1;
        public double DateX { get; set; } = -1;
        public double NameX { get; set; } = -1;
        public double ClubX { get; set; } = -1;
        public double PlaceX { get; set; } = -1;

        public bool IsValid =>
            DistanceX >= 0 && StyleX >= 0 && GenderX >= 0 && AgeX >= 0 && TimeX >= 0;
    }

    private IEnumerable<Result> ParseStream(Stream stream, string poolTypeOverride)
    {
        Log($"Starting IsrOrgAgeRecords parse (column-based v6), poolTypeOverride={poolTypeOverride}");

        using var doc = PdfDocument.Open(stream);
        Log($"PDF opened, pages={doc.NumberOfPages}");
        string poolType = poolTypeOverride;

        // ===== Pass 1: Collect all rows across all pages, detect layout =====
        ColumnLayout? layout = null;
        var allRows = new List<(List<Word> words, double yGlobal, ColumnLayout layout)>();
        double pageYOffset = 0;
        const double PageGap = 10000;

        foreach (var page in doc.GetPages())
        {
            Log($"--- Page {page.Number} ---");
            var allWords = page.GetWords().ToList();
            Log($"Page {page.Number}: {allWords.Count} words");

            var rows = allWords
                .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 3.0) * 3.0)
                .OrderByDescending(g => g.Key)
                .Select(g => (
                    words: g.OrderBy(w => w.BoundingBox.Left).ToList(),
                    yCenter: g.Average(w => (w.BoundingBox.Bottom + w.BoundingBox.Top) / 2.0)
                ))
                .ToList();

            Log($"Page {page.Number}: {rows.Count} rows");

            // Debug dump first rows
            if (page.Number <= 2)
            {
                for (int ri = 0; ri < Math.Min(5, rows.Count); ri++)
                {
                    var dump = string.Join(" | ", rows[ri].words.Select(w => $"'{w.Text}'@{w.BoundingBox.Left:F0}"));
                    Log($"  RAW ROW[{ri}]: {dump}");
                }
            }

            foreach (var row in rows)
            {
                // Try to detect header
                if (layout == null || ContainsHeaderWord(row.words))
                {
                    var detected = TryDetectColumns(row.words);
                    if (detected != null && detected.IsValid)
                    {
                        // Carry forward NameX from previous layout if new header doesn't have it
                        if (detected.NameX < 0 && layout != null && layout.NameX >= 0)
                        {
                            detected.NameX = layout.NameX;
                            Log($"    -> Carried forward NameX={detected.NameX:F0} from previous layout");
                        }
                        layout = detected;
                        Log($"    -> HEADER DETECTED: dist={layout.DistanceX:F0}, style={layout.StyleX:F0}, " +
                            $"gender={layout.GenderX:F0}, age={layout.AgeX:F0}, time={layout.TimeX:F0}, " +
                            $"date={layout.DateX:F0}, name={layout.NameX:F0}, club={layout.ClubX:F0}, place={layout.PlaceX:F0}");
                        continue;
                    }
                }

                if (layout == null)
                {
                    var preText = string.Join(' ', row.words.Select(w => NormalizeWordChars(w.Text)));
                    if (preText.Contains("\u05D1\u05E8\u05D9\u05DB\u05D4"))
                    {
                        if (preText.Contains("25")) poolType = "25m";
                        else if (preText.Contains("50")) poolType = "50m";
                    }
                    continue;
                }

                // Add row with global Y and the current layout snapshot
                allRows.Add((row.words, row.yCenter + pageYOffset, layout));
            }

            pageYOffset -= PageGap; // each page shifts Y down by PageGap
        }

        if (layout == null)
        {
            Log("ERROR: No column layout detected");
            yield break;
        }

        Log($"Pass 1 complete: {allRows.Count} data rows collected");

        // ===== Pass 2: Parse columns, classify each row =====
        var parsedRows = new List<ParsedRow>();

        for (int i = 0; i < allRows.Count; i++)
        {
            var (rowWords, yCenter, rowLayout) = allRows[i];
            var cols = AssignWordsToColumns(rowWords, rowLayout);

            var distRaw = NormalizeColumnValue(cols.distance);
            var styleRaw = NormalizeColumnValue(cols.style);
            var genderRaw = NormalizeColumnValue(cols.gender);
            var ageRaw = NormalizeColumnValue(cols.age);
            var timeRaw = NormalizeColumnValue(cols.time);
            var dateRaw = NormalizeColumnValue(cols.date);
            var nameRaw = NormalizeColumnValue(cols.name);
            var clubRaw = NormalizeColumnValue(cols.club);

            bool hasTime = TimeRx.IsMatch(timeRaw);

            var rowTextForLog = string.Join(' ', rowWords.Select(w => w.Text));
            Log($"  ROW[{i}] y={yCenter:F0}: '{(rowTextForLog.Length > 100 ? rowTextForLog[..100] : rowTextForLog)}'");
            Log($"    -> COLS: dist='{distRaw}' style='{styleRaw}' gender='{genderRaw}' " +
                $"age='{ageRaw}' time='{timeRaw}' date='{dateRaw}' name='{nameRaw}' club='{clubRaw}'");

            // Detect what this row contributes
            string? detectedGender = null;
            string? detectedDist = null;
            string? detectedStyle = null;
            bool detectedIsRelay = false;

            // Gender detection (always, even if row also has dist/style)
            if (!string.IsNullOrWhiteSpace(genderRaw))
            {
                detectedGender = ResolveGender(genderRaw);
            }

            // Dist/style detection (always, independent of gender)
            if (!hasTime)
            {
                if (!string.IsNullOrWhiteSpace(distRaw))
                {
                    var relay = Regex.Match(distRaw, @"(\d+)\s*[xX]\s*(\d+)");
                    if (relay.Success)
                    {
                        detectedDist = $"{relay.Groups[1].Value}X{relay.Groups[2].Value}";
                        detectedIsRelay = true;
                    }
                    else
                    {
                        var num = Regex.Match(distRaw, @"\d+");
                        if (num.Success) detectedDist = num.Value;
                    }
                }

                if (!string.IsNullOrWhiteSpace(styleRaw))
                {
                    if (ContainsAny(styleRaw, HebrewShlichim, HebrewShlichot, HebrewShlichimRev, HebrewShlichotRev))
                    {
                        var clean = styleRaw;
                        foreach (var mk in new[] { HebrewShlichim, HebrewShlichot, HebrewShlichimRev, HebrewShlichotRev })
                            clean = clean.Replace(mk, "");
                        detectedStyle = NormalizeStyleHe(clean.Trim());
                        detectedIsRelay = true;
                    }
                    else
                    {
                        var s = NormalizeStyleHe(styleRaw);
                        if (!string.IsNullOrEmpty(s)) detectedStyle = s;
                    }
                }
            }

            if (detectedGender != null)
                Log($"    -> GENDER: {detectedGender}");
            if (detectedDist != null || detectedStyle != null)
                Log($"    -> DIST/STYLE: dist='{detectedDist}' style='{detectedStyle}' relay={detectedIsRelay}");

            parsedRows.Add(new ParsedRow
            {
                Index = i,
                YCenter = yCenter,
                HasTime = hasTime,
                DistRaw = distRaw,
                StyleRaw = styleRaw,
                GenderRaw = genderRaw,
                AgeRaw = ageRaw,
                TimeRaw = timeRaw,
                DateRaw = dateRaw,
                NameRaw = nameRaw,
                ClubRaw = clubRaw,
                NameWords = cols.name,
                ClubWords = cols.club,
                DateWords = cols.date,
                RowLayout = rowLayout,
                DetectedGender = detectedGender,
                DetectedDist = detectedDist,
                DetectedStyle = detectedStyle,
                DetectedIsRelay = detectedIsRelay
            });
        }

        // ===== Pass 3: Forward + backward sweep to assign dist/style and gender =====
        // Dist and style are tracked INDEPENDENTLY because they are separate merged cells
        // with potentially different rowspans (especially in relay tables with 3 genders).

        // 3a. Collect all marker positions - dist and style separately
        var distMarkers = new List<(int index, string dist, bool isRelay, double y)>();
        var styleMarkers = new List<(int index, string style, bool isRelay, double y)>();
        var genderMarkers = new List<(int index, string gender, double y)>();

        for (int i = 0; i < parsedRows.Count; i++)
        {
            var pr = parsedRows[i];
            if (pr.DetectedGender != null)
                genderMarkers.Add((i, pr.DetectedGender, pr.YCenter));
            if (pr.DetectedDist != null)
                distMarkers.Add((i, pr.DetectedDist, pr.DetectedIsRelay, pr.YCenter));
            if (pr.DetectedStyle != null)
                styleMarkers.Add((i, pr.DetectedStyle, pr.DetectedIsRelay, pr.YCenter));
        }

        Log($"Gender markers: {genderMarkers.Count}");
        foreach (var (idx, g, y) in genderMarkers)
            Log($"  [{idx}] {g} at y={y:F0}");

        Log($"Dist markers: {distMarkers.Count}");
        foreach (var (idx, d, r, y) in distMarkers)
            Log($"  [{idx}] dist='{d}' relay={r} at y={y:F0}");

        Log($"Style markers: {styleMarkers.Count}");
        foreach (var (idx, s, r, y) in styleMarkers)
            Log($"  [{idx}] style='{s}' relay={r} at y={y:F0}");

        // 3b. Forward + backward sweep for DIST
        var fwdDist = new (string dist, bool isRelay, double markerY)?[parsedRows.Count];
        var bwdDist = new (string dist, bool isRelay, double markerY)?[parsedRows.Count];

        {
            // Build row->marker map for dist
            var distMarkerSet = new Dictionary<int, (string dist, bool isRelay, double y)>();
            foreach (var m in distMarkers)
                distMarkerSet[m.index] = (m.dist, m.isRelay, m.y);

            (string dist, bool isRelay, double markerY)? cur = null;
            for (int i = 0; i < parsedRows.Count; i++)
            {
                if (distMarkerSet.TryGetValue(i, out var dm))
                    cur = (dm.dist, dm.isRelay, dm.y);
                fwdDist[i] = cur;
            }
            cur = null;
            for (int i = parsedRows.Count - 1; i >= 0; i--)
            {
                if (distMarkerSet.TryGetValue(i, out var dm))
                    cur = (dm.dist, dm.isRelay, dm.y);
                bwdDist[i] = cur;
            }
        }

        // 3c. Forward + backward sweep for STYLE
        var fwdStyle = new (string style, bool isRelay, double markerY)?[parsedRows.Count];
        var bwdStyle = new (string style, bool isRelay, double markerY)?[parsedRows.Count];

        {
            var styleMarkerSet = new Dictionary<int, (string style, bool isRelay, double y)>();
            foreach (var m in styleMarkers)
                styleMarkerSet[m.index] = (m.style, m.isRelay, m.y);

            (string style, bool isRelay, double markerY)? cur = null;
            for (int i = 0; i < parsedRows.Count; i++)
            {
                if (styleMarkerSet.TryGetValue(i, out var sm))
                    cur = (sm.style, sm.isRelay, sm.y);
                fwdStyle[i] = cur;
            }
            cur = null;
            for (int i = parsedRows.Count - 1; i >= 0; i--)
            {
                if (styleMarkerSet.TryGetValue(i, out var sm))
                    cur = (sm.style, sm.isRelay, sm.y);
                bwdStyle[i] = cur;
            }
        }

        // ===== Pass 4: Build results =====
        var completedResults = new List<Result>();

        foreach (var pr in parsedRows)
        {
            if (!pr.HasTime) continue;

            var time = TimeRx.Match(pr.TimeRaw).Value;
            var date = DateRx.IsMatch(pr.DateRaw) ? DateRx.Match(pr.DateRaw).Value : "";
            var ageCategory = ExtractAgeCategory(pr.AgeRaw);
            var (swimmerName, club) = SplitNameAndClub(pr.NameWords, pr.ClubWords, pr.RowLayout);

            // Gender: nearest marker
            string gender = DetermineGender(pr.YCenter, genderMarkers);

            // Dist: resolve from forward/backward sweep independently
            var (distance, distIsRelay) = ResolveChannel(pr.YCenter, fwdDist[pr.Index], bwdDist[pr.Index]);

            // Style: resolve from forward/backward sweep independently
            var (style, styleIsRelay) = ResolveChannel(pr.YCenter, fwdStyle[pr.Index], bwdStyle[pr.Index]);

            bool isRelay = distIsRelay || styleIsRelay;

            Log($"    -> RESULT: dist={distance}, style={style}, gender={gender}, age={ageCategory}, " +
                $"time={time}, name='{swimmerName}', club='{club}', relay={isRelay}");

            if (!string.IsNullOrEmpty(distance) && !string.IsNullOrEmpty(style))
            {
                completedResults.Add(CreateResult(
                    style, distance, gender,
                    ageCategory, time, date,
                    swimmerName, club, "",
                    poolType, isRelay, null));
            }
            else
            {
                Log($"    -> SKIPPED (no dist/style context): dist='{distance}' style='{style}'");
            }
        }

        foreach (var r in completedResults)
            yield return r;

        Log($"Parse complete. Total results: {completedResults.Count}");
    }

    /// <summary>
    /// Resolve a single channel (dist or style) from forward and backward sweep.
    /// Picks the sweep whose originating marker is closer by Y to this row.
    /// </summary>
    private static (string value, bool isRelay) ResolveChannel(
        double rowY,
        (string value, bool isRelay, double markerY)? fwd,
        (string value, bool isRelay, double markerY)? bwd)
    {
        if (fwd.HasValue && bwd.HasValue)
        {
            if (fwd.Value.value == bwd.Value.value)
                return (fwd.Value.value, fwd.Value.isRelay || bwd.Value.isRelay);

            double fwdDist = Math.Abs(rowY - fwd.Value.markerY);
            double bwdDist = Math.Abs(rowY - bwd.Value.markerY);
            var pick = fwdDist <= bwdDist ? fwd.Value : bwd.Value;
            return (pick.value, pick.isRelay);
        }
        if (fwd.HasValue) return (fwd.Value.value, fwd.Value.isRelay);
        if (bwd.HasValue) return (bwd.Value.value, bwd.Value.isRelay);
        return ("", false);
    }

    /// <summary>
    /// Determine gender for a data row based on nearest gender marker Y position.
    /// </summary>
    private static string DetermineGender(double rowY, List<(int index, string gender, double y)> genderMarkers)
    {
        if (genderMarkers.Count == 0) return "male";

        string best = "male";
        double bestDist = double.MaxValue;
        foreach (var (_, g, gy) in genderMarkers)
        {
            var d = Math.Abs(rowY - gy);
            if (d < bestDist)
            {
                bestDist = d;
                best = g;
            }
        }
        return best;
    }

    private class ParsedRow
    {
        public int Index { get; set; }
        public double YCenter { get; set; }
        public bool HasTime { get; set; }
        public string DistRaw { get; set; } = "";
        public string StyleRaw { get; set; } = "";
        public string GenderRaw { get; set; } = "";
        public string AgeRaw { get; set; } = "";
        public string TimeRaw { get; set; } = "";
        public string DateRaw { get; set; } = "";
        public string NameRaw { get; set; } = "";
        public string ClubRaw { get; set; } = "";
        // Raw Word objects for name+club re-splitting
        public List<Word> NameWords { get; set; } = new();
        public List<Word> ClubWords { get; set; } = new();
        public List<Word> DateWords { get; set; } = new();
        public ColumnLayout RowLayout { get; set; } = null!;
        // Detected marker values (null = not a marker for this type)
        public string? DetectedGender { get; set; }
        public string? DetectedDist { get; set; }
        public string? DetectedStyle { get; set; }
        public bool DetectedIsRelay { get; set; }
    }

    /// <summary>
    /// Reverse Hebrew chars within a single word.
    /// PdfPig stores Hebrew words with reversed character order.
    /// </summary>
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

    /// <summary>
    /// Normalize column value: reverse chars in each Hebrew word,
    /// reverse word order for RTL reading.
    /// </summary>
    private static string NormalizeColumnValue(List<Word> words)
    {
        if (words.Count == 0) return "";
        var parts = words.Select(w => NormalizeWordChars(w.Text)).ToList();
        parts.Reverse();
        return string.Join(' ', parts).Trim();
    }

    private static bool ContainsAny(string text, params string[] candidates)
    {
        foreach (var c in candidates)
            if (text.Contains(c)) return true;
        return false;
    }

    /// <summary>
    /// Resolve gender from a column value.
    /// </summary>
    private static string? ResolveGender(string val)
    {
        if (ContainsAny(val, HebrewGvarim, HebrewGvarimRev)) return "male";
        if (ContainsAny(val, HebrewNashim, HebrewNashimRev)) return "female";
        if (ContainsAny(val, HebrewMix)) return "mix";
        var r = HebrewTextHelper.NormalizeGenderHE(val);
        return r != "none" ? r : null;
    }

    /// <summary>
    /// Check if any word in the row is a known header word.
    /// </summary>
    private static bool ContainsHeaderWord(List<Word> words)
    {
        foreach (var w in words)
            if (IsHeaderWord(w.Text)) return true;
        return false;
    }

    private static bool IsHeaderWord(string t)
    {
        return t == HebrewMerhak || t == HebrewMerhakRev ||
               t == HebrewSignon || t == HebrewSignonRev ||
               t == HebrewMin || t == HebrewMinRev ||
               t == HebrewGil || t == HebrewGilRev ||
               t == HebrewShi || t == HebrewShiRev ||
               t == HebrewHaShi || t == HebrewHaShiRev ||
               t == HebrewTotzaa || t == HebrewTotzaaRev ||
               t == HebrewTaarich || t == HebrewTaarichRev ||
               t == HebrewShem || t == HebrewShemRev ||
               t == HebrewMdina || t == HebrewMdinaRev ||
               t == HebrewAguda || t == HebrewAgudaRev ||
               t == HebrewMakom || t == HebrewMakomRev;
    }

    /// <summary>
    /// Detect column layout from a header row.
    /// </summary>
    private ColumnLayout? TryDetectColumns(List<Word> words)
    {
        var layout = new ColumnLayout();
        int matched = 0;

        foreach (var w in words)
        {
            var t = w.Text;
            double cx = (w.BoundingBox.Left + w.BoundingBox.Right) / 2.0;

            if (t == HebrewMerhak || t == HebrewMerhakRev)
            {
                layout.DistanceX = cx; matched++;
                Log($"      HEADER word '{t}' -> distance at X={cx:F0}");
            }
            else if (t == HebrewSignon || t == HebrewSignonRev)
            {
                layout.StyleX = cx; matched++;
                Log($"      HEADER word '{t}' -> style at X={cx:F0}");
            }
            else if (t == HebrewMin || t == HebrewMinRev)
            {
                layout.GenderX = cx; matched++;
                Log($"      HEADER word '{t}' -> gender at X={cx:F0}");
            }
            else if (t == HebrewShi || t == HebrewShiRev)
            {
                if (layout.AgeX < 0) { layout.AgeX = cx; matched++; }
                Log($"      HEADER word '{t}' -> age at X={cx:F0}");
            }
            else if (t == HebrewTotzaa || t == HebrewTotzaaRev)
            {
                layout.TimeX = cx; matched++;
                Log($"      HEADER word '{t}' -> time at X={cx:F0}");
            }
            else if (t == HebrewTaarich || t == HebrewTaarichRev)
            {
                layout.DateX = cx; matched++;
                Log($"      HEADER word '{t}' -> date at X={cx:F0}");
            }
            else if (t == HebrewHaShi || t == HebrewHaShiRev)
            {
                if (layout.DateX >= 0)
                {
                    layout.DateX = (layout.DateX + cx) / 2.0;
                    Log($"      HEADER word '{t}' -> date refined (avg) to X={layout.DateX:F0}");
                }
                else
                {
                    layout.DateX = cx; matched++;
                    Log($"      HEADER word '{t}' -> date at X={cx:F0}");
                }
            }
            else if (t == HebrewShem || t == HebrewShemRev)
            {
                layout.NameX = cx; matched++;
                Log($"      HEADER word '{t}' -> name at X={cx:F0}");
            }
            else if (t == HebrewSachyan || t == HebrewSachyanRev)
            {
                if (layout.NameX >= 0)
                {
                    layout.NameX = (layout.NameX + cx) / 2.0;
                    Log($"      HEADER word '{t}' -> name refined (avg) to X={layout.NameX:F0}");
                }
                else
                {
                    layout.NameX = cx; matched++;
                    Log($"      HEADER word '{t}' -> swimmer/name at X={cx:F0}");
                }
            }
            else if (t == HebrewMdina || t == HebrewMdinaRev ||
                     t == HebrewAguda || t == HebrewAgudaRev)
            {
                layout.ClubX = cx; matched++;
                Log($"      HEADER word '{t}' -> club at X={cx:F0}");
            }
            else if (t == HebrewMakom || t == HebrewMakomRev)
            {
                layout.PlaceX = cx; matched++;
                Log($"      HEADER word '{t}' -> place at X={cx:F0}");
            }
            else if (t.Contains('/') || t.Contains('\\'))
            {
                var parts = t.Split('/', '\\');
                foreach (var part in parts)
                {
                    var p = part.Trim();
                    if (p == HebrewMdina || p == HebrewMdinaRev ||
                        p == HebrewAguda || p == HebrewAgudaRev)
                    {
                        layout.ClubX = cx; matched++;
                        Log($"      HEADER word '{t}' (compound) -> club at X={cx:F0}");
                        break;
                    }
                }
            }
        }

        Log($"      HEADER matched={matched}, valid={layout.IsValid}");
        return matched >= 4 ? layout : null;
    }

    /// <summary>
    /// Assign each word in a row to the nearest column by X center distance.
    /// Place column is included to prevent venue words from contaminating other columns.
    /// </summary>
    private (List<Word> distance, List<Word> style, List<Word> gender, List<Word> age,
             List<Word> time, List<Word> date, List<Word> name, List<Word> club, List<Word> place)
        AssignWordsToColumns(List<Word> rowWords, ColumnLayout layout)
    {
        var dist = new List<Word>();
        var style = new List<Word>();
        var gender = new List<Word>();
        var age = new List<Word>();
        var time = new List<Word>();
        var date = new List<Word>();
        var name = new List<Word>();
        var club = new List<Word>();
        var place = new List<Word>();

        var columns = new List<(string id, double x, List<Word> bucket)>();
        if (layout.DistanceX >= 0) columns.Add(("dist", layout.DistanceX, dist));
        if (layout.StyleX >= 0) columns.Add(("style", layout.StyleX, style));
        if (layout.GenderX >= 0) columns.Add(("gender", layout.GenderX, gender));
        if (layout.AgeX >= 0) columns.Add(("age", layout.AgeX, age));
        if (layout.TimeX >= 0) columns.Add(("time", layout.TimeX, time));
        if (layout.DateX >= 0) columns.Add(("date", layout.DateX, date));
        if (layout.NameX >= 0) columns.Add(("name", layout.NameX, name));
        if (layout.ClubX >= 0) columns.Add(("club", layout.ClubX, club));
        if (layout.PlaceX >= 0) columns.Add(("place", layout.PlaceX, place));

        foreach (var w in rowWords)
        {
            double wx = (w.BoundingBox.Left + w.BoundingBox.Right) / 2.0;
            double minDist = double.MaxValue;
            List<Word>? best = null;
            foreach (var (id, cx, bucket) in columns)
            {
                var d = Math.Abs(wx - cx);
                if (d < minDist) { minDist = d; best = bucket; }
            }
            best?.Add(w);
        }

        return (dist, style, gender, age, time, date, name, club, place);
    }

    /// <summary>
    /// Split name and club using raw Word objects and their X positions.
    /// Merges all words from name+club columns, sorts by X descending (RTL: rightmost = name),
    /// and splits at the boundary between nameX and clubX column centers.
    /// This avoids misattribution when a 3-word name has some words closer to club column.
    /// </summary>
    private (string name, string club) SplitNameAndClub(
        List<Word> nameWords, List<Word> clubWords, ColumnLayout layout)
    {
        // Merge all words from both columns and sort by X descending (RTL order)
        var allWords = nameWords.Concat(clubWords)
            .OrderByDescending(w => w.BoundingBox.Left)
            .ToList();

        if (allWords.Count == 0)
            return ("", "");

        // Strip venue words
        allWords = allWords.Where(w =>
        {
            var norm = NormalizeWordChars(w.Text);
            return !norm.Contains(HebrewBrikha) && !norm.Contains(HebrewBrikhaRev);
        }).ToList();

        if (allWords.Count == 0)
            return ("", "");

        // Calculate boundary X between name and club columns.
        // In RTL: name column is to the RIGHT of club column.
        double boundary;
        if (layout.NameX >= 0 && layout.ClubX >= 0)
        {
            boundary = (layout.NameX + layout.ClubX) / 2.0;
        }
        else
        {
            // Fallback: take first 2 words as name
            var normalized = allWords.Select(w => NormalizeWordChars(w.Text)).ToList();
            if (normalized.Count <= 2)
                return (string.Join(' ', normalized), "");
            return ($"{normalized[0]} {normalized[1]}", string.Join(' ', normalized.Skip(2)));
        }

        // Split: words RIGHT of boundary = name, words LEFT of boundary = club
        // Word center X > boundary -> name; ????? -> club
        var nameList = new List<string>();
        var clubList = new List<string>();
        foreach (var w in allWords)
        {
            double wx = (w.BoundingBox.Left + w.BoundingBox.Right) / 2.0;
            var norm = NormalizeWordChars(w.Text);
            if (wx > boundary)
                nameList.Add(norm);
            else
                clubList.Add(norm);
        }

        // nameList is already sorted RTL (rightmost first) which is correct reading order
        var name = string.Join(' ', nameList).Trim();
        var club = string.Join(' ', clubList).Trim();

        // Strip trailing numbers from name (venue artifacts)
        name = Regex.Replace(name, @"\s+\d{2}\s*$", "").Trim();
        club = Regex.Replace(club, @"\s+\d{2}\s*$", "").Trim();

        return (name, club);
    }

    /// <summary>
    /// Remove venue suffixes like "????? ?????? 25", city names after club, etc.
    /// </summary>
    private static string StripVenue(string text)
    {
        var idx = text.IndexOf(HebrewBrikha);
        if (idx >= 0) text = text[..idx];
        idx = text.IndexOf(HebrewBrikhaRev);
        if (idx >= 0) text = text[..idx];
        text = Regex.Replace(text, @"\s+\d{2}\s*$", "");
        return text.Trim();
    }

    /// <summary>
    /// Extract age category from the age column value.
    /// </summary>
    private string ExtractAgeCategory(string ageVal)
    {
        if (string.IsNullOrWhiteSpace(ageVal)) return "";
        if (ContainsAny(ageVal, HebrewIsrael, HebrewIsraelRev))
            return "israel";
        if (ContainsAny(ageVal, HebrewBogrim, HebrewBogrimRev))
            return "adults_m";
        if (ContainsAny(ageVal, HebrewBogrot, HebrewBogrotRev))
            return "adults_f";
        var numMatch = Regex.Match(ageVal, @"\d+");
        if (numMatch.Success) return numMatch.Value;
        return ageVal;
    }

    private static string NormalizeStyleHe(string styleHe)
    {
        styleHe = styleHe.Replace(HebrewMeter, "").Replace(HebrewMeterRev, "").Trim();
        if (HebrewTextHelper.StyleMapHE.TryGetValue(styleHe, out var mapped))
            return mapped == "medley" ? "individual_medley" : mapped;
        if (styleHe.Contains(HebrewMeoravIshi)) return "individual_medley";
        if (styleHe.Contains(HebrewMeorav)) return "individual_medley";
        if (styleHe.Contains(HebrewHofshi)) return "freestyle";
        if (styleHe.Contains(HebrewHaze)) return "breaststroke";
        if (styleHe.Contains(HebrewGav)) return "backstroke";
        if (styleHe.Contains(HebrewParpar)) return "butterfly";
        return styleHe;
    }

    private static Result CreateResult(
        string styleName, string distance, string gender,
        string ageCategory, string time, string date,
        string swimmerName, string club, string venue,
        string poolType, bool isRelay, string? relaySwimmersInline)
    {
        var nameParts = swimmerName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts.Length > 0 ? nameParts[0] : "";
        var lastName = nameParts.Length > 1 ? nameParts[1] : "";

        var eventStyleAge = ageCategory switch
        {
            "israel" => "0",
            "adults_m" or "adults_f" => "18",
            _ => ageCategory
        };

        var ageGroup = ageCategory switch
        {
            "israel" => "national",
            "adults_m" or "adults_f" => "adults",
            _ => AgeGroupHelper.GetAgeGroup(int.TryParse(ageCategory, out var a) ? a : 0)
        };

        List<RelaySwimmer>? relaySwimmers = null;
        string? relaySwimmersName = null;
        if (isRelay && !string.IsNullOrEmpty(relaySwimmersInline))
        {
            var swimmerNames = relaySwimmersInline.Split(',', StringSplitOptions.RemoveEmptyEntries);
            relaySwimmers = new List<RelaySwimmer>();
            int order = 1;
            foreach (var sn in swimmerNames)
            {
                var snParts = sn.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                relaySwimmers.Add(new RelaySwimmer(
                    Order: order++,
                    FirstName: snParts.Length > 0 ? snParts[0] : "",
                    LastName: snParts.Length > 1 ? snParts[1] : "",
                    BirthYear: null, Club: null, SplitTime: null));
            }
            relaySwimmersName = relaySwimmersInline.Trim();
        }

        var eventStr = isRelay
            ? $"{distance} {styleName} relay - {gender} {ageCategory}"
            : $"{distance} {styleName} - {gender} {ageCategory}";

        return new Result(
            Country: "ISR", Competition: "Israeli Age Records",
            IsMasters: "false", IsAward: false,
            AgeGroup: ageGroup, Date: date, Event: eventStr,
            EventStyleName: styleName, EventStyleLen: distance,
            EventStyleGender: gender, EventStyleAge: eventStyleAge,
            PoolType: poolType, Position: 1, Heat: 0, Lane: 0,
            LastName: lastName, FirstName: firstName,
            LastNameEn: "", FirstNameEn: "",
            BirthYear: 0, Club: club, ClubEn: "",
            Time: time, TimeFail: false, TimeFailNote: null,
            InternationalPoints: 0,
            Note: ageCategory == "israel" ? "National Record" : $"Age {ageCategory} Record",
            IsRelay: isRelay,
            RelayTeamName: isRelay ? club : null,
            RelaySwimmersName: relaySwimmersName,
            RelaySwimmers: relaySwimmers
        );
    }

    private static NormativeOutput BuildNormativeOutput(List<Result> results)
    {
        var normatives = new Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, NormativeEntry>>>>>();

        foreach (var r in results)
        {
            var gender = r.EventStyleGender;
            var poolKey = r.PoolType + "_pool";
            var style = r.EventStyleName;
            var distKey = r.EventStyleLen + "m";
            var ageKey = NormalizeAgeKey(r);
            var timeStr = FormatTime(r.Time);
            var name = $"{r.FirstName} {r.LastName}".Trim();

            if (!normatives.ContainsKey(gender))
                normatives[gender] = new();
            if (!normatives[gender].ContainsKey(poolKey))
                normatives[gender][poolKey] = new();
            if (!normatives[gender][poolKey].ContainsKey(style))
                normatives[gender][poolKey][style] = new();
            if (!normatives[gender][poolKey][style].ContainsKey(distKey))
                normatives[gender][poolKey][style][distKey] = new();

            normatives[gender][poolKey][style][distKey][ageKey] = new NormativeEntry(
                Time: timeStr, Name: name, Club: r.Club,
                Country: r.Country, RecordDate: r.Date);
        }

        return new NormativeOutput(normatives);
    }

    private static string NormalizeAgeKey(Result r)
    {
        if (r.Note == "National Record" || r.EventStyleAge == "0")
            return "ISR";
        if (r.EventStyleAge == "18" && (r.Note == "Age adults_m Record" || r.Note == "Age adults_f Record"))
            return "adults";
        return r.EventStyleAge;
    }

    private static string FormatTime(string time)
    {
        if (time.StartsWith("00:"))
            return time[3..];
        return time;
    }
}
