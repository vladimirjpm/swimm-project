using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Реестр проверок данных (docs/data-integrity.md, фаза Д3): гоняет все <see cref="IDataCheck"/>
/// и ведёт находки.
///
/// Ключевое решение — находка живёт ДО УСТРАНЕНИЯ, а не до следующего прогона. Поэтому
/// таблица находок не привязана к прогону, а ключом служит (CheckId, EntityType, EntityId):
/// - находка вернулась → обновляем LastSeenAt и текст, статус «принято» СОХРАНЯЕМ;
/// - находки больше нет → помечаем fixed;
/// - иначе решение «это ошибка федерации, не чиним» пришлось бы принимать заново каждый
///   прогон. Ровно так уже устроены ручные пометки качества результатов (SuspectIsManual).
///
/// Прогон одной проверки изолирован: упавшая проверка не роняет остальные — иначе один
/// битый запрос лишал бы админа всей картины.
/// </summary>
public class DataCheckRunner(SwimmDbContext db, IEnumerable<IDataCheck> checks) : IDataCheckRunner
{
    public async Task<DataCheckRunDto> RunAllAsync(string trigger, CancellationToken ct = default)
    {
        var run = new DataCheckRun { Trigger = trigger, StartedAt = DateTime.UtcNow };
        db.DataCheckRuns.Add(run);
        await db.SaveChangesAsync(ct);

        var stored = await db.DataCheckFindings
            .Where(f => f.Resolution == null || f.Resolution == DataCheckResolutions.Accepted)
            .ToListAsync(ct);
        var storedByKey = stored.ToDictionary(Key);

        var states = await db.DataCheckStates.ToDictionaryAsync(s => s.CheckId, ct);

        var seen = new HashSet<string>();
        int errors = 0, warnings = 0, infos = 0;

        foreach (var check in checks)
        {
            DataCheckOutcome outcome;
            var failed = false;
            try
            {
                outcome = await check.RunAsync(ct);
            }
            catch (Exception ex)
            {
                // Упавшая проверка — сама по себе находка: молчать о ней хуже всего.
                failed = true;
                outcome = new DataCheckOutcome(1, [new DataCheckItem(
                    "Check", null, $"проверка не выполнилась: {ex.GetType().Name}: {ex.Message}")]);
            }

            // Полное число живёт здесь: список находок капнут, и по нему счётчик дашборда
            // недосчитался бы (проверка на 8071 находку кладёт 50).
            if (!states.TryGetValue(check.Id, out var state))
            {
                state = new DataCheckState { CheckId = check.Id };
                db.DataCheckStates.Add(state);
                states[check.Id] = state;
            }
            state.Severity = (int)check.Severity;
            state.Total = outcome.Total;
            state.Shown = outcome.Items.Count;
            state.Failed = failed;
            state.LastRunId = run.Id;
            state.LastRunAt = run.StartedAt;

            foreach (var item in outcome.Items)
            {
                var key = $"{check.Id}|{item.EntityType}|{item.EntityId}";
                if (!seen.Add(key)) continue;

                if (storedByKey.TryGetValue(key, out var existing))
                {
                    existing.LastSeenAt = run.StartedAt;
                    existing.Message = item.Message;
                    existing.Details = item.Details;
                    existing.Link = item.Link;
                    existing.PublicLink = item.PublicLink;
                    existing.SubjectName = item.SubjectName;
                    existing.FixKind = item.FixKind;
                    existing.FixEntityId = item.FixEntityId;
                    existing.Severity = (int)check.Severity;
                }
                else
                {
                    db.DataCheckFindings.Add(new DataCheckFinding
                    {
                        CheckId = check.Id,
                        Severity = (int)check.Severity,
                        EntityType = item.EntityType,
                        EntityId = item.EntityId,
                        Message = item.Message,
                        Details = item.Details,
                        Link = item.Link,
                        PublicLink = item.PublicLink,
                        SubjectName = item.SubjectName,
                        FixKind = item.FixKind,
                        FixEntityId = item.FixEntityId,
                        FirstSeenAt = run.StartedAt,
                        LastSeenAt = run.StartedAt
                    });
                }

                switch (check.Severity)
                {
                    case DataCheckSeverity.Error: errors++; break;
                    case DataCheckSeverity.Warning: warnings++; break;
                    default: infos++; break;
                }
            }
        }

        // Исчезнувшие — починены. Принятые тоже закрываем: раз находки больше нет, держать
        // решение по ней незачем (вернётся — заведётся заново).
        var fixedNow = stored.Where(f => !seen.Contains(Key(f))).ToList();
        foreach (var f in fixedNow)
        {
            f.Resolution = DataCheckResolutions.Fixed;
            f.ResolvedAt = run.StartedAt;
        }

        run.FinishedAt = DateTime.UtcNow;
        run.ErrorCount = errors;
        run.WarningCount = warnings;
        run.InfoCount = infos;
        run.FixedCount = fixedNow.Count;
        await db.SaveChangesAsync(ct);

        return ToDto(run);
    }

