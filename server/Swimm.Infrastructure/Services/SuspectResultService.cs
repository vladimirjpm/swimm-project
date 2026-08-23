using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// I/O-обёртка вокруг <see cref="SuspectResultDetector"/>: грузит строки события/дня,
/// перезаписывает АВТОМАТИЧЕСКИЕ пометки, сохраняет ручные.
///
/// Почему ручные живут дольше: автоправила меняются и пересчитываются при каждом прогоне
/// и переимпорте, а решение человека «эта строка врёт» — факт о данных, его нельзя
/// молча терять (иначе после каждого импорта пришлось бы перепроверять руками).
/// </summary>
public class SuspectResultService(SwimmDbContext db, ICacheService cache) : ISuspectResultService
{
    public async Task<SuspectScanResultDto> ScanAsync(
        int? eventId, int? competitionId, CancellationToken ct = default)
    {
        var scope = Scope(eventId, competitionId);

        var rows = await scope
            .Select(r => new SuspectCandidateRow(
                r.Id, r.SwimmerId, r.Style.Name, r.Distance, r.Gender,
                r.TimeMillisecond, r.CompetitionDate, r.RelayId != null, r.TimeFail, r.AgeGroup,
                r.InternationalPoints, r.HeatType, r.Round, r.Heat))
            .ToListAsync(ct);

        var verdicts = SuspectResultDetector.Detect(rows, await LoadPersonalHistoryAsync(rows, ct))
            .ToDictionary(v => v.ResultId);

        // Трекаем только то, что может измениться: уже помеченные + новые кандидаты.
        var affectedIds = verdicts.Keys.ToHashSet();
        var tracked = await scope
            .Where(r => r.SuspectReason != null || affectedIds.Contains(r.Id))
            .ToListAsync(ct);

        int flagged = 0, cleared = 0, manualKept = 0;

        foreach (var r in tracked)
        {
            if (r.SuspectIsManual)
            {
                manualKept++;
                continue;
            }

            if (verdicts.TryGetValue(r.Id, out var v))
            {
                r.SuspectReason = v.Reason;
                r.SuspectNote = Truncate(v.Note, 300);
                flagged++;
            }
            else if (r.SuspectReason != null)
            {
                // Автопометка, которую правила больше не подтверждают, — снимаем.
                r.SuspectReason = null;
                r.SuspectNote = null;
                cleared++;
            }
        }

        // Отметка «проверка была». Без неё пустой список неотличим от «никто не смотрел»,
        // и в списке соревнований нечего было бы красить зелёным.
        var scanStamp = DateTime.UtcNow;
        var scannedCompetitions = eventId is { } eid
            ? await db.Competitions.Where(c => c.EventId == eid).ToListAsync(ct)
            : await db.Competitions.Where(c => c.Id == competitionId).ToListAsync(ct);
        foreach (var c in scannedCompetitions) c.QualityScannedAt = scanStamp;

        await db.SaveChangesAsync(ct);
        // Рекорды в шапке соревнования считаются с учётом пометок — сбрасываем кэш.
        await cache.InvalidateAllAsync();

        var result = await GetFlaggedAsync(eventId, competitionId, ct);
        return new SuspectScanResultDto(rows.Count, flagged, cleared, manualKept, result);
    }

    public async Task<IReadOnlyList<SuspectRowDto>> GetFlaggedAsync(
        int? eventId, int? competitionId, CancellationToken ct = default)
        => await Scope(eventId, competitionId)
            .Where(r => r.SuspectReason != null)
            .OrderBy(r => r.CompetitionDate).ThenBy(r => r.Id)
            .Select(r => new SuspectRowDto(
                r.Id, r.CompetitionId, r.CompetitionDate,
                (r.Swimmer.FirstName + " " + r.Swimmer.LastName).Trim(),
                r.Club.Name, r.Style.Name, r.Distance, r.Gender, r.TimeOriginal,
                r.SuspectReason!, r.SuspectIsManual, r.SuspectNote))
            .ToListAsync(ct);

