using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Перенос тренировок «Дельфин-мастерс» из dolphin_masters_data.json в приватные
/// Sys_TrainingSessions / Sys_TrainingResults. Идентичность пловцов — из вычитанного вручную
/// словаря canon-resolved.csv. Соревнования НЕ трогаем (уже в БД). См. IDolphinTrainingSeeder.
/// </summary>
public class DolphinTrainingSeeder : IDolphinTrainingSeeder
{
    private readonly SwimmDbContext _db;

    public DolphinTrainingSeeder(SwimmDbContext db) => _db = db;

    // Одна запись словаря канонизации (строка src-варианта).
    private sealed record CanonEntry(
        string Person, string Kind, int? ExistingId,
        string FirstName, string LastName, string FirstNameEn, string LastNameEn,
        int BirthYear, string Gender);

    public async Task<IReadOnlyList<string>> SeedAsync(
        string jsonPath, string canonCsvPath, int hubGroupId, bool force = false)
    {
        var log = new List<string>();

        if (!File.Exists(jsonPath)) throw new InvalidOperationException($"JSON not found: {jsonPath}");
        if (!File.Exists(canonCsvPath)) throw new InvalidOperationException($"CSV not found: {canonCsvPath}");
        if (!await _db.HubGroups.AnyAsync(g => g.Id == hubGroupId))
            throw new InvalidOperationException($"HubGroup #{hubGroupId} не найдена — тренировкам нужна группа.");

        // ── словарь: srcKey -> CanonEntry (и группировка по canon_person) ─────────
        var canonBySrc = LoadCanon(canonCsvPath, out var byPerson);
        log.Add($"словарь: {canonBySrc.Count} src-вариантов → {byPerson.Count} человек");

        // ── стили и клуб Дельфина ─────────────────────────────────────────────────
        var styleByName = await _db.Styles.ToDictionaryAsync(s => s.Name, s => s.Id);

        // ── резолвим SwimmerId для каждого canon_person (create local при нужде) ───
        var swimmerIdByPerson = new Dictionary<string, int>();
        int created = 0, reused = 0;
        foreach (var (person, entry) in byPerson)
        {
            if (entry.Kind == "existing")
            {
                if (entry.ExistingId is null)
                    throw new InvalidOperationException($"{person}: kind=existing без existing_swimmer_id");
                if (!await _db.Swimmers.AnyAsync(s => s.Id == entry.ExistingId))
                    throw new InvalidOperationException($"{person}: Swimmer #{entry.ExistingId} не найден");
                swimmerIdByPerson[person] = entry.ExistingId.Value;
                continue;
            }

            // local: find-or-create по (имя, год, Origin='local')
            var existing = await _db.Swimmers.FirstOrDefaultAsync(s =>
                s.Origin == "local" && s.FirstName == entry.FirstName &&
                s.LastName == entry.LastName && s.BirthYear == entry.BirthYear);
            if (existing is not null)
            {
                swimmerIdByPerson[person] = existing.Id;
                reused++;
                continue;
            }

            var sw = new Swimmer
            {
                FirstName = entry.FirstName,
                LastName = entry.LastName,
                FirstNameEn = entry.FirstNameEn,
                LastNameEn = entry.LastNameEn,
                BirthYear = entry.BirthYear,
                Gender = entry.Gender == "female" ? "F" : "M",
                Origin = "local",
                ClubId = await ResolveDolphinClubIdAsync(),
            };
            _db.Swimmers.Add(sw);
            await _db.SaveChangesAsync();
            swimmerIdByPerson[person] = sw.Id;
            created++;
        }
        log.Add($"local-пловцы: создано {created}, переиспользовано {reused}, existing {byPerson.Values.Count(e => e.Kind == "existing")}");

        // ── ростер группы (HubGroupMembers) — иначе вкладка Competitions пуста ─────
        var existingMemberIds = new HashSet<int>(await _db.HubGroupMembers
            .Where(m => m.HubGroupId == hubGroupId).Select(m => m.SwimmerId).ToListAsync());
        var maxSort = await _db.HubGroupMembers.Where(m => m.HubGroupId == hubGroupId)
            .Select(m => (int?)m.SortOrder).MaxAsync() ?? 0;
        int rosterAdded = 0;
        foreach (var swimmerId in swimmerIdByPerson.Values.Distinct())
        {
            if (!existingMemberIds.Add(swimmerId)) continue;
            _db.HubGroupMembers.Add(new HubGroupMember
            {
                HubGroupId = hubGroupId,
                SwimmerId = swimmerId,
                Role = "member",
                SortOrder = ++maxSort,
            });
            rosterAdded++;
        }
        if (rosterAdded > 0) await _db.SaveChangesAsync();
        log.Add($"ростер группы: добавлено {rosterAdded}, уже было {existingMemberIds.Count - rosterAdded}");

        // ── --force: снести ранее засиженные тренировки этой группы ────────────────
        if (force)
        {
            var sessIds = await _db.TrainingSessions.Where(s => s.HubGroupId == hubGroupId)
                .Select(s => s.Id).ToListAsync();
            if (sessIds.Count > 0)
            {
                var delR = await _db.TrainingResults.Where(r => sessIds.Contains(r.SessionId)).ExecuteDeleteAsync();
                var delS = await _db.TrainingSessions.Where(s => s.HubGroupId == hubGroupId).ExecuteDeleteAsync();
                log.Add($"--force: удалено {delR} TrainingResults, {delS} TrainingSessions группы {hubGroupId}");
            }
        }

        // ── читаем JSON, только тренировки ────────────────────────────────────────
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var trainingRecords = doc.RootElement.EnumerateArray()
            .Where(r => r.TryGetProperty("training", out var t) && t.ValueKind == JsonValueKind.Object)
            .ToList();

        // существующие натуральные ключи (идемпотентность повторного прогона)
        var existingKeys = new HashSet<string>(
            await _db.TrainingResults
                .Where(r => r.Session!.HubGroupId == hubGroupId)
                .Select(r => r.Session!.ExternalTrainingId + "|" + r.SwimmerId + "|" + r.StyleId + "|" +
                             r.Distance + "|" + r.SetNo + "|" + r.OrderNo)
                .ToListAsync());

        var sessionCache = await _db.TrainingSessions
            .Where(s => s.HubGroupId == hubGroupId)
            .ToDictionaryAsync(s => s.ExternalTrainingId, s => s);

        int inserted = 0, skippedDup = 0, unmapped = 0;
        var unmappedSamples = new List<string>();

        foreach (var r in trainingRecords)
        {
            var t = r.GetProperty("training");
            var extId = Str(t, "trainingId");
            if (extId.Length == 0) continue;

            var srcKey = SrcKey(Str(r, "first_name"), Str(r, "last_name"), Str(r, "birth_year"));
            if (!canonBySrc.TryGetValue(srcKey, out var canon) ||
                !swimmerIdByPerson.TryGetValue(canon.Person, out var swimmerId))
            {
                unmapped++;
                if (unmappedSamples.Count < 8) unmappedSamples.Add(srcKey);
                continue;
            }

            // session find-or-create
            if (!sessionCache.TryGetValue(extId, out var session))
            {
                session = new TrainingSession
                {
                    HubGroupId = hubGroupId,
                    ExternalTrainingId = extId,
                    Name = StrOrNull(t, "trainingName"),
                    Date = ParseDate(Str(r, "date")),
                    PoolType = NormalizePool(Str(r, "pool_type")),
                };
                _db.TrainingSessions.Add(session);
                await _db.SaveChangesAsync();
                sessionCache[extId] = session;
            }
            else if (session.Name is null)
            {
                var nm = StrOrNull(t, "trainingName");
                if (nm is not null) { session.Name = nm; await _db.SaveChangesAsync(); }
            }

            var styleName = Str(r, "event_style_name");
            if (!styleByName.TryGetValue(styleName, out var styleId))
            {
                unmapped++;
                if (unmappedSamples.Count < 8) unmappedSamples.Add($"style:{styleName}");
                continue;
            }

            var distance = Str(r, "event_style_len");
            var setNo = Int(t, "set") ?? 0;
            var orderNo = Int(t, "order") ?? 0;

            var natKey = $"{extId}|{swimmerId}|{styleId}|{distance}|{setNo}|{orderNo}";
            if (!existingKeys.Add(natKey)) { skippedDup++; continue; }

            _db.TrainingResults.Add(new TrainingResult
            {
                SessionId = session.Id,
                SwimmerId = swimmerId,
                StyleId = styleId,
                Distance = distance,
                Gender = Str(r, "event_style_gender"),
                TimeMillisecond = ParseTimeMs(Str(r, "time")),
                TimeOriginal = Str(r, "time"),
                SetNo = setNo,
                OrderNo = orderNo,
                IntervalSec = Int(t, "interval"),
                Intensity = StrOrNull(t, "intensity"),
                IsPaddles = Bool(t, "isPaddles"),
                IsBuoy = Bool(t, "isBuoy"),
                ExpectedTimeMs = ParseTimeMs(Str(t, "expected_time")),
            });
            inserted++;
        }

        await _db.SaveChangesAsync();

        log.Add($"тренировок в JSON: {trainingRecords.Count}");
        log.Add($"сессий в группе: {sessionCache.Count}");
        log.Add($"ВСТАВЛЕНО TrainingResults: {inserted}; пропущено дублей: {skippedDup}; не сопоставлено: {unmapped}");
        if (unmappedSamples.Count > 0)
            log.Add($"  примеры несопоставленных ключей: {string.Join(" ; ", unmappedSamples)}");
        return log;
    }

