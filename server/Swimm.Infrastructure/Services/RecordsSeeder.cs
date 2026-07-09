using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Перенос легаси JS-данных клиента в Records / NormativeStandards.
/// Каждый window.*-файл — это JSON после отрезания комментариев и префикса присваивания.
/// Сидер печатает счётчики «распарсено = вставлено» по файлам — сверка обязательна.
/// </summary>
public class RecordsSeeder : IRecordsSeeder
{
    private readonly SwimmDbContext _db;

    // Территория текущих легаси-данных: возрастные/мастерс-рекорды и ISR-ветка
    // normative-records.js — израильские; нормативы — российская система разрядов.
    private const string LegacyCountry = "ISR";
    private const string StandardsCountry = "RUS";

    public RecordsSeeder(SwimmDbContext db) => _db = db;

    public async Task<IReadOnlyList<string>> SeedAsync(string dataDirectory, bool force = false)
    {
        var log = new List<string>();

        if (!Directory.Exists(dataDirectory))
            throw new InvalidOperationException($"Data directory not found: {dataDirectory}");

        var hasData = await _db.Records.AnyAsync() || await _db.NormativeStandards.AnyAsync();
        if (hasData && !force)
            throw new InvalidOperationException(
                "Records/NormativeStandards уже содержат данные. Повторный сид сотрёт правки админа — " +
                "запусти с --force, если это осознанно.");

        if (hasData)
        {
            var removedR = await _db.Records.ExecuteDeleteAsync();
            var removedN = await _db.NormativeStandards.ExecuteDeleteAsync();
            log.Add($"--force: удалено {removedR} Records, {removedN} NormativeStandards");
        }

        var records = new List<Record>();
        var standards = new List<NormativeStandard>();

        records.AddRange(ParseOpenRecords(LoadJson(dataDirectory, "normative-records.js"), log));
        records.AddRange(ParseAgeRecords(LoadJson(dataDirectory, "normative-age-records.js"), log));
        records.AddRange(ParseMastersRecords(LoadJson(dataDirectory, "normative-masters-records.js"), log));
        standards.AddRange(ParseStandards(LoadJson(dataDirectory, "normative.js"), kind: "regular", log));
        standards.AddRange(ParseStandards(LoadJson(dataDirectory, "normative-masters.js"), kind: "masters", log));

        _db.Records.AddRange(records);
        _db.NormativeStandards.AddRange(standards);
        await _db.SaveChangesAsync();

        log.Add($"ИТОГО вставлено: {records.Count} Records, {standards.Count} NormativeStandards");
        return log;
    }

    // ── Загрузка: window.X = {...}; → JsonDocument ───────────────────────────