    /// <summary>
    /// Личная история пловцов соревнования — вход правила «выброс относительно себя» (Б1).
    /// В скоупе её нет по определению: сравнивать надо с ДРУГИМИ стартами, поэтому это
    /// отдельный запрос, а не часть выборки соревнования.
    ///
    /// Тянем только личные зачтённые заплывы с очками и только в окне вокруг дат
    /// соревнования — иначе на пловца с десятилетней историей приезжали бы сотни строк,
    /// из которых правило всё равно смотрит лишь ближайшие ±120 дней.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, IReadOnlyList<PersonalSwim>>> LoadPersonalHistoryAsync(
        IReadOnlyCollection<SuspectCandidateRow> rows, CancellationToken ct)
    {
        var swimmerIds = rows.Select(r => r.SwimmerId).Distinct().ToList();
        if (swimmerIds.Count == 0) return new Dictionary<int, IReadOnlyList<PersonalSwim>>();

        // Запас к окну правила: даты дней многодневки разъезжаются, и жёсткая граница
        // отрезала бы сравнения у крайних дней.
        const int windowDays = 150;
        var from = rows.Min(r => r.CompetitionDate).AddDays(-windowDays);
        var to = rows.Max(r => r.CompetitionDate).AddDays(windowDays);

        var history = await db.Results.AsNoTracking()
            .Where(r => swimmerIds.Contains(r.SwimmerId))
            .Where(r => r.RelayId == null && !r.TimeFail && r.InternationalPoints > 0)
            .Where(r => r.CompetitionDate >= from && r.CompetitionDate <= to)
            .Select(r => new { r.Id, r.SwimmerId, r.InternationalPoints, r.CompetitionDate })
            .ToListAsync(ct);

        return history
            .GroupBy(h => h.SwimmerId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PersonalSwim>)g
                    .Select(h => new PersonalSwim(h.Id, h.InternationalPoints, h.CompetitionDate))
                    .ToList());
    }

    public async Task<IReadOnlyList<SuspectRowDto>> SearchAsync(
        int? eventId, int? competitionId, string query, int limit = 30, CancellationToken ct = default)
    {
        var q = (query ?? "").Trim().ToLower();
        if (q.Length < 2) return [];

        // ToLower().Contains, а не EF.Functions.ILike: последний живёт только в Npgsql, и
        // тест на InMemory-провайдере писать было бы не на чем. Индексов тут всё равно нет —
        // выборка идёт внутри одного соревнования, это сотни строк.
        //
        // Пустой Reason у непомеченной строки: DTO общий с помеченными, и «пусто» здесь
        // читается однозначно — строка ещё не разобрана.
        return await Scope(eventId, competitionId)
            .Where(r =>
                (r.Swimmer.LastName + " " + r.Swimmer.FirstName).ToLower().Contains(q) ||
                r.Club.Name.ToLower().Contains(q) ||
                (r.TimeOriginal ?? "").ToLower().Contains(q) ||
                r.Distance.ToLower().Contains(q))
            .OrderBy(r => r.CompetitionDate).ThenBy(r => r.Id)
            .Take(limit)
            .Select(r => new SuspectRowDto(
                r.Id, r.CompetitionId, r.CompetitionDate,
                (r.Swimmer.FirstName + " " + r.Swimmer.LastName).Trim(),
                r.Club.Name, r.Style.Name, r.Distance, r.Gender, r.TimeOriginal,
                r.SuspectReason ?? "", r.SuspectIsManual, r.SuspectNote))
            .ToListAsync(ct);
    }

    public async Task<bool> SetManualAsync(
        long resultId, bool flagged, string? note, CancellationToken ct = default)
    {
        var row = await db.Results.FirstOrDefaultAsync(r => r.Id == resultId, ct);
        if (row is null) return false;

        if (flagged)
        {
            row.SuspectReason = SuspectReasons.Manual;
            row.SuspectIsManual = true;
            row.SuspectNote = Truncate(note, 300);
        }
        else
        {
            // Снятие убирает и автоматическую пометку: человек посмотрел и решил, что
            // строка в порядке. Следующий скан её не вернёт — он ручные не трогает.
            row.SuspectReason = null;
            row.SuspectNote = note is null ? null : Truncate(note, 300);
            row.SuspectIsManual = true;
        }

        await db.SaveChangesAsync(ct);
        await cache.InvalidateAllAsync();
        return true;
    }

    /// <summary>
    /// Скоуп проверки: событие целиком (все дни) или один день. Правила «повтор дисциплины»
    /// и «пол против остальных заплывов» смотрят пловца ЦЕЛИКОМ по событию, поэтому
    /// вызывать со eventId правильнее, чем по одному дню.
    /// </summary>
    private IQueryable<ResultRecord> Scope(int? eventId, int? competitionId)
    {
        var q = db.Results.AsQueryable();
        if (competitionId is { } cid) return q.Where(r => r.CompetitionId == cid);
        if (eventId is { } eid) return q.Where(r => r.Competition.EventId == eid);
        throw new ArgumentException("Нужен eventId или competitionId");
    }

    private static string? Truncate(string? s, int max)
        => s is null ? null : s.Length <= max ? s : s[..max];
}
