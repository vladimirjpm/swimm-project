using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Swimm.Parsing;
using Swimm.Parsing.Models;

namespace Swimm.Parsing.Parsers.WorldRecords;

public class WorldRecordsParser : IFormatParser
{
    public string FormatName => "WorldRecords";

    private readonly List<string> _debugLog = new();

    // Маппинг названий стилей на нормализованные ключи
    private static readonly Dictionary<string, string> StyleMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["freestyle"] = "freestyle",
        ["backstroke"] = "backstroke",
        ["breaststroke"] = "breaststroke",
        ["butterfly"] = "butterfly",
        ["individual medley"] = "individual_medley",
        ["medley"] = "medley",
        ["im"] = "individual_medley",
    };

    // Regex для разбора колонки Event, например: "Women's 50m Freestyle" или "Mixed 4x100m Medley Relay"
    private static readonly Regex EventRx = new(
        @"^(?:(?<gender>Women|Men|Mixed)(?:'s)?)\s+(?<distance>\d+(?:\s*[xX]\s*\d+)?)\s*m\s+(?<style>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        Log($"Parsing primary file: {request.PrimaryFileName}");
        foreach (var r in ParseXlsx(request.PrimaryStream, request.PrimaryFileName))
            yield return r;

        if (request.SecondaryStream != null)
        {
            Log($"Parsing secondary file: {request.SecondaryFileName}");
            foreach (var r in ParseXlsx(request.SecondaryStream, request.SecondaryFileName ?? "secondary.xlsx"))
                yield return r;
        }

        if (request.ExtraStreams != null)
        {
            foreach (var (stream, fileName) in request.ExtraStreams)
            {
                Log($"Parsing extra file: {fileName}");
                foreach (var r in ParseXlsx(stream, fileName))
                    yield return r;
            }
        }
    }

    private IEnumerable<Result> ParseXlsx(Stream stream, string fileName)
    {
        Log($"Opening XLSX file: {fileName}");

        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        Log($"Sheet: '{sheet.Name}', rows used: {sheet.LastRowUsed()?.RowNumber() ?? 0}");

        // Определяем строку заголовков (ищем "Event" в первых 5 строках)
        int headerRow = FindHeaderRow(sheet);
        if (headerRow < 0)
        {
            Log("ERROR: Header row not found");
            yield break;
        }
        Log($"Header row: {headerRow}");

        // Маппинг заголовков к индексам колонок
        var columnMap = BuildColumnMap(sheet, headerRow);
        Log($"Column map: {string.Join(", ", columnMap.Select(kv => $"{kv.Key}={kv.Value}"))}");

        int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        int parsed = 0;
        int skipped = 0;

        for (int row = headerRow + 1; row <= lastRow; row++)
        {
            var wsRow = sheet.Row(row);

            var eventVal = GetCellString(wsRow, columnMap, "event");
            var poolVal = GetCellString(wsRow, columnMap, "pool");
            var recordVal = GetCellString(wsRow, columnMap, "record");
            var timeVal = GetCellString(wsRow, columnMap, "time");
            var athleteVal = GetCellString(wsRow, columnMap, "athlete");
            var nfCodeVal = GetCellString(wsRow, columnMap, "nfcode");
            var genderVal = GetCellString(wsRow, columnMap, "gender");
            var dateVal = GetCellDate(wsRow, columnMap, "date");
            var competitionVal = GetCellString(wsRow, columnMap, "competition");
            var countryVal = GetCellString(wsRow, columnMap, "country");
            var splitsVal = GetCellString(wsRow, columnMap, "splits");

            // Пропускаем пустые строки
            if (string.IsNullOrWhiteSpace(eventVal) && string.IsNullOrWhiteSpace(timeVal))
            {
                skipped++;
                continue;
            }

            // Парсим Event: "Women's 50m Freestyle"
            var (evGender, evDistance, evStyle, evIsRelay) = ParseEvent(eventVal);

            // Определяем пол: приоритет — колонка Gender, иначе из Event
            string gender = NormalizeGender(genderVal, evGender);

            // Нормализуем бассейн
            string poolType = NormalizePool(poolVal);

            // Нормализуем стиль
            string style = NormalizeStyle(evStyle);

            // Нормализуем дистанцию
            string distance = NormalizeDistance(evDistance, evIsRelay);

            // Нормализуем время
            string time = NormalizeTime(timeVal);

            // Страна (NF Code или Country)
            string country = !string.IsNullOrWhiteSpace(nfCodeVal) ? nfCodeVal.Trim().ToUpperInvariant() : countryVal.Trim();

            // Тип рекорда: WR остаётся, NR/CR и другие национальные → код страны (ISR, SWE, ...)
            string recordType = NormalizeRecordType(recordVal.Trim().ToUpperInvariant(), country);
            if (string.IsNullOrEmpty(recordType)) recordType = "WR";

            // Дата в формате DD/MM/YYYY
            string date = dateVal;

            // Имя спортсмена
            var (firstName, lastName) = ParseAthleteName(athleteVal);
            string fullName = $"{firstName} {lastName}".Trim();

            Log($"  Row {row}: event='{eventVal}' -> gender={gender}, pool={poolType}, " +
                $"style={style}, dist={distance}, record={recordType}, time={time}, " +
                $"athlete='{fullName}', country={country}, date={date}");

            if (string.IsNullOrEmpty(style) || string.IsNullOrEmpty(distance) || string.IsNullOrEmpty(time))
            {
                Log($"  Row {row}: SKIPPED (missing style/distance/time)");
                skipped++;
                continue;
            }

            parsed++;

            var eventStr = evIsRelay
                ? $"{distance} {style} relay - {gender}"
                : $"{distance} {style} - {gender}";

            yield return new Result(
                Country: country,
                Competition: competitionVal ?? "World Records",
                IsMasters: "false",
                IsAward: false,
                AgeGroup: "open",
                Date: date,
                Event: eventStr,
                EventStyleName: style,
                EventStyleLen: distance.TrimEnd('m'),
                EventStyleGender: gender,
                EventStyleAge: "0",
                PoolType: poolType.Replace("_pool", ""),
                Position: 1,
                Heat: 0,
                Lane: 0,
                LastName: lastName,
                FirstName: firstName,
                LastNameEn: lastName,
                FirstNameEn: firstName,
                BirthYear: 0,
                Club: "",
                ClubEn: "",
                Time: time,
                TimeFail: false,
                TimeFailNote: null,
                InternationalPoints: 0,
                Note: recordType,
                IsRelay: evIsRelay,
                RelayTeamName: evIsRelay ? country : null,
                RelaySwimmersName: null,
                RelaySwimmers: null
            );
        }

        Log($"Parse complete. Parsed: {parsed}, Skipped: {skipped}");
    }

    /// <summary>
    /// Поиск строки заголовков в первых 10 строках листа.
    /// Ищет ячейку с текстом "Event".
    /// </summary>
    private int FindHeaderRow(IXLWorksheet sheet)
    {
        int maxSearch = Math.Min(10, sheet.LastRowUsed()?.RowNumber() ?? 0);
        for (int row = 1; row <= maxSearch; row++)
        {
            for (int col = 1; col <= 20; col++)
            {
                var val = sheet.Cell(row, col).GetString().Trim();
                if (val.Equals("Event", StringComparison.OrdinalIgnoreCase))
                    return row;
            }
        }
        return -1;
    }

    /// <summary>
    /// Строит словарь: нормализованное имя заголовка -> номер колонки.
    /// </summary>
    private Dictionary<string, int> BuildColumnMap(IXLWorksheet sheet, int headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;

        for (int col = 1; col <= lastCol; col++)
        {
            var header = sheet.Cell(headerRow, col).GetString().Trim();
            if (string.IsNullOrEmpty(header)) continue;

            var key = NormalizeHeaderName(header);
            if (!string.IsNullOrEmpty(key) && !map.ContainsKey(key))
            {
                map[key] = col;
            }
        }

        return map;
    }

    /// <summary>
    /// Нормализация заголовка колонки к внутреннему ключу.
    /// </summary>
    private static string NormalizeHeaderName(string header)
    {
        return header.ToLowerInvariant() switch
        {
            "event" => "event",
            "pool" => "pool",
            "record description" => "recorddescription",
            "record" => "record",
            "time" => "time",
            "athlete" => "athlete",
            "nf code" => "nfcode",
            "gender" => "gender",
            "competition" => "competition",
            "country" => "country",
            "city" => "city",
            "date" => "date",
            "splits" => "splits",
            _ => header.ToLowerInvariant().Replace(" ", "")
        };
    }

    private static string GetCellString(IXLRow row, Dictionary<string, int> columnMap, string key)
    {
        if (!columnMap.TryGetValue(key, out int col)) return "";
        return row.Cell(col).GetString().Trim();
    }

    /// <summary>
    /// Читает дату из ячейки и форматирует как DD/MM/YYYY.
    /// Поддерживает DateTime-ячейки и строки формата "29-Jul-23", "dd/MM/yyyy" и т.д.
    /// </summary>
    private string GetCellDate(IXLRow row, Dictionary<string, int> columnMap, string key)
    {
        if (!columnMap.TryGetValue(key, out int col)) return "";

        var cell = row.Cell(col);

        // Если ячейка содержит дату
        if (cell.DataType == XLDataType.DateTime)
        {
            return cell.GetDateTime().ToString(ParserConstants.DateFormat);
        }

        var text = cell.GetString().Trim();
        if (string.IsNullOrEmpty(text)) return "";

        // Пробуем распарсить строковые форматы дат
        string[] formats = {
            "dd-MMM-yy", "dd-MMM-yyyy", "d-MMM-yy", "d-MMM-yyyy",
            "dd-MM-yyyy", "d-MM-yyyy",
            "dd/MM/yyyy", "d/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd",
            "dd.MM.yyyy", "d.MM.yyyy"
        };

        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt))
        {
            return dt.ToString(ParserConstants.DateFormat);
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt2))
        {
            return dt2.ToString(ParserConstants.DateFormat);
        }

        Log($"    WARNING: Could not parse date '{text}'");
        return text;
    }

    /// <summary>
    /// Разбирает колонку Event: "Women's 50m Freestyle" или "Mixed 4x100m Medley Relay".
    /// Возвращает (gender, distance, style, isRelay).
    /// </summary>
    private static (string gender, string distance, string style, bool isRelay) ParseEvent(string eventVal)
    {
        if (string.IsNullOrWhiteSpace(eventVal))
            return ("", "", "", false);

        var match = EventRx.Match(eventVal.Trim());
        if (!match.Success)
            return ("", "", eventVal, false);

        var genderPart = match.Groups["gender"].Value;
        var distPart = match.Groups["distance"].Value;
        var stylePart = match.Groups["style"].Value.Trim();

        string gender = genderPart.ToLowerInvariant() switch
        {
            "women" => "female",
            "men" => "male",
            "mixed" => "mix",
            _ => ""
        };

        bool isRelay = stylePart.EndsWith("relay", StringComparison.OrdinalIgnoreCase);
        if (isRelay)
        {
            stylePart = Regex.Replace(stylePart, @"\s*relay\s*$", "", RegexOptions.IgnoreCase).Trim();
        }

        // Нормализация дистанции: "4 x 100" -> "4X100", "50" -> "50"
        string distance = Regex.Replace(distPart, @"\s*[xX]\s*", "X");

        return (gender, distance, stylePart, isRelay);
    }

    /// <summary>
    /// Определяет пол: приоритет — явная колонка Gender, затем Event.
    /// </summary>
    private static string NormalizeGender(string genderCol, string eventGender)
    {
        if (!string.IsNullOrWhiteSpace(genderCol))
        {
            return genderCol.Trim().ToUpperInvariant() switch
            {
                "W" or "F" or "FEMALE" or "WOMEN" => "female",
                "M" or "MALE" or "MEN" => "male",
                "X" or "MIXED" or "MIX" => "mix",
                _ => eventGender
            };
        }
        return !string.IsNullOrEmpty(eventGender) ? eventGender : "male";
    }

    /// <summary>
    /// Нормализация бассейна: "LCM" -> "50m_pool", "SCM" -> "25m_pool".
    /// </summary>
    private static string NormalizePool(string poolVal)
    {
        return poolVal.Trim().ToUpperInvariant() switch
        {
            "LCM" or "50" or "50M" => "50m_pool",
            "SCM" or "25" or "25M" => "25m_pool",
            _ when poolVal.Contains("50") => "50m_pool",
            _ when poolVal.Contains("25") => "25m_pool",
            _ => "50m_pool"
        };
    }

    /// <summary>
    /// Нормализация типа рекорда: "WR" остаётся "WR",
    /// национальные рекорды ("NR", "CR" и пр.) заменяются на код страны спортсмена.
    /// </summary>
    private static string NormalizeRecordType(string recordType, string country)
    {
        if (string.IsNullOrEmpty(recordType)) return "";
        if (recordType == "WR") return "WR";
        // NR (National Record), CR (Championship Record) и другие → код страны
        if (!string.IsNullOrEmpty(country))
            return country;
        return recordType;
    }

    /// <summary>
    /// Нормализация стиля: "Freestyle" -> "freestyle", "Individual Medley" -> "individual_medley".
    /// </summary>
    private static string NormalizeStyle(string stylePart)
    {
        if (string.IsNullOrWhiteSpace(stylePart)) return "";

        var trimmed = stylePart.Trim();
        if (StyleMap.TryGetValue(trimmed, out var mapped))
            return mapped;

        // Пробуем частичное совпадение
        var lower = trimmed.ToLowerInvariant();
        foreach (var kvp in StyleMap)
        {
            if (lower.Contains(kvp.Key))
                return kvp.Value;
        }

        // Фоллбэк: lowercase + замена пробелов на _
        return lower.Replace(' ', '_');
    }

    /// <summary>
    /// Нормализация дистанции: "50" -> "50m", "4X100" -> "4X100m".
    /// </summary>
    private static string NormalizeDistance(string distRaw, bool isRelay)
    {
        if (string.IsNullOrWhiteSpace(distRaw)) return "";

        var clean = distRaw.Trim().TrimEnd('m', 'M');

        if (isRelay && clean.Contains('X'))
        {
            // "4X100" -> "4X100m"
            return clean + "m";
        }

        return clean + "m";
    }

    /// <summary>
    /// Нормализация времени: оставляет оригинальный формат.
    /// Если число без двоеточия — считаем секунды.
    /// </summary>
    private static string NormalizeTime(string timeVal)
    {
        if (string.IsNullOrWhiteSpace(timeVal)) return "";

        var trimmed = timeVal.Trim();

        // Уже в формате MM:SS.ss или SS.ss
        if (trimmed.Contains(':') || trimmed.Contains('.'))
            return trimmed;

        // Целое число секунд
        if (double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var seconds))
        {
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.Minutes > 0
                ? $"{ts.Minutes}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}"
                : $"{ts.Seconds}.{ts.Milliseconds / 10:D2}";
        }

        return trimmed;
    }

    /// <summary>
    /// Разбирает имя спортсмена: "SJOESTROEM Sarah" -> ("Sarah", "Sjoestroem").
    /// Первое слово в верхнем регистре считается фамилией.
    /// </summary>
    private static (string firstName, string lastName) ParseAthleteName(string athlete)
    {
        if (string.IsNullOrWhiteSpace(athlete))
            return ("", "");

        var parts = athlete.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return ("", "");
        if (parts.Length == 1) return ("", ToTitleCase(parts[0]));

        // Ищем границу: блок слов в верхнем регистре (фамилия), затем имя
        var lastNameParts = new List<string>();
        var firstNameParts = new List<string>();
        bool hitLower = false;

        foreach (var part in parts)
        {
            if (!hitLower && part == part.ToUpperInvariant() && part.Length > 1)
            {
                lastNameParts.Add(ToTitleCase(part));
            }
            else
            {
                hitLower = true;
                firstNameParts.Add(part);
            }
        }

        // Если все слова были в верхнем регистре — первое = фамилия, остальные = имя
        if (firstNameParts.Count == 0 && lastNameParts.Count > 1)
        {
            var first = lastNameParts[0];
            lastNameParts.RemoveAt(0);
            return (string.Join(' ', lastNameParts), first);
        }

        var firstName = string.Join(' ', firstNameParts);
        var lastName = string.Join(' ', lastNameParts);

        return (firstName, lastName);
    }

    private static string ToTitleCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.Length == 1) return s.ToUpperInvariant();
        return char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
    }

    /// <summary>
    /// Формирует нормативный словарь: gender -> pool -> style -> distance -> recordType -> entry.
    /// </summary>
    private static WorldRecordsNormativeOutput BuildNormativeOutput(List<Result> results)
    {
        var normatives = new Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, WorldRecordEntry>>>>>();

        foreach (var r in results)
        {
            var gender = r.EventStyleGender;
            var poolKey = r.PoolType switch
            {
                "25m" => "25m_pool",
                "50m" => "50m_pool",
                _ => r.PoolType + "_pool"
            };
            var style = r.EventStyleName;
            var distKey = r.EventStyleLen.EndsWith("m") ? r.EventStyleLen : r.EventStyleLen + "m";
            var recordType = r.Note ?? "WR";
            var name = $"{r.FirstName} {r.LastName}".Trim();

            if (!normatives.ContainsKey(gender))
                normatives[gender] = new();
            if (!normatives[gender].ContainsKey(poolKey))
                normatives[gender][poolKey] = new();
            if (!normatives[gender][poolKey].ContainsKey(style))
                normatives[gender][poolKey][style] = new();
            if (!normatives[gender][poolKey][style].ContainsKey(distKey))
                normatives[gender][poolKey][style][distKey] = new();

            normatives[gender][poolKey][style][distKey][recordType] = new WorldRecordEntry(
                Time: r.Time,
                Name: name,
                Country: r.Country,
                RecordDate: r.Date
            );
        }

        var lastUpdate = DateTime.Now.ToString(ParserConstants.DateFormat);
        return new WorldRecordsNormativeOutput(lastUpdate, normatives);
    }
}
