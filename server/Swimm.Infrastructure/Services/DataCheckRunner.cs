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
public class DataCheckRunner(
    SwimmDbContext db,
    IEnumerable<IDataCheck> checks,
    // Пересчёт зачёта после привязки правила. Необязателен: без него правило проставится,
    // но цифры Top Clubs останутся старыми до следующего пересчёта — в тестах это не нужно.
    IClubStandingService? standings = null,
    // Сброс кэша после правок, меняющих витрину (пол участвует в рекордах и season best).
    // Необязателен по той же причине, что и standings: в тестах кэша нет.
    ICacheService? cache = null,
    // «Не дубли» для пар дедупа при «Принять». Необязателен, как и остальные: в тестах,
    // где проверяют только реестр, развязывать нечего.
    IDedupIgnoreService? dedupIgnore = null) : IDataCheckRunner
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
                var key = Key(check.Id, item.EntityType, item.EntityId, item.FixKind, item.FixEntityId);
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

        // Живое состояние субъектов, у которых есть точечное исправление: пол и привязка к
        // loglig. Читаем на выдаче, а НЕ храним в находке — находка обновляется только
        // прогоном, и сохранённое значение врало бы сразу после нажатия кнопки: человек
        // поправил, а список показывает старое.
        var subjectIds = findings
            .Where(f => (f.FixKind == DataCheckFixKinds.SwimmerGender
                         || f.FixKind == DataCheckFixKinds.SwimmerGenderAlign)
                        && f.FixEntityId != null)
            .Select(f => f.FixEntityId!.Value)
            .Distinct()
            .ToList();

        var subjects = subjectIds.Count == 0
            ? []
            : await db.Swimmers.AsNoTracking()
                .Where(s => subjectIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Gender, s.LogligId })
                .ToDictionaryAsync(s => s.Id, s => (s.Gender, s.LogligId), ct);

        // Галочка «пол уже стоит» должна показывать ТО, на что смотрит проверка, — пол самой
        // строки результата. Пол пловца бывает известен (пришёл из другого протокола), а
        // строка всё равно пустая: тогда галочка по пловцу врёт — находка висит, а кнопка
        // выглядит нажатой.
        var resultIds = findings
            .Where(f => f.FixKind == DataCheckFixKinds.SwimmerGender
                        && f.EntityType == "Result" && f.EntityId != null)
            .Select(f => (long)f.EntityId!.Value)
            .Distinct()
            .ToList();

        var resultGenders = resultIds.Count == 0
            ? []
            : await db.Results.AsNoTracking()
                .Where(r => resultIds.Contains(r.Id))
                .Select(r => new { r.Id, r.Gender })
                .ToDictionaryAsync(r => r.Id, r => r.Gender, ct);

        // Текущее правило клубных очков — по той же причине живое: селект в находке должен
        // показывать то, что в базе сейчас, а не то, что было на прогоне.
        var ruleCompIds = findings
            .Where(f => f.FixKind == DataCheckFixKinds.CompetitionClubRule && f.FixEntityId != null)
            .Select(f => f.FixEntityId!.Value)
            .Distinct()
            .ToList();

        var clubRules = ruleCompIds.Count == 0
            ? []
            : await db.Competitions.AsNoTracking()
                .Where(c => ruleCompIds.Contains(c.Id))
                .Select(c => new { c.Id, c.PointRuleClubsId })
                .ToDictionaryAsync(c => c.Id, c => c.PointRuleClubsId, ct);

        // compID на isr.org.il — тоже живой: штамп проставляется переимпортом и бэкфиллом
        // Discovery, и сохранённый в находке он остался бы пустым до следующего прогона.
        var compIds = findings
            .Where(f => f.EntityType == "Competition" && f.EntityId != null)
            .Select(f => f.EntityId!.Value)
            .Distinct()
            .ToList();

        var orgCompIds = await ResolveOrgCompIdsAsync(compIds, ct);

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
                    items.Select(f => ToDto(f, subjects, resultGenders, clubRules, orgCompIds)).ToList(),
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

        // «Принять» пару дедупа = «это не дубли, а тёзки». Значит она обязана уйти в тот же
        // Sys_DedupIgnoredPairs, что заводит ✕ на /Admin/Swimmers: иначе механизма два —
        // находка принята, а в списке дублей пара продолжает висеть и просить склейки.
        if (f.FixKind == DataCheckFixKinds.DedupIgnore && f.EntityId is { } canonId
            && f.FixEntityId is { } dupId && canonId != dupId && dedupIgnore != null)
        {
            var entityType = f.EntityType == "Club" ? DedupEntityType.Club : DedupEntityType.Swimmer;
            await dedupIgnore.AddAsync(entityType, canonId, dupId, ct);
        }

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

    public async Task<int?> AlignSwimmerGenderAsync(int findingId, string gender, CancellationToken ct = default)
    {
        if (gender is not ("male" or "female")) return null;

        var f = await db.DataCheckFindings.FirstOrDefaultAsync(x => x.Id == findingId, ct);
        if (f?.FixKind != DataCheckFixKinds.SwimmerGenderAlign || f.FixEntityId is not { } swimmerId)
            return null;

        var swimmer = await db.Swimmers.FirstOrDefaultAsync(s => s.Id == swimmerId, ct);
        if (swimmer is null) return null;

        swimmer.Gender = gender;

        // Здесь, в отличие от `results.no-gender`, перезаписываем и НЕПУСТОЙ пол строки:
        // находка и есть «копии разошлись», а человек только что сказал, какая верна.
        // Эстафеты не трогаем — там пол команды, а не пловца.
        var rows = await db.Results
            .Where(r => r.SwimmerId == swimmerId && r.RelayId == null && r.Gender != gender)
            .ToListAsync(ct);
        foreach (var r in rows) r.Gender = gender;

        await db.SaveChangesAsync(ct);
        // Пол участвует в выборках витрин (рекорды, season best) — кэш обязан протухнуть.
        if (cache != null) await cache.InvalidateAllAsync();
        return rows.Count;
    }

    public async Task<bool> FixCompetitionClubRuleAsync(int findingId, int ruleId, CancellationToken ct = default)
    {
        var f = await db.DataCheckFindings.FirstOrDefaultAsync(x => x.Id == findingId, ct);
        if (f?.FixKind != DataCheckFixKinds.CompetitionClubRule || f.FixEntityId is not { } competitionId)
            return false;

        if (!await db.PointRulesClubs.AnyAsync(r => r.Id == ruleId, ct)) return false;

        var comp = await db.Competitions.FirstOrDefaultAsync(c => c.Id == competitionId, ct);
        if (comp is null) return false;

        // Регламент у многодневного события один — ставим всем дням, иначе у дней одного
        // чемпионата получился бы разный зачёт (тот же принцип, что в быстрой правке списка).
        var targets = comp.EventId is int eventId
            ? await db.Competitions.Where(c => c.EventId == eventId).ToListAsync(ct)
            : [comp];

        foreach (var c in targets) c.PointRuleClubsId = ruleId;
        await db.SaveChangesAsync(ct);

        // Клубный зачёт материализован — без пересчёта цифры остались бы старыми, и
        // находка выглядела бы «починенной», не изменив ничего на витринах.
        if (standings is not null)
            foreach (var c in targets)
                await standings.RebuildForCompetitionAsync(c.Id, ct);

        return true;
    }

    public async Task<(int Findings, int Rows)> FixAllKnownSwimmerGendersAsync(CancellationToken ct = default)
    {
        var swimmerIds = await db.DataCheckFindings
            .Where(f => f.Resolution == null && f.FixKind == DataCheckFixKinds.SwimmerGender
                        && f.FixEntityId != null)
            .Select(f => f.FixEntityId!.Value)
            .Distinct()
            .ToListAsync(ct);
        if (swimmerIds.Count == 0) return (0, 0);

        // Только те, у кого пол пловца УЖЕ известен: массово гадать за человека нельзя,
        // здесь мы лишь дописываем в строки ответ, который в базе уже есть.
        var known = await db.Swimmers
            .Where(s => swimmerIds.Contains(s.Id) && (s.Gender == "male" || s.Gender == "female"))
            .Select(s => new { s.Id, s.Gender })
            .ToDictionaryAsync(s => s.Id, s => s.Gender!, ct);
        if (known.Count == 0) return (0, 0);

        var knownIds = known.Keys.ToList();
        var rows = await db.Results
            .Where(r => knownIds.Contains(r.SwimmerId) && r.RelayId == null
                        && (r.Gender == null || r.Gender == "" || r.Gender == "none"))
            .ToListAsync(ct);
        foreach (var r in rows) r.Gender = known[r.SwimmerId];

        await db.SaveChangesAsync(ct);
        return (known.Count, rows.Count);
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

    /// <summary>
    /// Ключ находки между прогонами. У находок-ПАР (дедуп) в него входит и второй участник:
    /// иначе «A ← B» и «A ← C» неразличимы, вторая пара молча терялась, а «принять» одну
    /// значило спрятать обе.
    /// </summary>
    private static string Key(DataCheckFinding f) =>
        Key(f.CheckId, f.EntityType, f.EntityId, f.FixKind, f.FixEntityId);

    private static string Key(string checkId, string entityType, int? entityId, string? fixKind, int? fixEntityId) =>
        fixKind == DataCheckFixKinds.DedupIgnore
            ? $"{checkId}|{entityType}|{entityId}|{fixEntityId}"
            : $"{checkId}|{entityType}|{entityId}";

    private static DataCheckRunDto ToDto(DataCheckRun r) =>
        new(r.Id, r.StartedAt, r.FinishedAt, r.Trigger, r.ErrorCount, r.WarningCount, r.InfoCount, r.FixedCount);

    /// <summary>
    /// compID на isr.org.il для соревнований, попавших в находки. У многодневки он проставлен
    /// не каждому дню: <c>Competition.OrgCompId</c> — альтернативный ключ с UNIQUE-индексом
    /// (максимум один день), а общий штамп Д2 живёт на событии. Поэтому дню без своего compID
    /// подставляем событийный, а затем первый непустой у соседних дней — иначе ссылка на
    /// протокол пропадала бы у дней 2..N.
    /// </summary>
    private async Task<Dictionary<int, int?>> ResolveOrgCompIdsAsync(
        List<int> compIds, CancellationToken ct)
    {
        if (compIds.Count == 0) return [];

        var comps = await db.Competitions.AsNoTracking()
            .Where(c => compIds.Contains(c.Id))
            .Select(c => new { c.Id, c.EventId, c.OrgCompId })
            .ToListAsync(ct);

        var eventIds = comps
            .Where(c => c.OrgCompId == null && c.EventId != null)
            .Select(c => c.EventId!.Value)
            .Distinct()
            .ToList();

        var byEvent = new Dictionary<int, int?>();
        if (eventIds.Count > 0)
        {
            byEvent = await db.CompetitionEvents.AsNoTracking()
                .Where(e => eventIds.Contains(e.Id))
                .Select(e => new { e.Id, e.OrgCompId })
                .ToDictionaryAsync(e => e.Id, e => e.OrgCompId, ct);

            var stampedDays = await db.Competitions.AsNoTracking()
                .Where(c => c.EventId != null && eventIds.Contains(c.EventId!.Value) && c.OrgCompId != null)
                .Select(c => new { EventId = c.EventId!.Value, c.OrgCompId })
                .ToListAsync(ct);

            foreach (var g in stampedDays.GroupBy(d => d.EventId))
                if (byEvent.GetValueOrDefault(g.Key) is null)
                    byEvent[g.Key] = g.First().OrgCompId;
        }

        return comps.ToDictionary(
            c => c.Id,
            c => c.OrgCompId ?? (c.EventId is { } ev ? byEvent.GetValueOrDefault(ev) : null));
    }

    private static DataCheckFindingDto ToDto(
        DataCheckFinding f,
        IReadOnlyDictionary<int, (string? Gender, int? LogligId)>? subjects = null,
        IReadOnlyDictionary<long, string>? resultGenders = null,
        IReadOnlyDictionary<int, int?>? clubRules = null,
        IReadOnlyDictionary<int, int?>? orgCompIds = null)
    {
        var subject = f.FixEntityId is { } id && subjects is not null && subjects.TryGetValue(id, out var v)
            ? v
            : default;

        // Для находок по строке результата пол берём со строки: именно он «не проставлен».
        // Пол пловца оставляем запасным вариантом — он совпадает после нажатия кнопки.
        var gender = f.EntityType == "Result" && resultGenders is not null && f.EntityId is { } rid
            ? resultGenders.TryGetValue(rid, out var rg) && rg is not (null or "" or "none") ? rg : null
            : subject.Gender;

        return new DataCheckFindingDto(
            f.Id, f.CheckId, (DataCheckSeverity)f.Severity, f.EntityType, f.EntityId,
            f.Message, f.Details, f.Link, f.FirstSeenAt, f.LastSeenAt, f.Resolution, f.Note,
            f.PublicLink, f.SubjectName, f.FixKind, f.FixEntityId,
            gender, subject.LogligId,
            f.FixEntityId is { } cid && clubRules is not null && clubRules.TryGetValue(cid, out var rule)
                ? rule
                : null,
            f.EntityType == "Competition" && f.EntityId is { } compId && orgCompIds is not null
                ? orgCompIds.GetValueOrDefault(compId)
                : null);
    }
}
