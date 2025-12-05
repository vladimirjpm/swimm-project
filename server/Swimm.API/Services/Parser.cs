using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Swimm.API.Services
{
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
        [property: JsonPropertyName("international_points")] int InternationalPoints
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
        [property: JsonPropertyName("international_points")] int InternationalPoints,
        [property: JsonPropertyName("note")] string? Note
    );

    public static class Parser
    {
        private static readonly Regex headerRxHE =
            new(@"^(?<len>\d+)\s+(?<style>.+?)\s*-\s*(?<gender>בנות|בנים)\s+(?<age>\d+)$");
        private static readonly Regex headerRxEN =
            new(@"^(?<len>\d+m?)\s+(?<style>.+?)\s*-\s*(?<gender>female|male|girls|boys)\s+(?<age>\d+)$",
                RegexOptions.IgnoreCase);
        private static readonly Regex fullResultRx =
            new(@"^\d+\s+\d+\s+\d+.*\d{2}:\d{2}\.\d{2}\s+\d+$");

        private static readonly Dictionary<string, string> styleMapHE = new()
        {
            ["חופשי"] = "freestyle",
            ["פרפר"] = "butterfly",
            ["גב"] = "backstroke",
            ["חזה"] = "breaststroke",
            ["מעורב אישי"] = "individual_medley"
        };

        private static IEnumerable<PDF_CompetitionResult> ParseCompetitions(Stream pdfStream, string language)
        {
            bool isHE = language.Equals("HE", StringComparison.OrdinalIgnoreCase);
            var headerRx = isHE ? headerRxHE : headerRxEN;

            using var doc = PdfDocument.Open(pdfStream);
            PDF_CompetitionResult? current = null;

            foreach (var page in doc.GetPages())
            {
                var words = page.GetWords();
                var lines = words
                    .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 2.0) * 2.0)
                    .OrderByDescending(g => g.Key)
                    .Select(g => string.Join(' ', g.OrderBy(w => w.BoundingBox.Left)
                        .Select(w => w.Text)))
                    .ToList();

                for (int i = 0; i < lines.Count; i++)
                {
                    var raw = lines[i].Trim();
                    var line = isHE ? NormalizeHebrewLine(raw) : raw;

                    // Заголовок события
                    var m = headerRx.Match(line);
                    if (m.Success)
                    {
                        // Пропустить эстафеты
                        var styleVal = m.Groups["style"].Value;
                        if ((!isHE && styleVal.Contains("Relay", StringComparison.OrdinalIgnoreCase)) ||
                            (isHE && styleVal.Contains("שליח", StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        // Завершить предыдущий конкурс
                        if (current != null)
                        {
                            yield return current;
                        }

                        // Дата — до пробела
                        var next = i + 1 < lines.Count ? lines[i + 1].Trim() : string.Empty;
                        var date = next.Split(' ')[0];

                        // Убираем букву 'm' у длины
                        var rawLen = m.Groups["len"].Value;
                        var len = rawLen.EndsWith("m", StringComparison.OrdinalIgnoreCase)
                            ? rawLen.Substring(0, rawLen.Length - 1)
                            : rawLen;

                        string genderNorm;
                        if (!isHE)
                        {
                            var g = m.Groups["gender"].Value.ToLower();
                            genderNorm = g switch
                            {
                                "girls" => "female",
                                "boys" => "male",
                                "female" => "female",
                                "male" => "male",
                                _ => g
                            };
                        }
                        else
                        {
                            genderNorm = m.Groups["gender"].Value == "בנות" ? "female" : "male";
                        }

                        current = new PDF_CompetitionResult(
                            Competition: isHE ? NormalizeHebrewLine(lines[0].Trim()) : lines[0].Trim(),
                            AgeGroup: "9-11",
                            Date: date,
                            Event: line,
                            EventStyleName: isHE
                                ? styleMapHE.GetValueOrDefault(m.Groups["style"].Value, m.Groups["style"].Value)
                                : m.Groups["style"].Value,
                            EventStyleLen: len,
                            EventStyleGender: genderNorm,
                            EventStyleAge: m.Groups["age"].Value,
                            PoolType: "25m",
                            Results: new List<PDF_Result>()
                        );
                        continue;
                    }

                    // Результаты
                    if (current != null && Regex.IsMatch(line, @"^\d+\s+\d+\s+\d+"))
                    {
                        var entry = line;
                        if (!fullResultRx.IsMatch(entry) && i + 1 < lines.Count)
                        {
                            var nxtRaw = lines[i + 1].Trim();
                            var nxtLine = isHE ? NormalizeHebrewLine(nxtRaw) : nxtRaw;
                            entry += " " + nxtLine;
                            i++;
                        }

                        try
                        {
                            var res = ParseResultLine(entry);
                            current.Results.Add(res);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException(
                                $"Parse error on page {page.Number}, line '{entry}': {ex.Message}", ex);
                        }
                    }
                }

                // В конце страницы закрыть конкурс
                if (current != null)
                {
                    yield return current;
                    current = null;
                }
            }
        }

        public static IEnumerable<Result> Parse(
            Stream? englishPdfStream, string? englishFileName,
            Stream hebrewPdfStream, string hebrewFileName)
        {
            // Если нет английской версии
            if (englishPdfStream == null || string.IsNullOrWhiteSpace(englishFileName))
            {
                var heParts = Path.GetFileNameWithoutExtension(hebrewFileName)
                                  .Split('_', StringSplitOptions.RemoveEmptyEntries);
                var country = heParts.Length >= 2 ? heParts[^2] : string.Empty;
                var langHe = heParts.Length >= 1 ? heParts[^1] : string.Empty;

                foreach (var comp in ParseCompetitions(hebrewPdfStream, langHe))
                {
                    foreach (var rHe in comp.Results)
                    {
                        yield return new Result(
                            Country: country,
                            Competition: comp.Competition,
                            AgeGroup: comp.AgeGroup,
                            Date: comp.Date,
                            Event: comp.Event,
                            EventStyleName: comp.EventStyleName,
                            EventStyleLen: comp.EventStyleLen,
                            EventStyleGender: comp.EventStyleGender,
                            EventStyleAge: comp.EventStyleAge,
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
                            Note: null
                        );
                    }
                }

                yield break;
            }

            // Синхронизация EN + HE
            var enParts = Path.GetFileNameWithoutExtension(englishFileName!)
                              .Split('_', StringSplitOptions.RemoveEmptyEntries);
            var countryEn = enParts.Length >= 2 ? enParts[^2] : string.Empty;
            var langEn = enParts.Length >= 1 ? enParts[^1] : string.Empty;

            var hePartsSync = Path.GetFileNameWithoutExtension(hebrewFileName)
                                  .Split('_', StringSplitOptions.RemoveEmptyEntries);
            var langHeSync = hePartsSync.Length >= 1 ? hePartsSync[^1] : string.Empty;

            var compsEn = ParseCompetitions(englishPdfStream!, langEn).ToList();
            var compsHeSync = ParseCompetitions(hebrewPdfStream, langHeSync).ToList();

            for (int i = 0; i < compsEn.Count; i++)
            {
                var compEn = compsEn[i];
                var compHe = i < compsHeSync.Count
                    ? compsHeSync[i]
                    : throw new InvalidOperationException($"No matching HE event for '{compEn.Event}'");

                for (int j = 0; j < compEn.Results.Count; j++)
                {
                    var rEn = compEn.Results[j];
                    var rHe = j < compHe.Results.Count
                        ? compsHeSync[i].Results[j]
                        : throw new InvalidOperationException(
                            $"No matching HE result for {compEn.Event} heat={rEn.Heat}, lane={rEn.Lane}");

                    if (!string.IsNullOrEmpty(rEn.Time) && !string.IsNullOrEmpty(rHe.Time) && rEn.Time != rHe.Time)
                    {
                        throw new InvalidOperationException($"Time mismatch EN='{rEn.Time}', HE='{rHe.Time}'");
                    }

                    bool timeFail = string.IsNullOrEmpty(rEn.Time);
                    var lastNameHe = rHe.LastName;
                    var firstNameHe = rHe.FirstName;
                    var lastNameEn = !string.IsNullOrWhiteSpace(rEn.LastName) ? rEn.LastName : rHe.LastName;
                    var firstNameEn = !string.IsNullOrWhiteSpace(rEn.FirstName) ? rEn.FirstName : rHe.FirstName;
                    var clubEn = !string.IsNullOrWhiteSpace(rEn.Club) ? rEn.Club : rHe.Club;

                    yield return new Result(
                        Country: countryEn,
                        Competition: compHe.Competition,
                        AgeGroup: compEn.AgeGroup,
                        Date: compEn.Date,
                        Event: compEn.Event,
                        EventStyleName: compEn.EventStyleName,
                        EventStyleLen: compEn.EventStyleLen,
                        EventStyleGender: compEn.EventStyleGender,
                        EventStyleAge: compEn.EventStyleAge,
                        PoolType: compEn.PoolType,
                        Position: rEn.Position is int pi2 ? pi2 : null,
                        Heat: rEn.Heat,
                        Lane: rEn.Lane,
                        LastName: lastNameHe,
                        FirstName: firstNameHe,
                        LastNameEn: lastNameEn,
                        FirstNameEn: firstNameEn,
                        BirthYear: rEn.BirthYear,
                        Club: rHe.Club,
                        ClubEn: clubEn,
                        Time: rEn.Time ?? string.Empty,
                        TimeFail: timeFail,
                        InternationalPoints: rEn.InternationalPoints,
                        Note: null
                    );
                }
            }
        }

        private static string NormalizeHebrewLine(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            var tokens = input
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Array.Reverse(tokens);
            for (int i = 0; i < tokens.Length; i++)
            {
                var tok = tokens[i];
                if (Regex.IsMatch(tok, "[\\u0590-\\u05FF]"))
                {
                    tokens[i] = new string(tok.Reverse().ToArray());
                }
            }
            return string.Join(' ', tokens);
        }

        private static PDF_Result ParseResultLine(string line)
        {
            var tok = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            object? position = tok[0] == "-" ? null : int.Parse(tok[0]);
            int heat = int.Parse(tok[1]);
            int lane = int.Parse(tok[2]);
            int idxYear = Array.FindIndex(tok, 3, t => Regex.IsMatch(t, "^\\d{4}$"));
            int birth = int.Parse(tok[idxYear]);
            string timeTok = tok[^2];
            string? time = Regex.IsMatch(timeTok, @"^\d{2}:\d{2}\.\d{2}$") ? timeTok : null;
            int points = int.TryParse(tok[^1], out var p) ? p : 0;

            string firstEn = string.Empty;
            string lastEn = string.Empty;
            if (idxYear > 3)
            {
                firstEn = tok[idxYear - 1];
                lastEn = string.Join(' ', tok[3..(idxYear - 1)]);
            }

            string club = tok.Length > idxYear + 1
                ? string.Join(' ', tok[(idxYear + 1)..^2])
                : string.Empty;

            return new PDF_Result(
                Country: tok[0],
                Position: position,
                Heat: heat,
                Lane: lane,
                LastName: lastEn,
                FirstName: firstEn,
                BirthYear: birth,
                Club: club,
                Time: time,
                InternationalPoints: points
            );
        }
    }
}