    // ── словарь канонизации ───────────────────────────────────────────────────────

    private static Dictionary<string, CanonEntry> LoadCanon(
        string path, out Dictionary<string, CanonEntry> byPerson)
    {
        var bySrc = new Dictionary<string, CanonEntry>();
        byPerson = new Dictionary<string, CanonEntry>();

        var lines = File.ReadAllLines(path);
        for (int i = 1; i < lines.Length; i++) // skip header
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            // note (последняя колонка) может содержать запятые — берём первые 13 полей.
            var c = line.Split(',');
            if (c.Length < 13) continue;

            int? existingId = int.TryParse(c[2], out var eid) ? eid : null;
            int birthYear = int.TryParse(c[7], out var by) ? by : 0;

            var entry = new CanonEntry(
                Person: c[0], Kind: c[1], ExistingId: existingId,
                FirstName: c[3], LastName: c[4], FirstNameEn: c[5], LastNameEn: c[6],
                BirthYear: birthYear, Gender: c[8]);

            var srcKey = SrcKey(c[9], c[10], c[11]);
            bySrc[srcKey] = entry;
            byPerson[entry.Person] = entry; // все src-строки одного person несут одинаковую canon-часть
        }
        return bySrc;
    }

    private async Task<int?> ResolveDolphinClubIdAsync()
    {
        var club = await _db.Clubs
            .Where(c => c.Name == "הפועל דולפין נתניה")
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync();
        return club?.Id;
    }

    // ── парсеры/нормализация ──────────────────────────────────────────────────────

    private static string SrcKey(string first, string last, string birthYearRaw)
        => $"{first.Trim()}|{last.Trim()}|{NormYear(birthYearRaw)}";

    private static string NormYear(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "";
        var m = System.Text.RegularExpressions.Regex.Match(v, @"\d{4}");
        return m.Success ? m.Value : "";
    }

    /// <summary>«3:42» / «1:15.6» / «16.6» / «» → мс (null если пусто/битое).</summary>
    private static int? ParseTimeMs(string? s)
    {
        s = s?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        double totalSec;
        if (s.Contains(':'))
        {
            var parts = s.Split(':');
            if (parts.Length != 2) return null;
            if (!int.TryParse(parts[0], out var min)) return null;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var sec)) return null;
            totalSec = min * 60 + sec;
        }
        else if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out totalSec))
        {
            return null;
        }
        return (int)Math.Round(totalSec * 1000);
    }

    private static DateTime ParseDate(string s)
    {
        var d = DateTime.ParseExact(s.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture);
        return DateTime.SpecifyKind(d, DateTimeKind.Utc);
    }

    private static string NormalizePool(string s)
    {
        s = s.Trim();
        return s.EndsWith("m", StringComparison.OrdinalIgnoreCase) ? s : s + "m";
    }

    // ── JSON-хелперы (терпимы к числам-как-строкам) ───────────────────────────────

    private static string Str(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.ToString(),
            _ => "",
        };
    }

    private static string? StrOrNull(JsonElement e, string prop)
    {
        var s = Str(e, prop);
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static int? Int(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
        return null;
    }

    private static bool Bool(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var v)) return false;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => v.GetString()?.Trim().ToLowerInvariant() is "true" or "1",
            JsonValueKind.Number => v.TryGetInt32(out var n) && n != 0,
            _ => false,
        };
    }
}