    public async Task<IReadOnlyList<DataCheckGroupDto>> GetCurrentAsync(CancellationToken ct = default)
    {
        var findings = await db.DataCheckFindings.AsNoTracking()
            .Where(f => f.Resolution == null || f.Resolution == DataCheckResolutions.Accepted)
            .OrderByDescending(f => f.Severity).ThenBy(f => f.CheckId).ThenBy(f => f.Id)
            .ToListAsync(ct);

        var byCheck = findings.GroupBy(f => f.CheckId).ToDictionary(g => g.Key, g => g.ToList());
        var states = await db.DataCheckStates.AsNoTracking().ToDictionaryAsync(s => s.CheckId, ct);

        // Показываем ВСЕ зарегистрированные проверки, даже пустые: «проверка есть и она
        // молчит» — полезная информация, отличная от «проверки нет».
        return checks
            .Select(c =>
            {
                var items = byCheck.GetValueOrDefault(c.Id, []);
                var state = states.GetValueOrDefault(c.Id);
                return new DataCheckGroupDto(
                    c.Id, c.Title, c.Description, c.Severity,
                    items.Count(f => f.Resolution == null),
                    items.Count(f => f.Resolution == DataCheckResolutions.Accepted),
                    items.Select(ToDto).ToList(),
                    state?.Total, state?.LastRunAt, state?.Failed ?? false);
            })
            .OrderByDescending(g => g.OpenCount > 0)
            .ThenByDescending(g => g.Severity)
            .ThenBy(g => g.CheckId, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<DataCheckRunDto>> GetHistoryAsync(int limit = 20, CancellationToken ct = default) =>
        (await db.DataCheckRuns.AsNoTracking()
            .OrderByDescending(r => r.Id).Take(limit).ToListAsync(ct))
        .Select(ToDto).ToList();

    public async Task<(DataCheckRunDto? LastRun, IReadOnlyList<DataCheckStateDto> States)> GetStateAsync(
        CancellationToken ct = default)
    {
        var lastRun = await db.DataCheckRuns.AsNoTracking()
            .OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);

        // Принятые находки состояние не знает (оно про «что нашла проверка»), а потребителю
        // нужно «что ещё требует работы» — иначе решения Р16-типа («не чиним») висели бы
        // в счётчиках вечно.
        var accepted = await db.DataCheckFindings.AsNoTracking()
            .Where(f => f.Resolution == DataCheckResolutions.Accepted)
            .GroupBy(f => f.CheckId)
            .Select(g => new { CheckId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CheckId, x => x.Count, ct);

        var states = (await db.DataCheckStates.AsNoTracking().ToListAsync(ct))
            .Select(s => new DataCheckStateDto(
                s.CheckId, (DataCheckSeverity)s.Severity, s.Total, s.Shown, s.Failed,
                s.LastRunId, s.LastRunAt, accepted.GetValueOrDefault(s.CheckId)))
            .ToList();

        return (lastRun is null ? null : ToDto(lastRun), states);
    }

    public async Task<bool> AcceptAsync(int findingId, string? note, CancellationToken ct = default)
    {
        var f = await db.DataCheckFindings.FirstOrDefaultAsync(x => x.Id == findingId, ct);
        if (f == null || f.Resolution == DataCheckResolutions.Fixed) return false;

        f.Resolution = DataCheckResolutions.Accepted;
        f.ResolvedAt = DateTime.UtcNow;
        f.Note = note;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int?> FixSwimmerGenderAsync(int findingId, string gender, CancellationToken ct = default)
    {
        if (gender is not ("male" or "female")) return null;

        var f = await db.DataCheckFindings.FirstOrDefaultAsync(x => x.Id == findingId, ct);
        if (f?.FixKind != DataCheckFixKinds.SwimmerGender || f.FixEntityId is not { } swimmerId)
            return null;

        var swimmer = await db.Swimmers.FirstOrDefaultAsync(s => s.Id == swimmerId, ct);
        if (swimmer is null) return null;

        swimmer.Gender = gender;

        // И строки этого пловца, у которых пола нет: проверка смотрит именно на них, а
        // Results.Gender заполняется на импорте — иначе находка висела бы до переимпорта.
        // Трогаем ТОЛЬКО пустые: перезаписывать напечатанный в протоколе пол нельзя.
        var rows = await db.Results
            .Where(r => r.SwimmerId == swimmerId && r.RelayId == null
                        && (r.Gender == null || r.Gender == "" || r.Gender == "none"))
            .ToListAsync(ct);
        foreach (var r in rows) r.Gender = gender;

        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    public async Task<bool> ReopenAsync(int findingId, CancellationToken ct = default)
    {
        var f = await db.DataCheckFindings.FirstOrDefaultAsync(x => x.Id == findingId, ct);
        if (f?.Resolution != DataCheckResolutions.Accepted) return false;

        f.Resolution = null;
        f.ResolvedAt = null;
        f.Note = null;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static string Key(DataCheckFinding f) => $"{f.CheckId}|{f.EntityType}|{f.EntityId}";

    private static DataCheckRunDto ToDto(DataCheckRun r) =>
        new(r.Id, r.StartedAt, r.FinishedAt, r.Trigger, r.ErrorCount, r.WarningCount, r.InfoCount, r.FixedCount);

    private static DataCheckFindingDto ToDto(DataCheckFinding f) =>
        new(f.Id, f.CheckId, (DataCheckSeverity)f.Severity, f.EntityType, f.EntityId,
            f.Message, f.Details, f.Link, f.FirstSeenAt, f.LastSeenAt, f.Resolution, f.Note,
            f.PublicLink, f.SubjectName, f.FixKind, f.FixEntityId);
}
