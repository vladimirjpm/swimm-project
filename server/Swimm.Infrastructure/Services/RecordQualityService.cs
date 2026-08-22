using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Сверка справочника рекордов с нашими протоколами + чтение реестра спорных записей
/// (docs/plans/records-quality-plan.md).
///
/// ⚠ Дважды и подчёркнуто: «заплыв не найден» — НЕ признак ошибки источника. Протоколы
/// загружены не за все годы, рекорд 1995 года сверять просто не с чем. Сервис отвечает на
/// вопрос «можем ли мы подтвердить», а не «правда ли это».
/// </summary>
public class RecordQualityService(SwimmDbContext db, ISettingsService? settings = null)
    : IRecordQualityService
{
    /// <summary>Форматы дат в источниках рекордов — смешанные, разбираем оба.</summary>
    private static readonly string[] RecordDateFormats = ["dd/MM/yyyy", "M/d/yyyy", "d/M/yyyy"];

    public async Task<RecordVerifyResult> VerifyAllAsync(CancellationToken ct = default)
    {
        var records = await db.Records.AsNoTracking()
            .Select(r => new
            {
                r.Id, r.Gender, r.PoolType, r.Style, r.Distance, r.Time, r.RecordDate,
                r.Category, r.AgeKey
            })
            .ToListAsync(ct);

        // Дистанция в Records с суффиксом «m» («100m», «4X50m»), в Results — без него.
        var parsed = records
            .Select(r => new
            {
                r.Id,
                Ms = SwimTime.ParseToMs(r.Time),
                Gender = NormalizeGender(r.Gender),
                r.PoolType,
                r.Style,
                Distance = TrimDistance(r.Distance),
                Date = ParseRecordDate(r.RecordDate),
                r.Category,
                r.AgeKey
            })
            .ToList();

        var msSet = parsed.Where(p => p.Ms != null).Select(p => p.Ms!.Value).Distinct().ToList();

        // Тянем только заплывы с ровно такими временами: выборка узкая, а альтернатива —
        // запрос на каждый рекорд (их ~2 тысячи).
        var candidates = msSet.Count == 0
            ? []
            : await db.Results.AsNoTracking()
                .Where(r => r.TimeMillisecond != null && msSet.Contains(r.TimeMillisecond.Value))
                .Select(r => new
                {
                    r.Id,
                    r.SwimmerId,
                    BirthYear = r.Swimmer.BirthYear,
                    Ms = r.TimeMillisecond!.Value,
                    r.Gender,
                    PoolType = r.Competition.PoolType,
                    Style = r.Style.Name,
                    r.Distance,
                    r.CompetitionDate
                })
                .ToListAsync(ct);

        var byKey = candidates
            .GroupBy(c => MatchKey(c.Ms, NormalizeGender(c.Gender), c.PoolType, c.Style, c.Distance))
            .ToDictionary(g => g.Key, g => g.ToList());

        var existing = await db.RecordVerifications.ToDictionaryAsync(v => v.RecordId, ct);
        var now = DateTime.UtcNow;
        int found = 0, notFound = 0, wrongDate = 0;
        int axisChecked = 0, axisBoth = 0, axisCalendar = 0, axisSeason = 0, axisNone = 0;

        foreach (var p in parsed)
        {
            var matches = p.Ms == null
                ? null
                : byKey.GetValueOrDefault(MatchKey(p.Ms.Value, p.Gender, p.PoolType, p.Style, p.Distance));

            var best = matches == null || matches.Count == 0
                ? null
                // Совпадение по дате важнее: тот же результат мог быть повторён другим пловцом.
                : matches.OrderByDescending(m => p.Date != null && m.CompetitionDate.Date == p.Date.Value.Date)
                    .ThenBy(m => m.CompetitionDate)
                    .First();

            if (!existing.TryGetValue(p.Id, out var row))
            {
                row = new RecordVerification { RecordId = p.Id };
                db.RecordVerifications.Add(row);
                existing[p.Id] = row;
            }

            row.Found = best != null;
            row.ResultId = best?.Id;
            row.SwimmerId = best?.SwimmerId;
            row.DateMatched = best == null || p.Date == null
                ? null
                : best.CompetitionDate.Date == p.Date.Value.Date;

            // Ось возраста: сходится ли ступень справочника с пловцом и по какой именно оси.
            // Считаем ВСЕГДА по обеим — поле отвечает на вопрос «какая ось у источника», а не
            // «совпало ли с нашей настройкой» (docs/data-integrity.md §13).
            //
            // ⚠ Только при совпавшей ДАТЕ. Совпадение одного лишь времени опознаёт пловца
            // ненадёжно: на реальной базе 147 из 167 «найденных» приходились на другой день,
            // то есть на другого человека, и его возраст про ступень не говорит ничего.
            row.AgeAxisMatch = best == null || row.DateMatched != true
                ? null
                : AgeAxisMatchOf(p.Category, p.AgeKey, p.Distance, best.BirthYear, best.CompetitionDate);

            row.CheckedAt = now;

            if (best == null) notFound++;
            else
            {
                found++;
                if (row.DateMatched == false) wrongDate++;

                switch (row.AgeAxisMatch)
                {
                    case AgeAxisMatches.Both:     axisChecked++; axisBoth++;     break;
                    case AgeAxisMatches.Calendar: axisChecked++; axisCalendar++; break;
                    case AgeAxisMatches.Season:   axisChecked++; axisSeason++;   break;
                    case AgeAxisMatches.None:     axisChecked++; axisNone++;     break;
                }
            }
        }

        await db.SaveChangesAsync(ct);

        return new RecordVerifyResult(
            parsed.Count, found, notFound, wrongDate,
            AgeAxisChecked: axisChecked,
            AgeAxisBoth: axisBoth,
            AgeAxisCalendarOnly: axisCalendar,
            AgeAxisSeasonOnly: axisSeason,
            AgeAxisNone: axisNone);
    }

    public async Task<RecordQualitySummary> GetSummaryAsync(int issuesLimit = 20, CancellationToken ct = default)
    {
        var total = await db.Records.AsNoTracking().CountAsync(ct);
        var found = await db.RecordVerifications.AsNoTracking().CountAsync(v => v.Found, ct);
        var notFound = await db.RecordVerifications.AsNoTracking().CountAsync(v => !v.Found, ct);
        var wrongDate = await db.RecordVerifications.AsNoTracking()
            .CountAsync(v => v.Found && v.DateMatched == false, ct);
        var lastCheckedAt = await db.RecordVerifications.AsNoTracking()
            .OrderByDescending(v => v.CheckedAt)
            .Select(v => (DateTime?)v.CheckedAt)
            .FirstOrDefaultAsync(ct);

        // Разбивка по осям: она же — ответ на вопрос «а как ступени раскладывает источник».
        var axisBoth = await db.RecordVerifications.AsNoTracking()
            .CountAsync(v => v.AgeAxisMatch == AgeAxisMatches.Both, ct);
        var axisCalendar = await db.RecordVerifications.AsNoTracking()
            .CountAsync(v => v.AgeAxisMatch == AgeAxisMatches.Calendar, ct);
        var axisSeason = await db.RecordVerifications.AsNoTracking()
            .CountAsync(v => v.AgeAxisMatch == AgeAxisMatches.Season, ct);
        var axisNone = await db.RecordVerifications.AsNoTracking()
            .CountAsync(v => v.AgeAxisMatch == AgeAxisMatches.None, ct);

        var axis = RecordAgeAxisSetting.From(settings);
        // «Не сходится с текущей осью» = не подтверждается ни обеими осями, ни выбранной.
        var axisMismatch = axis == RecordAgeAxis.Calendar
            ? axisSeason + axisNone
            : axisCalendar + axisNone;

        var issuesTotal = await db.RecordIssues.AsNoTracking().CountAsync(ct);
        var issuesOpen = await db.RecordIssues.AsNoTracking()
            .CountAsync(i => i.Status == RecordIssueStatuses.Open, ct);

        var issues = await db.RecordIssues.AsNoTracking()
            .Where(i => i.Status == RecordIssueStatuses.Open)
            .OrderByDescending(i => i.CreatedAt)
            .Take(issuesLimit)
            .ToListAsync(ct);

        var issueDtos = new List<RecordIssueDto>(issues.Count);
        foreach (var i in issues)
            issueDtos.Add(await ToDtoAsync(i, ct));

        return new RecordQualitySummary(
            Total: total,
            Found: found,
            NotFound: notFound,
            NotChecked: Math.Max(0, total - found - notFound),
            FoundWrongDate: wrongDate,
            LastCheckedAt: lastCheckedAt,
            IssuesOpen: issuesOpen,
            IssuesTotal: issuesTotal,
            Issues: issueDtos,
            AgeAxis: axis == RecordAgeAxis.Season ? "season" : "calendar",
            AgeAxisChecked: axisBoth + axisCalendar + axisSeason + axisNone,
            AgeAxisBoth: axisBoth,
            AgeAxisCalendarOnly: axisCalendar,
            AgeAxisSeasonOnly: axisSeason,
            AgeAxisNone: axisNone,
            AgeAxisMismatch: axisMismatch);
    }

    /* ──────────────────────── реестр претензий ──────────────────────── */

    public async Task<PagedResult<RecordIssueDto>> ListIssuesAsync(
        string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.RecordIssues.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(i => i.Status == status);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(i => i.Status == RecordIssueStatuses.Open ? 0 : 1)
            .ThenByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = new List<RecordIssueDto>(rows.Count);
        foreach (var i in rows)
            dtos.Add(await ToDtoAsync(i, ct));

        return new PagedResult<RecordIssueDto>(dtos, total, page, pageSize);
    }

    public async Task<RecordIssueDto> CreateIssueAsync(
        RecordIssueInputDto input, string createdBy, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        // Ключ реестра — ось + время. Повтор той же претензии не плодит дубль, а обновляет
        // обоснование: помечать одно и то же дважды admin-у незачем.
        var existing = await db.RecordIssues.FirstOrDefaultAsync(i =>
            i.RegionType == input.RegionType && i.RegionCode == input.RegionCode &&
            i.Category == input.Category && i.AgeKey == input.AgeKey &&
            i.Gender == input.Gender && i.PoolType == input.PoolType &&
            i.Style == input.Style && i.Distance == input.Distance &&
            i.FlaggedTime == input.FlaggedTime, ct);

        if (existing == null)
        {
            existing = new RecordIssue
            {
                RegionType = input.RegionType,
                RegionCode = input.RegionCode,
                Category = input.Category,
                AgeKey = input.AgeKey,
                Gender = input.Gender,
                PoolType = input.PoolType,
                Style = input.Style,
                Distance = input.Distance,
                FlaggedTime = input.FlaggedTime,
                CreatedBy = createdBy,
                CreatedAt = now
            };
            db.RecordIssues.Add(existing);
        }

        if (!string.IsNullOrWhiteSpace(input.Reason)) existing.Reason = input.Reason;
        if (!string.IsNullOrWhiteSpace(input.Note)) existing.Note = input.Note;
        existing.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(existing, ct);
    }

    public async Task<RecordIssueDto?> UpdateIssueAsync(
        int id, RecordIssueUpdateDto update, CancellationToken ct = default)
    {
        var issue = await db.RecordIssues.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue == null) return null;

        if (!string.IsNullOrWhiteSpace(update.Status)) issue.Status = update.Status;
        if (update.Note != null) issue.Note = update.Note;
        if (!string.IsNullOrWhiteSpace(update.Reason)) issue.Reason = update.Reason;
        issue.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(issue, ct);
    }

    public async Task<bool> DeleteIssueAsync(int id, CancellationToken ct = default)
    {
        var issue = await db.RecordIssues.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue == null) return false;

        db.RecordIssues.Remove(issue);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Досыпает в DTO флаг «претензия всё ещё про текущую запись»: ищет строку Records на той
    /// же оси и сравнивает время. Рекорд могли побить — тогда претензия стала историей.
    /// </summary>
    private async Task<RecordIssueDto> ToDtoAsync(RecordIssue i, CancellationToken ct)
    {
        var currentTime = await db.Records.AsNoTracking()
            .Where(r => r.RegionType == i.RegionType && r.RegionCode == i.RegionCode &&
                        r.Category == i.Category && r.AgeKey == i.AgeKey &&
                        r.Gender == i.Gender && r.PoolType == i.PoolType &&
                        r.Style == i.Style && r.Distance == i.Distance)
            .Select(r => r.Time)
            .FirstOrDefaultAsync(ct);

        return new RecordIssueDto(
            i.Id, i.RegionType, i.RegionCode, i.Category, i.AgeKey, i.Gender,
            i.PoolType, i.Style, i.Distance, i.FlaggedTime, i.Reason, i.Status,
            i.Note, i.CreatedBy, i.CreatedAt,
            RecordStillCurrent: currentTime == i.FlaggedTime);
    }

    /* ───────────────────────── helpers ───────────────────────── */

    /// <summary>
    /// По какой оси возраста ступень рекорда сходится с найденным пловцом.
    /// null — проверять нечего (см. <see cref="RecordVerification.AgeAxisMatch"/>).
    ///
    /// Эстафеты исключены сознательно: там ступень задаётся составом четвёрки, а мы нашли
    /// одного пловца по времени — его возраст ничего не доказывает.
    /// </summary>
    private static string? AgeAxisMatchOf(
        string category, string ageKey, string distance, int birthYear, DateTime swimDate)
    {
        if (birthYear <= 0) return null;
        if (distance.Contains('X', StringComparison.OrdinalIgnoreCase)) return null;

        var calendarAge = swimDate.Year - birthYear;
        var seasonAge = SeasonMath.AgeInSeason(SeasonMath.StartYearOf(swimDate), birthYear);

        var byCalendar = StepMatches(category, ageKey, calendarAge);
        var bySeason = seasonAge is int sa ? StepMatches(category, ageKey, sa) : null;

        // null от обоих = ступень не возрастная (open / adults / ISR): проверять нечего.
        if (byCalendar is null && bySeason is null) return null;

        return (byCalendar == true, bySeason == true) switch
        {
            (true, true)  => AgeAxisMatches.Both,
            (true, false) => AgeAxisMatches.Calendar,
            (false, true) => AgeAxisMatches.Season,
            _             => AgeAxisMatches.None
        };
    }

    /// <summary>
    /// Попадает ли возраст в ступень справочника: «11» — точное совпадение, «25-29» —
    /// диапазон masters. null — ключ не возрастной («», «adults», «ISR»).
    /// </summary>
    private static bool? StepMatches(string category, string ageKey, int age)
    {
        var key = (ageKey ?? "").Trim();
        if (key.Length == 0) return null;

        if (category == "age")
            return int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var step)
                ? step == age
                : null;

        if (category == "masters")
        {
            var parts = key.Split('-');
            return parts.Length == 2
                   && int.TryParse(parts[0], out var lo)
                   && int.TryParse(parts[1], out var hi)
                ? age >= lo && age <= hi
                : null;
        }

        return null;
    }

    private static string MatchKey(int ms, string gender, string poolType, string style, string distance) =>
        $"{ms}|{gender}|{poolType}|{style}|{distance}";

    /// <summary>«100m» → «100», «4X50m» → «4X50». В Results дистанция без суффикса.</summary>
    private static string TrimDistance(string distance) =>
        distance.EndsWith('m') || distance.EndsWith('M') ? distance[..^1] : distance;

    /// <summary>В базе живут оба написания пола («male»/«M»), сводим к одному.</summary>
    private static string NormalizeGender(string? gender) => gender switch
    {
        "F" or "female" => "female",
        "M" or "male" => "male",
        _ => gender ?? ""
    };

    private static DateTime? ParseRecordDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTime.TryParseExact(raw.Trim(), RecordDateFormats,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
    }
}
