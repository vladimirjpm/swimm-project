using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Импорт результатов из JSON (формат CommonModels / ResultWrap).
/// Заполняет справочники (Competition, Club, Swimmer, Style, Relay, Gallery) и таблицу Results.
/// </summary>
public class JsonImportService : IImportService
{
    private readonly SwimmDbContext _db;

    private static readonly string[] ClearableTables =
        ["Results", "GalleryItems", "Galleries", "Relays", "Swimmers", "Clubs", "Sys_ImportHistory", "Competitions", "Sys_ImportHistory"];

    public JsonImportService(SwimmDbContext db)
    {
        _db = db;
    }

    public string[] GetClearableTables() => ClearableTables;

    /// <summary>
    /// Импортирует JSON-файл и возвращает статистику.
    /// Поддерживает два формата: ResultWrap { results: [...] } и простой массив Result[].
    /// </summary>
    public async Task<ImportResult> ImportAsync(Stream jsonStream, string? fileName = null)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        options.Converters.Add(new LenientBoolConverter());
        options.Converters.Add(new LenientNullableBoolConverter());

        // Читаем весь поток в память для возможности двойной десериализации
        using var ms = new MemoryStream();
        await jsonStream.CopyToAsync(ms);
        ms.Position = 0;

        List<ResultJsonItem> items;
        bool isMasters = false, isAward = false;
        var diagnosticLog = new List<string>();

        // Пробуем ResultWrap { results: [...] }, затем простой массив
        try
        {
            var wrap = await JsonSerializer.DeserializeAsync<ResultWrap>(ms, options);
            if (wrap?.Results is { Count: > 0 })
            {
                items = wrap.Results;
                isMasters = wrap.IsMasters ?? false;
                isAward = wrap.IsAward ?? false;
                diagnosticLog.Add("Format: ResultWrap");
            }
            else
            {
                ms.Position = 0;
                items = await JsonSerializer.DeserializeAsync<List<ResultJsonItem>>(ms, options) ?? [];
                diagnosticLog.Add("Format: Array");
            }
        }
        catch (JsonException ex1)
        {
            diagnosticLog.Add($"ResultWrap attempt failed: {ex1.Message}");
            ms.Position = 0;
            try
            {
                items = await JsonSerializer.DeserializeAsync<List<ResultJsonItem>>(ms, options) ?? [];
                diagnosticLog.Add("Format: Array (fallback)");
            }
            catch (JsonException ex2)
            {
                diagnosticLog.Add($"Array attempt failed: {ex2.Message}");
                ms.Position = 0;
                diagnosticLog.AddRange(await DiagnoseJsonFieldsAsync(ms));
                return new ImportResult
                {
                    Message = "JSON deserialization failed",
                    ErrorMessages = [$"{ex2.Message}"],
                    DiagnosticLog = diagnosticLog
                };
            }
        }

        diagnosticLog.Add($"Items: {items.Count}, IsMasters(wrap): {isMasters}, IsAward(wrap): {isAward}");

        if (items.Count == 0)
            return new ImportResult { Message = "JSON contains no results", DiagnosticLog = diagnosticLog };

        // Кеши справочников для предотвращения повторных запросов
        var competitionCache = new Dictionary<string, Competition>();
        var clubCache = new Dictionary<string, Club>();
        var swimmerCache = new Dictionary<string, Swimmer>();
        var countryCache = new Dictionary<string, Country>();
        var styleCache = await _db.Styles.ToDictionaryAsync(s => s.Name, s => s);

        int created = 0, skipped = 0, errors = 0;
        var errorMessages = new List<string>();