    private static JsonElement LoadJson(string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path))
            throw new InvalidOperationException($"File not found: {path}");

        var text = File.ReadAllText(path);
        var eq = text.IndexOf('=');
        if (eq < 0)
            throw new InvalidOperationException($"{fileName}: не найден 'window.X =' префикс");

        var json = text[(eq + 1)..].Trim().TrimEnd(';');
        var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        return doc.RootElement.GetProperty("normatives");
    }

    /// <summary>"25m_pool" → "25m" (формат PoolType, как у Competitions).</summary>
    private static string Pool(string poolKey) => poolKey.Replace("_pool", "");

    private static string? Str(JsonElement obj, string prop)
        => obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    // ── normative-records.js: дистанция → { ISR, WR } ────────────────────────

    private static List<Record> ParseOpenRecords(JsonElement root, List<string> log)
    {
        var list = new List<Record>();
        foreach (var (gender, pool, style, distance, leaf) in Leaves(root))
            foreach (var scope in leaf.EnumerateObject())
            {
                var (regionType, regionCode) = scope.Name.ToUpperInvariant() switch
                {
                    "WR" => ("world", ""),
                    "ISR" => ("country", LegacyCountry),
                    // Будущие ключи в этом файле (EU и т.п.) сидер должен встретить осознанно.
                    _ => throw new InvalidOperationException($"normative-records.js: неизвестный scope '{scope.Name}'")
                };
                list.Add(new Record
                {
                    RegionType = regionType,
                    RegionCode = regionCode,
                    Category = "open",
                    AgeKey = "",
                    Gender = gender,
                    PoolType = pool,
                    Style = style,
                    Distance = distance,
                    Time = Str(scope.Value, "time") ?? "",
                    HolderName = Str(scope.Value, "name"),
                    HolderCountry = Str(scope.Value, "country"),
                    RecordDate = Str(scope.Value, "record_date")
                });
            }
        log.Add($"normative-records.js → {list.Count} open-рекордов (WR+ISR)");
        return list;
    }

    // ── normative-age-records.js: дистанция → возраст → рекорд ───────────────

    private static List<Record> ParseAgeRecords(JsonElement root, List<string> log)
    {
        var list = new List<Record>();
        foreach (var (gender, pool, style, distance, leaf) in Leaves(root))
            foreach (var age in leaf.EnumerateObject())
                list.Add(new Record
                {
                    RegionType = "country",
                    RegionCode = LegacyCountry,
                    Category = "age",
                    AgeKey = age.Name,
                    Gender = gender,
                    PoolType = pool,
                    Style = style,
                    Distance = distance,
                    Time = Str(age.Value, "time") ?? "",
                    HolderName = Str(age.Value, "name"),
                    Club = Str(age.Value, "club"),
                    HolderCountry = Str(age.Value, "country") ?? LegacyCountry,
                    RecordDate = Str(age.Value, "record_date")
                });
        log.Add($"normative-age-records.js → {list.Count} age-рекордов (ISR)");
        return list;
    }

    // ── normative-masters-records.js: дистанция → возр. группа → рекорд ──────

    private static List<Record> ParseMastersRecords(JsonElement root, List<string> log)
    {
        var list = new List<Record>();
        foreach (var (gender, pool, style, distance, leaf) in Leaves(root))
            foreach (var group in leaf.EnumerateObject())
                list.Add(new Record
                {
                    RegionType = "country",
                    RegionCode = LegacyCountry,
                    Category = "masters",
                    AgeKey = group.Name,
                    Gender = gender,
                    PoolType = pool,
                    Style = style,
                    Distance = distance,
                    Time = Str(group.Value, "time") ?? "",
                    HolderName = Str(group.Value, "name"),
                    Club = Str(group.Value, "club"),
                    HolderCountry = LegacyCountry,
                    RecordDate = Str(group.Value, "record_date")
                });
        log.Add($"normative-masters-records.js → {list.Count} masters-рекордов (ISR)");
        return list;
    }

    // ── normative.js / normative-masters.js: уровни-нормативы ────────────────

    private static List<NormativeStandard> ParseStandards(JsonElement root, string kind, List<string> log)
    {
        var list = new List<NormativeStandard>();
        foreach (var (gender, pool, style, distance, leaf) in Leaves(root))
            foreach (var entry in leaf.EnumerateObject())
            {
                if (kind == "regular")
                {
                    // leaf = { III_youth: "0:59.05", ... }
                    list.Add(Standard(kind, gender, pool, style, distance, ageKey: "", entry.Name, entry.Value.GetString() ?? ""));
                }
                else
                {
                    // leaf = { "25-29": { MSMK: "...", ... }, ... }
                    foreach (var level in entry.Value.EnumerateObject())
                        list.Add(Standard(kind, gender, pool, style, distance, ageKey: entry.Name, level.Name, level.Value.GetString() ?? ""));
                }
            }
        log.Add($"normative{(kind == "masters" ? "-masters" : "")}.js → {list.Count} нормативов ({kind})");
        return list;
    }

    private static NormativeStandard Standard(
        string kind, string gender, string pool, string style, string distance,
        string ageKey, string level, string time) => new()
    {
        Kind = kind,
        Country = StandardsCountry,
        Gender = gender,
        PoolType = pool,
        Style = style,
        Distance = distance,
        AgeKey = ageKey,
        Level = level,
        Time = time
    };

    // ── Обход общих осей: gender → pool → style → distance → leaf ────────────

    private static IEnumerable<(string Gender, string Pool, string Style, string Distance, JsonElement Leaf)>
        Leaves(JsonElement normatives)
    {
        foreach (var gender in normatives.EnumerateObject())
            foreach (var pool in gender.Value.EnumerateObject())
                foreach (var style in pool.Value.EnumerateObject())
                    foreach (var distance in style.Value.EnumerateObject())
                        yield return (gender.Name, Pool(pool.Name), style.Name, distance.Name, distance.Value);
    }
}