        foreach (var (item, idx) in items.Select((r, i) => (r, i)))
        {
            try
            {
                // 1. Style
                var styleName = NormalizeStyleName(item.EventStyleName);
                if (!styleCache.TryGetValue(styleName, out var style))
                {
                    style = new Style { Name = styleName };
                    _db.Styles.Add(style);
                    await _db.SaveChangesAsync();
                    styleCache[styleName] = style;
                }

                // 2. Country
                Country? country = null;
                var countryCode = (item.Country ?? "").Trim().ToUpperInvariant();
                if (!string.IsNullOrEmpty(countryCode))
                {
                    if (!countryCache.TryGetValue(countryCode, out country))
                    {
                        country = await _db.Countries.FirstOrDefaultAsync(c => c.CountryCode == countryCode);
                        if (country == null)
                        {
                            country = new Country { CountryCode = countryCode, CountryName = countryCode };
                            _db.Countries.Add(country);
                            await _db.SaveChangesAsync();
                        }
                        countryCache[countryCode] = country;
                    }
                }

                // 3. Competition
                var compKey = $"{item.Competition}|{item.Date}|{item.PoolType}";
                if (!competitionCache.TryGetValue(compKey, out var competition))
                {
                    competition = await _db.Competitions.FirstOrDefaultAsync(c =>
                        c.Name == item.Competition && c.Date == item.Date && c.PoolType == NormalizePoolType(item.PoolType));

                    if (competition == null)
                    {
                        competition = new Competition
                        {
                            Name = item.Competition ?? string.Empty,
                            Country = item.Country ?? string.Empty,
                            Date = item.Date ?? string.Empty,
                            PoolType = NormalizePoolType(item.PoolType),
                            IsMasters = isMasters || (item.IsMasters ?? false),
                            IsAward = isAward || (item.IsAward ?? false)
                        };
                        _db.Competitions.Add(competition);
                        await _db.SaveChangesAsync();
                    }
                    competitionCache[compKey] = competition;
                }

                // 4. Club
                var clubName = item.Club ?? string.Empty;
                var clubNameEn = item.ClubEn ?? string.Empty;
                var clubKey = $"{clubName}|{clubNameEn}";
                if (!clubCache.TryGetValue(clubKey, out var club))
                {
                    club = await _db.Clubs.FirstOrDefaultAsync(c => c.Name == clubName && c.NameEn == clubNameEn);
                    if (club == null)
                    {
                        club = new Club { Name = clubName, NameEn = clubNameEn, CountryId = country?.Id };
                        _db.Clubs.Add(club);
                        await _db.SaveChangesAsync();
                    }
                    clubCache[clubKey] = club;
                }

                // 5. Swimmer
                var swimmerKey = $"{item.LastName}|{item.FirstName}|{item.BirthYear}";
                if (!swimmerCache.TryGetValue(swimmerKey, out var swimmer))
                {
                    swimmer = await _db.Swimmers.FirstOrDefaultAsync(s =>
                        s.LastName == (item.LastName ?? "") &&
                        s.FirstName == (item.FirstName ?? "") &&
                        s.BirthYear == item.BirthYear);

                    if (swimmer == null)
                    {
                        swimmer = new Swimmer
                        {
                            LastName = item.LastName ?? string.Empty,
                            FirstName = item.FirstName ?? string.Empty,
                            LastNameEn = item.LastNameEn ?? string.Empty,
                            FirstNameEn = item.FirstNameEn ?? string.Empty,
                            BirthYear = item.BirthYear
                        };
                        _db.Swimmers.Add(swimmer);
                        await _db.SaveChangesAsync();
                    }

                    // Дополнение данных спортсмена из результата (Gender, ClubId, CountryId)
                    EnrichSwimmerFromResult(swimmer, item.EventStyleGender, club, country);

                    swimmerCache[swimmerKey] = swimmer;
                }

                // 6. Relay
                Relay? relay = null;
                if (item.IsRelay == true)
                {
                    relay = new Relay
                    {
                        TeamName = item.RelayTeamName,
                        SwimmersName = item.RelaySwimmersName
                    };
                    _db.Relays.Add(relay);
                    await _db.SaveChangesAsync();
                }

                // 7. Gallery
                Gallery? gallery = null;
                if (item.Gallery != null && item.Gallery.Count > 0)
                {
                    gallery = new Gallery();
                    _db.Galleries.Add(gallery);
                    await _db.SaveChangesAsync();

                    foreach (var gi in item.Gallery)
                    {
                        _db.GalleryItems.Add(new GalleryItem
                        {
                            GalleryId = gallery.Id,
                            Type = gi.Type ?? "image",
                            SourceType = gi.SourceType,
                            Url = gi.Url,
                            Code = gi.Code
                        });
                    }
                    await _db.SaveChangesAsync();
                }

                // 8. ResultRecord
                var record = new ResultRecord
                {
                    CompetitionId = competition.Id,
                    SwimmerId = swimmer.Id,
                    ClubId = club.Id,
                    StyleId = style.Id,
                    RelayId = relay?.Id,
                    GalleryId = gallery?.Id,
                    CountryId = country?.Id,
                    CompetitionDate = ParseDate(item.Date),
                    Distance = item.EventStyleLen ?? string.Empty,
                    Gender = item.EventStyleGender ?? string.Empty,
                    AgeGroup = item.AgeGroup ?? string.Empty,
                    EventStyleAge = item.EventStyleAge ?? string.Empty,
                    Position = item.Position,
                    PositionAgeGroup = item.PositionAgeGroup,
                    Heat = item.Heat,
                    Lane = item.Lane,
                    TimeMillisecond = ParseTimeToMs(item.Time),
                    TimeOriginal = item.Time ?? string.Empty,
                    TimeSplit = item.TimeSplit ?? string.Empty,
                    TimeFail = item.TimeFail,
                    TimeFailNote = item.TimeFailNote,
                    InternationalPoints = item.InternationalPoints,
                    Note = item.Note
                };

                _db.Results.Add(record);
                await _db.SaveChangesAsync();
                created++;
            }
            catch (Exception ex)
            {
                errors++;
                if (errorMessages.Count < 20)
                {
                    var ctx = $"competition='{item.Competition}', swimmer='{item.LastName} {item.FirstName}', date='{item.Date}', style='{item.EventStyleName}'";
                    errorMessages.Add($"Row {idx} ({ctx}): {ex.Message}");
                    if (ex.InnerException != null)
                        errorMessages.Add($"  Inner: {ex.InnerException.Message}");
                }
            }
        }

        // Сохраняем обновлённые данные спортсменов (Gender, ClubId)
        if (_db.ChangeTracker.HasChanges())
            await _db.SaveChangesAsync();

        // Запись в историю импортов — одна запись на весь импорт файла
        if (created > 0 && competitionCache.Count > 0)
        {
            var importFileName = fileName ?? "unknown.json";
            var firstComp = competitionCache.Values.First();
            _db.ImportHistory.Add(new ImportHistory
            {
                CompetitionId = firstComp.Id,
                ImportFileName = importFileName,
                ImportDate = DateTime.UtcNow,
                Approved = false
            });
            await _db.SaveChangesAsync();
        }

        return new ImportResult
        {
            TotalRows = items.Count,
            Created = created,
            Skipped = skipped,
            Errors = errors,
            ErrorMessages = errorMessages,
            DiagnosticLog = diagnosticLog,
            Message = $"Import complete: {created} created, {skipped} skipped, {errors} errors"
        };
    }

    /// <summary>
    /// Дополняет данные спортсмена полями из результатов:
    /// Gender (из event_style_gender), ClubId (FK на клуб) и CountryId (FK на страну).
    /// Обновляет только пустые поля — не перезаписывает уже заполненные.
    /// </summary>
    private static void EnrichSwimmerFromResult(Swimmer swimmer, string? gender, Club? club, Country? country)
    {
        if (string.IsNullOrEmpty(swimmer.Gender) && !string.IsNullOrWhiteSpace(gender))
            swimmer.Gender = gender;

        if (swimmer.ClubId == null && club != null)
            swimmer.ClubId = club.Id;

        if (swimmer.CountryId == null && country != null)
            swimmer.CountryId = country.Id;
    }

    /// <summary>
    /// Пакетное дополнение спортсменов данными из таблицы Results.
    /// Заполняет Gender, ClubId и CountryId для записей, где эти поля ещё пусты,
    /// на основе самого свежего результата каждого спортсмена.
    /// </summary>
    public async Task<int> EnrichSwimmersFromResultsAsync()
    {
        var updated = 0;

        var swimmers = await _db.Swimmers
            .Where(s => s.Gender == null || s.ClubId == null || s.CountryId == null)
            .ToListAsync();

        if (swimmers.Count == 0)
            return 0;

        var swimmerIds = swimmers.Select(s => s.Id).ToHashSet();

        var latestResults = await _db.Results
            .Where(r => swimmerIds.Contains(r.SwimmerId))
            .GroupBy(r => r.SwimmerId)
            .Select(g => g.OrderByDescending(r => r.CompetitionDate).First())
            .Select(r => new { r.SwimmerId, r.Gender, r.ClubId, r.CountryId })
            .ToListAsync();

        var resultMap = latestResults.ToDictionary(r => r.SwimmerId);

        foreach (var swimmer in swimmers)
        {
            if (!resultMap.TryGetValue(swimmer.Id, out var latest))
                continue;

            var changed = false;

            if (string.IsNullOrEmpty(swimmer.Gender) && !string.IsNullOrWhiteSpace(latest.Gender))
            {
                swimmer.Gender = latest.Gender;
                changed = true;
            }

            if (swimmer.ClubId == null && latest.ClubId > 0)
            {
                swimmer.ClubId = latest.ClubId;
                changed = true;
            }

            if (swimmer.CountryId == null && latest.CountryId != null)
            {
                swimmer.CountryId = latest.CountryId;
                changed = true;
            }

            if (changed)
                updated++;
        }

        if (updated > 0)
            await _db.SaveChangesAsync();

        return updated;
    }

    /// <summary>
    /// Очищает все таблицы с импортированными данными.
    /// Сохраняет справочник Styles (seed-данные), AppUsers, AppRoles и пр.
    /// </summary>
    public async Task<ClearResult> ClearDataAsync()
    {
        var result = new ClearResult();

        // Порядок удаления — учитываем FK-зависимости: сначала дочерние таблицы
        result.Results = await _db.Results.ExecuteDeleteAsync();
        result.GalleryItems = await _db.GalleryItems.ExecuteDeleteAsync();
        result.Galleries = await _db.Galleries.ExecuteDeleteAsync();
        result.Relays = await _db.Relays.ExecuteDeleteAsync();
        result.Swimmers = await _db.Swimmers.ExecuteDeleteAsync();
        result.Clubs = await _db.Clubs.ExecuteDeleteAsync();
        result.ImportHistory = await _db.ImportHistory.ExecuteDeleteAsync();
        result.Competitions = await _db.Competitions.ExecuteDeleteAsync();
        result.Countries = await _db.Countries.ExecuteDeleteAsync();

        result.Total = result.Results + result.GalleryItems + result.Galleries
            + result.Relays + result.Swimmers + result.Clubs + result.ImportHistory + result.Competitions + result.Countries;

        // Сброс identity-счётчиков (только метаданные, на FK не влияет).
        //
        // TRUNCATE ... RESTART IDENTITY CASCADE здесь применять нельзя: Swimmers связана с
        // Sys_AppUsers (FK SwimmerId, ON DELETE SET NULL), CASCADE прошёл бы до AppUsers.
        var clearedTables = new[]
        {
            "Results", "GalleryItems", "Galleries", "Relays",
            "Swimmers", "Clubs", "Sys_ImportHistory", "Competitions", "Countries"
        };

        foreach (var table in clearedTables)
            await _db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE \"{table}\" ALTER COLUMN \"Id\" RESTART WITH 1;");

        return result;
    }

    private static async Task<List<string>> DiagnoseJsonFieldsAsync(Stream stream)
    {
        var log = new List<string>();
        try
        {
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                log.Add($"JSON root: Array, length={root.GetArrayLength()}");
                if (root.GetArrayLength() > 0)
                {
                    log.Add("Fields of first element:");
                    foreach (var prop in root[0].EnumerateObject())
                        log.Add($"  {prop.Name}: {prop.Value.ValueKind} = {Truncate(prop.Value.GetRawText(), 120)}");
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                log.Add("JSON root: Object");
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        log.Add($"  {prop.Name}: Array[{prop.Value.GetArrayLength()}]");
                        if (prop.Value.GetArrayLength() > 0)
                        {
                            log.Add($"  Fields of {prop.Name}[0]:");
                            foreach (var p in prop.Value[0].EnumerateObject())
                                log.Add($"    {p.Name}: {p.Value.ValueKind} = {Truncate(p.Value.GetRawText(), 120)}");
                        }
                    }
                    else
                    {
                        log.Add($"  {prop.Name}: {prop.Value.ValueKind} = {Truncate(prop.Value.GetRawText(), 120)}");
                    }
                }
            }
            else
            {
                log.Add($"JSON root: {root.ValueKind}");
            }
        }
        catch (Exception ex)
        {
            log.Add($"Diagnostic parse failed: {ex.Message}");
        }
        return log;
    }

    private static string Truncate(string s, int maxLen)
        => s.Length <= maxLen ? s : s[..maxLen] + "...";

    private static string NormalizePoolType(string? pt)
    {
        if (string.IsNullOrEmpty(pt)) return "";
        pt = pt.Trim().ToLowerInvariant().Replace("m", "");
        return pt switch
        {
            "25" => "25m",
            "50" => "50m",
            _ => pt + "m"
        };
    }

    private static string NormalizeStyleName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "unknown";
        return name.Trim().ToLowerInvariant().Replace(" ", "_");
    }

    private static DateTime ParseDate(string? date)
    {
        if (string.IsNullOrEmpty(date)) return DateTime.MinValue;

        string[] formats = [
            ParserConstants.DateFormat,
            "yyyy-MM-dd",
            "dd.MM.yyyy",
            "MM/dd/yyyy",
            "dd-MM-yyyy"
        ];

        if (DateTime.TryParseExact(date, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt;

        if (DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            return dt;

        return DateTime.MinValue;
    }

    /// <summary>
    /// Парсит строку времени вида "1:23.45" / "59.12" / "29.3" в миллисекунды.
    /// </summary>
    private static int? ParseTimeToMs(string? time)
    {
        if (string.IsNullOrWhiteSpace(time)) return null;

        time = time.Trim().Replace(',', '.');

        var match = Regex.Match(time, @"^(?:(\d+):)?(\d+)\.(\d+)$");
        if (!match.Success) return null;

        int minutes = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
        int seconds = int.Parse(match.Groups[2].Value);
        string frac = match.Groups[3].Value;

        frac = frac.Length switch
        {
            1 => frac + "00",
            2 => frac + "0",
            3 => frac,
            _ => frac[..3]
        };
        int ms = int.Parse(frac);

        return (minutes * 60 + seconds) * 1000 + ms;
    }

    private class LenientBoolConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.Null => false,
                JsonTokenType.Number => reader.TryGetInt64(out var n) ? n != 0 : reader.GetDouble() != 0,
                JsonTokenType.String => reader.GetString()?.Trim().ToLowerInvariant() is "true" or "1" or "yes",
                _ => throw new JsonException($"Cannot convert {reader.TokenType} to bool")
            };
        }

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
            => writer.WriteBooleanValue(value);
    }

    private class LenientNullableBoolConverter : JsonConverter<bool?>
    {
        public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.Null => null,
                JsonTokenType.Number => reader.TryGetInt64(out var n) ? n != 0 : reader.GetDouble() != 0,
                JsonTokenType.String => reader.GetString()?.Trim().ToLowerInvariant() is "true" or "1" or "yes",
                _ => throw new JsonException($"Cannot convert {reader.TokenType} to bool?")
            };
        }

        public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
        {
            if (value.HasValue) writer.WriteBooleanValue(value.Value);
            else writer.WriteNullValue();
        }
    }
}

/// <summary>
/// Константы формата дат, общие для парсера и импорта.
/// </summary>
public static class ParserConstants
{
    public const string DateFormat = "dd/MM/yyyy";
}

// ─── Internal JSON DTOs (десериализация входных данных) ───────────────────────

public class ResultWrap
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("results")]
    public List<ResultJsonItem>? Results { get; set; }

    [JsonPropertyName("is_masters")]
    public bool? IsMasters { get; set; }

    [JsonPropertyName("is_award")]
    public bool? IsAward { get; set; }
}

public class ResultJsonItem
{
    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("competition")]
    public string? Competition { get; set; }

    [JsonPropertyName("is_masters")]
    public bool? IsMasters { get; set; }

    [JsonPropertyName("is_award")]
    public bool? IsAward { get; set; }

    [JsonPropertyName("age_group")]
    public string? AgeGroup { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("event")]
    public string? Event { get; set; }

    [JsonPropertyName("event_style_name")]
    public string? EventStyleName { get; set; }

    [JsonPropertyName("event_style_len")]
    public string? EventStyleLen { get; set; }

    [JsonPropertyName("event_style_gender")]
    public string? EventStyleGender { get; set; }

    [JsonPropertyName("event_style_age")]
    public string? EventStyleAge { get; set; }

    [JsonPropertyName("pool_type")]
    public string? PoolType { get; set; }

    [JsonPropertyName("position")]
    public int? Position { get; set; }

    [JsonPropertyName("position_age_group")]
    public int? PositionAgeGroup { get; set; }

    [JsonPropertyName("heat")]
    public int Heat { get; set; }

    [JsonPropertyName("lane")]
    public int Lane { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name_en")]
    public string? LastNameEn { get; set; }

    [JsonPropertyName("first_name_en")]
    public string? FirstNameEn { get; set; }

    [JsonPropertyName("birth_year")]
    public int BirthYear { get; set; }

    [JsonPropertyName("club")]
    public string? Club { get; set; }

    [JsonPropertyName("club_en")]
    public string? ClubEn { get; set; }

    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("time_split")]
    public string? TimeSplit { get; set; }

    [JsonPropertyName("time_fail")]
    public bool TimeFail { get; set; }

    [JsonPropertyName("time_fail_note")]
    public string? TimeFailNote { get; set; }

    [JsonPropertyName("international_points")]
    public int InternationalPoints { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("is_relay")]
    public bool? IsRelay { get; set; }

    [JsonPropertyName("relay_team_name")]
    public string? RelayTeamName { get; set; }

    [JsonPropertyName("relay_swimmers_name")]
    public string? RelaySwimmersName { get; set; }

    [JsonPropertyName("relay_swimmers")]
    public List<RelaySwimmerJson>? RelaySwimmers { get; set; }

    [JsonPropertyName("gallery")]
    public List<GalleryItemJson>? Gallery { get; set; }
}

public class RelaySwimmerJson
{
    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("birth_year")]
    public int? BirthYear { get; set; }

    [JsonPropertyName("club")]
    public string? Club { get; set; }

    [JsonPropertyName("split_time")]
    public string? SplitTime { get; set; }
}

public class GalleryItemJson
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("sourceType")]
    public string? SourceType { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }
}
