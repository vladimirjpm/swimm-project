using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="ISiblingDayPullService"/>
/// <remarks>
/// Повторяет рисунок ручного затягивания 488 (журнал docs/pull-to-prod.md, 24.11.2026): импорт
/// файлом без compID и без записи автозабора (у нового дня <c>OrgCompId</c> пуст — штамп один на
/// старт, он у лежащего дня), затем сбор в событие с общим именем (<see cref="ICompetitionDayJoinService"/>);
/// compID поднимается на событие, и Р115 сама видит «2 из 2». Отличие от ручного пути — фоновые шаги
/// loglig идут по номеру соседа, а не по номеру записи федерации.
/// </remarks>
public sealed class SiblingDayPullService(
    SwimmDbContext db,
    IProgrammeCheckService programmeCheck,
    ICompetitionDiscoveryProvider loglig,
    IDiscoveryPreviewService previews,
    IImportJobQueue jobs,
    ICompetitionDayJoinService joiner,
    IAdminAuditService audit,
    ILogger<SiblingDayPullService> logger) : ISiblingDayPullService
{
    public const string AuditAction = "competition.pull-sibling-day";
    private const string DateFormat = "dd/MM/yyyy";

    // Очередь импорта — в памяти процесса, поэтому и замок «один импорт дня на старт» — здесь:
    // проверка «дня нет в базе» и постановка — под одним шлюзом, а поставленная, но ещё не
    // выполненная задача держит старт до своего конца (второй клик до импорта первого — отказ).
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly ConcurrentDictionary<int, Guid> Pending = new();

    /// <summary>Для тестов: забыть поставленные задачи.</summary>
    internal static void ResetPendingForTests() => Pending.Clear();

    public async Task<SiblingDayPreview> PreviewAsync(int discoveryId, int logligId, CancellationToken ct = default)
    {
        var start = await LoadStartAsync(discoveryId, ct);
        if (start.Refusal != null) return SiblingDayPreview.Refused(start.Refusal);

        // Тот же вердикт, что показал кнопку: кандидат ровно один, и это он (Р123).
        var verdict = await programmeCheck.CheckAsync(discoveryId, ct);
        if (ProgrammeCheckRules.PullCandidate(verdict) != logligId)
            return SiblingDayPreview.Refused(
                "поиск больше не находит один подходящий день — нажмите «Проверить, чего не хватает» ещё раз");
        var (candidateRefusal, candidate) = await VerifyCandidateAsync(start, logligId, ct);
        if (candidateRefusal != null) return SiblingDayPreview.Refused(candidateRefusal);

        var p = await previews.PreviewSiblingAsync(discoveryId, logligId, ct);
        if (p.Error != null || p.Parsed is null)
            return SiblingDayPreview.Refused($"результаты дня на loglig не разобрались: {p.Error}");

        var refusal = SiblingDayRules.FileDateRefusal(p.Parsed.Competitions.Select(c => c.Date),
                          ProgrammeCheckRules.NeighbourDates(candidate!), start.Missing, start.DbDays)
                      ?? (p.ExistingCompetitionId is int already ? "этот день у нас уже есть" : null)
                      ?? await OverlapRefusalAsync(start, p.Parsed.ResultsJson, ct);
        if (refusal != null)
        {
            previews.RemoveEntry(p.PreviewId);
            return SiblingDayPreview.Refused(refusal);
        }

        var date = p.Parsed.Competitions[0].Date.Trim();
        var existing = await LoadExistingDayAsync(start, date, ct);
        var (pool, conflict) = SiblingDayRules.DefaultPool(existing.Day.PoolType, p.Flags?.PoolType);
        var overlapCheck = await OverlapAsync(start, p.Parsed.ResultsJson, ct);
        var overlapRows = overlapCheck.Matched.Count;
        var eventName = existing.Day.EventId is int ev
            ? await db.CompetitionEvents.AsNoTracking().Where(e => e.Id == ev).Select(e => e.Name).FirstOrDefaultAsync(ct)
            : null;

        var context = new SiblingDayContext(
            logligId, candidate!.Title, date,
            existing.Day.Id, existing.Day.SubName ?? existing.Day.Name, existing.Day.Date, existing.Day.PoolType,
            existing.Day.EventId, eventName,
            eventName ?? LogligTitleRules.CommonName(existing.Day.SubName ?? existing.Day.Name),
            p.Flags?.PoolType, conflict,
            existing.Day.PointRuleClubsId, existing.Day.PointRuleSwimmersId, overlapRows,
            MissingDayTexts.ImportSummaryTemplate(start.DbDays, ParseDate(date) ?? default, p.Parsed.ResultCount,
                nameEditable: existing.Day.EventId == null),
            MissingDayTexts.DefaultsNote(ParseDate(existing.Day.Date) ?? default),
            conflict ? MissingDayTexts.PoolConflict(ParseDate(existing.Day.Date) ?? default, existing.Day.PoolType,
                ParseDate(date) ?? default, p.Flags?.PoolType, start.DbDays) : null,
            overlapCheck.By == OverlapBy.None ? MissingDayTexts.OverlapNotChecked : null);
        return new SiblingDayPreview(null, p with { Flags = DefaultsFromDay(p.Flags, existing, pool, conflict) }, context);
    }

    public async Task<SiblingDayEnqueue> ImportAsync(int discoveryId, SiblingDayImportRequest request, string actor,
        CancellationToken ct = default)
    {
        var entry = previews.GetEntry(request.PreviewId);
        if (entry == null)
            return SiblingDayEnqueue.Refused($"превью устарело (живёт {previews.EntryLifetime.TotalMinutes:0} минут) — нажмите кнопку «Затянуть …» ещё раз");
        if (entry.SiblingLogligId != request.LogligId || entry.DiscoveredId != discoveryId)
            return SiblingDayEnqueue.Refused("это превью другого дня — нажмите кнопку «Затянуть …» ещё раз");

        var pool = SiblingDayRules.NormalizePool(request.PoolType);
        if (pool == null) return SiblingDayEnqueue.Refused("выберите бассейн: 25 или 50 м");

        // Сеть — до шлюза: два запроса к loglig (заголовки привязанного и соседа).
        var start = await LoadStartAsync(discoveryId, ct);
        if (start.Refusal != null) return SiblingDayEnqueue.Refused(start.Refusal);
        var (candidateRefusal, candidate) = await VerifyCandidateAsync(start, request.LogligId, ct);
        if (candidateRefusal != null) return SiblingDayEnqueue.Refused(candidateRefusal);

        await Gate.WaitAsync(ct);
        try
        {
            if (Pending.TryGetValue(start.OrgCompId, out var pendingJob)
                && jobs.GetStatus(pendingJob) is { State: ImportJobState.Queued or ImportJobState.Running })
                return SiblingDayEnqueue.Refused("этот день уже затягивается — дождитесь конца");

            // База могла измениться, пока шли запросы: перечитываем всё под шлюзом И под общим замком дня
            // (деление, сбор, --join-days ждут, пока идут проверки). Замок — в транзакции через стратегию
            // повторов; ставим задачу уже после commit, чтобы повтор транзакции не поставил её дважды.
            var lockIds = start.Days.Select(d => d.Id).ToList();
            var strategy = db.Database.CreateExecutionStrategy();
            var plan = await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await CompetitionDayLock.AcquireAsync(db, lockIds, ct);
                var checkedPlan = await CheckUnderLockAsync(discoveryId, entry, candidate!, pool, request, ct);
                await tx.CommitAsync(ct);
                return checkedPlan;
            });
            if (plan.Refusal != null) return SiblingDayEnqueue.Refused(plan.Refusal);
            start = plan.Start!;
            var existing = plan.Existing!;
            var date = plan.Date!;
            var newEventName = plan.NewEventName;

            var target = new SiblingDayTarget(discoveryId, start.OrgCompId, request.LogligId, existing.Day.Id,
                existing.Day.EventId, newEventName, date, actor);
            // Событие уже есть — импорт кладёт день прямо в него (имя дня = имя события, номер дня
            // следующий). Нет — день ложится отдельным соревнованием, фон собирает оба дня в событие.
            var options = new ImportEventOptions(
                existing.Day.EventId, null,
                PointRuleClubsId: request.PointRuleClubsId,
                PointRuleSwimmersId: existing.Day.PointRuleSwimmersId,
                IsAward: request.IsAward,
                IsChampionship: request.IsChampionship,
                IsMasters: request.IsMasters,
                ClubPointsDisabled: request.ClubPointsDisabled,
                PoolType: pool,
                SiblingDay: target);
            var json = ImportPayloadSuspectFlags.Apply(entry.Parsed.ResultsJson, request.SuspectFlags);
            // Без discoveredId и compID — как ручной путь 488: статус и штамп записи федерации — про её
            // протокол, а штамп compID один на старт и стоит у лежащего дня (событие получит его при сборе).
            var jobId = jobs.Enqueue(Encoding.UTF8.GetBytes(json), entry.FileName,
                request.CategoryKeys is { Length: > 0 } cats ? cats : null, options);
            previews.RemoveEntry(request.PreviewId);
            Pending[start.OrgCompId] = jobId;

            await audit.LogAsync(AuditAction, "Competition", existing.Day.Id.ToString(CultureInfo.InvariantCulture),
                $"Затянуть этот день: compID {start.OrgCompId}, loglig № {request.LogligId} ({date}) поставлен в очередь импорта — "
                + (existing.Day.EventId is int ev
                    ? $"в событие #{ev}"
                    : $"вторым днём к #{existing.Day.Id}, событие «{newEventName}» соберётся после импорта")
                + $" (discovery #{discoveryId})",
                new
                {
                    Stage = "queued", start.OrgCompId, SiblingLogligId = request.LogligId, BoundLogligId = start.LogligId,
                    Date = date, ExistingCompetitionId = existing.Day.Id, existing.Day.EventId, NewEventName = newEventName,
                    PoolType = pool, JobId = jobId, DiscoveryId = discoveryId,
                }, CancellationToken.None);
            return new SiblingDayEnqueue(null, jobId, existing.Day.EventId, existing.Day.EventId == null ? newEventName : null);
        }
        finally
        {
            Gate.Release();
        }
    }

    private sealed record CheckedPlan(string? Refusal, Start? Start = null, ExistingDay? Existing = null,
        string? Date = null, string? NewEventName = null);

    /// <summary>Перепроверки записи — под шлюзом и общим замком дня: дата, «дня нет», пересечение, бассейн, имя.</summary>
    private async Task<CheckedPlan> CheckUnderLockAsync(int discoveryId, DiscoveryPreviewEntry entry,
        LogligCompetitionSummaryDto candidate, string pool, SiblingDayImportRequest request, CancellationToken ct)
    {
        var start = await LoadStartAsync(discoveryId, ct);
        if (start.Refusal != null) return new(start.Refusal);
        var dates = entry.Parsed.Competitions.Select(c => c.Date).ToList();
        var refusal = SiblingDayRules.FileDateRefusal(dates, ProgrammeCheckRules.NeighbourDates(candidate), start.Missing, start.DbDays)
                      ?? await DayInDbRefusalAsync(entry.Parsed, ct)
                      ?? await OverlapRefusalAsync(start, entry.Parsed.ResultsJson, ct);
        if (refusal != null) return new(refusal);

        var date = dates[0]!.Trim();
        var existing = await LoadExistingDayAsync(start, date, ct);
        var existingPool = SiblingDayRules.NormalizePool(existing.Day.PoolType);
        if (existingPool != null && existingPool != pool)
            return new(MissingDayTexts.PoolMismatch(ParseDate(existing.Day.Date) ?? default, existingPool,
                ParseDate(date) ?? default, pool, start.DbDays));

        string? newEventName = null;
        if (existing.Day.EventId == null)
        {
            newEventName = request.EventName?.Trim();
            if (string.IsNullOrEmpty(newEventName)) return new("впишите название соревнования — общее для всех дней");
            if (newEventName.Length > 200) return new("название длиннее 200 символов — сократите");
        }
        return new(null, start, existing, date, newEventName);
    }

    /// <remarks>
    /// Не бросает: любой исход — строка аудита и текст для человека. Новый день ищется ДО сбора, и
    /// его номер уходит наружу при любом исходе сбора — фон планирует по нему шаги loglig. Сбор и
    /// перенумерация дней — под общим замком дня (<see cref="CompetitionDayLock"/>, как деление):
    /// задача могла простоять в очереди за пачкой, поэтому лежащий день и дата перечитываются.
    /// </remarks>
    public async Task<SiblingDayCompletion> CompleteAsync(SiblingDayTarget target, ImportResult result, CancellationToken ct = default)
    {
        var newDate = ParseDate(target.Date) ?? default;
        try
        {
            if (!ImportPostStepRules.Succeeded(result))
                return await FinishAsync(target, new SiblingDayCompletion(null, null, false,
                        MissingDayTexts.NotImported(newDate, FirstLine(result))),
                    $"импорт ничего не записал: {FirstLine(result)}");

            int? newDayId = null;
            try
            {
                newDayId = await db.Competitions.AsNoTracking()
                    .Where(c => result.CompetitionIds.Contains(c.Id) && c.Id != target.ExistingCompetitionId && c.Date == target.Date)
                    .Select(c => (int?)c.Id).FirstOrDefaultAsync(CancellationToken.None);
                if (newDayId is not int dayId)
                    return await FinishAsync(target, new SiblingDayCompletion(null, null, false,
                            MissingDayTexts.NotFoundAfterImport(newDate)),
                        "новый день среди записанных импортом не найден");
                return await PlaceAsync(target, dayId, newDate);
            }
            catch (Exception ex)
            {
                // Сбор мог успеть записаться (исключение — в пересчёте после commit): смотрим базу.
                db.ChangeTracker.Clear();
                logger.LogWarning(ex, "Р124: compID {OrgCompId}, loglig {LogligId} — завершение сорвалось", target.OrgCompId, target.LogligId);
                var state = newDayId is int id
                    ? await db.Competitions.AsNoTracking().Where(c => c.Id == id || c.Id == target.ExistingCompetitionId)
                        .Select(c => new { c.Id, c.EventId }).ToListAsync(CancellationToken.None)
                    : [];
                var newEvent = state.FirstOrDefault(s => s.Id == newDayId)?.EventId;
                var together = newEvent != null && state.Count == 2 && state.All(s => s.EventId == newEvent);
                var existingDates = await ExistingDatesAsync(target);
                return await FinishAsync(target, new SiblingDayCompletion(newDayId, newEvent, together,
                        together
                            ? MissingDayTexts.JoinedButRecalcFailed(newDate,
                                "--recalc-competition " + string.Join(" и --recalc-competition ", state.Select(s => s.Id).Order()))
                            : MissingDayTexts.NotJoined(newDate, existingDates, ex.Message, JoinCommand(target, newDayId))),
                    $"завершение сорвалось ({ex.Message}); новый день #{newDayId?.ToString(CultureInfo.InvariantCulture) ?? "—"}, "
                    + $"событие {newEvent?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
            }
        }
        finally
        {
            Pending.TryRemove(target.OrgCompId, out _);
        }
    }

    /// <inheritdoc />
    public async Task ImportFailedAsync(SiblingDayTarget target, string error)
    {
        Pending.TryRemove(target.OrgCompId, out _);
        await FinishAsync(target, new SiblingDayCompletion(null, null, false,
                MissingDayTexts.NotImported(ParseDate(target.Date) ?? default, error)),
            $"задача импорта упала: {error}");
    }

    /// <summary>День записан — положить его к лежащему: в его событие (перенумеровать по дате) или собрать оба.</summary>
    private async Task<SiblingDayCompletion> PlaceAsync(SiblingDayTarget target, int newDayId, DateOnly newDate)
    {
        var existing = await db.Competitions.AsNoTracking().Where(c => c.Id == target.ExistingCompetitionId)
            .Select(c => new { c.EventId, c.Date }).FirstOrDefaultAsync(CancellationToken.None);
        var newDayEvent = await db.Competitions.AsNoTracking().Where(c => c.Id == newDayId)
            .Select(c => c.EventId).FirstAsync(CancellationToken.None);
        var existingDates = await ExistingDatesAsync(target);

        if (target.EventId is int ev)
        {
            if (newDayEvent != ev)
                return await FinishAsync(target, new SiblingDayCompletion(newDayId, newDayEvent, false,
                        MissingDayTexts.NotJoined(newDate, existingDates, $"день не попал в событие #{ev}",
                            $"проверить событие #{ev} и день #{newDayId}")),
                    $"новый день #{newDayId} не в событии #{ev}");
            await RenumberDaysAsync(ev);
            return await FinishAsync(target, new SiblingDayCompletion(newDayId, ev, true, await DoneTextAsync(ev)),
                $"новый день #{newDayId} в событии #{ev}, дни перенумерованы по дате");
        }

        // Задача могла простоять в очереди: лежащий день тем временем попал в событие, удалён или эта
        // дата появилась у старта другим путём — тогда не собираем, а говорим как есть.
        string? changed = existing == null ? $"день #{target.ExistingCompetitionId} удалён"
            : existing.EventId is int now ? $"день {existing.Date} тем временем попал в событие #{now}"
            : await db.Competitions.AsNoTracking().AnyAsync(c => c.Id != newDayId && c.Id != target.ExistingCompetitionId
                    && c.Date == target.Date
                    && (c.OrgCompId == target.OrgCompId || (c.Event != null && c.Event.OrgCompId == target.OrgCompId)),
                CancellationToken.None)
                ? $"у старта уже есть другой день {target.Date}"
                : null;
        if (changed != null)
            return await FinishAsync(target, new SiblingDayCompletion(newDayId, null, false,
                    MissingDayTexts.NotJoined(newDate, existingDates, changed, JoinCommand(target, newDayId))),
                $"новый день #{newDayId} не собран: {changed}");

        // Лежащий день одиночный: штатный сбор (как --join-days у 488) с общим именем; замок — внутри.
        var join = await joiner.JoinAsync([target.ExistingCompetitionId, newDayId], apply: true,
            CancellationToken.None, target.NewEventName);
        return await FinishAsync(target, join.Applied
                ? new SiblingDayCompletion(newDayId, join.EventId, true, await DoneTextAsync(join.EventId!.Value))
                : new SiblingDayCompletion(newDayId, null, false,
                    MissingDayTexts.NotJoined(newDate, existingDates, join.Refusal ?? "сбор отказал", JoinCommand(target, newDayId))),
            join.Applied
                ? $"событие #{join.EventId} «{join.Name}»: " + string.Join(", ", join.Days.Select(d => $"день {d.DayNumber} — #{d.CompetitionId} ({Fmt(d.Date)})"))
                  + (join.Differences.Count > 0 ? ". Расходятся настройки: " + string.Join("; ", join.Differences) : "")
                : $"новый день #{newDayId} в событие не собран: {join.Refusal}");
    }

    /// <summary>
    /// Импорт в событие даёт новому дню номер max+1, а недостающий день бывает и раньше лежащих —
    /// номера дней события выстраиваются по дате (как у сбора). Под общим замком дня.
    /// </summary>
    private async Task RenumberDaysAsync(int eventId)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(CancellationToken.None);
            var ids = await db.Competitions.Where(c => c.EventId == eventId).Select(c => c.Id).ToListAsync(CancellationToken.None);
            await CompetitionDayLock.AcquireAsync(db, ids, CancellationToken.None);
            var days = await db.Competitions.Where(c => c.EventId == eventId).ToListAsync(CancellationToken.None);
            var n = 1;
            foreach (var d in days.OrderBy(d => ParseDate(d.Date) ?? DateOnly.MaxValue).ThenBy(d => d.Id))
                d.DayNumber = n++;
            await db.SaveChangesAsync(CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
        });
    }

    private async Task<List<DateOnly>> ExistingDatesAsync(SiblingDayTarget target) =>
        (await db.Competitions.AsNoTracking().Where(c => c.Id == target.ExistingCompetitionId)
            .Select(c => c.Date).ToListAsync(CancellationToken.None)).Select(ParseDate).OfType<DateOnly>().ToList();

    /// <summary>Подсказка «доделать руками»: сбор с тем же общим именем, что видел человек.</summary>
    internal static string JoinCommand(SiblingDayTarget t, int? newDayId) =>
        $"--join-days {t.ExistingCompetitionId},{newDayId?.ToString(CultureInfo.InvariantCulture) ?? "<новый день>"}"
        + (t.NewEventName is { } name ? $" --name \"{name.Replace("\"", "\\\"")}\"" : "") + " --apply";

    private static string FirstLine(ImportResult r) =>
        (r.ErrorMessages.FirstOrDefault() ?? r.Message ?? "").Split('\n')[0].Trim() is { Length: > 0 } s ? s : "причина неизвестна";

    /// <summary>«Готово. Теперь у соревнования два дня: 06.12 (1555 строк) и 07.12 (1784 строки).»</summary>
    private async Task<string> DoneTextAsync(int eventId)
    {
        var days = await db.Competitions.AsNoTracking().Where(c => c.EventId == eventId)
            .Select(c => new { c.Date, Rows = db.Results.Count(r => r.CompetitionId == c.Id) })
            .ToListAsync(CancellationToken.None);
        return MissingDayTexts.Done(days.Select(d => (ParseDate(d.Date) ?? default, d.Rows)).ToList());
    }

    /// <param name="technical">Для аудита: номера дней и события (человеку — <c>c.Message</c>).</param>
    private async Task<SiblingDayCompletion> FinishAsync(SiblingDayTarget t, SiblingDayCompletion c, string technical)
    {
        await audit.LogAsync(AuditAction, "Competition", (c.CompetitionId ?? t.ExistingCompetitionId).ToString(CultureInfo.InvariantCulture),
            $"Затянуть этот день: compID {t.OrgCompId}, loglig № {t.LogligId} — {technical}. {c.Message} (клик: {t.Actor})",
            new
            {
                Stage = "imported", t.OrgCompId, SiblingLogligId = t.LogligId, t.Date, t.ExistingCompetitionId,
                NewCompetitionId = c.CompetitionId, c.EventId, c.Joined, t.DiscoveryId, ClickedBy = t.Actor,
            }, CancellationToken.None);
        logger.LogInformation("Р124: compID {OrgCompId}, loglig {LogligId} — {Message}", t.OrgCompId, t.LogligId, c.Message);
        return c;
    }

    // ── Старт и его дни ──────────────────────────────────────────────────────

    private sealed record Start(string? Refusal, int OrgCompId, int? LogligId, DateOnly SiteStart, DateOnly SiteEnd,
        List<Competition> Days, List<DateOnly> DbDays, IReadOnlyList<DateOnly> Missing);

    /// <summary>Запись федерации и дни её compID в базе — тот же круг, что у вердикта Р123 (без чужого штампа).</summary>
    private async Task<Start> LoadStartAsync(int discoveryId, CancellationToken ct)
    {
        var row = await db.DiscoveredCompetitions.AsNoTracking().Where(d => d.Id == discoveryId)
            .Select(d => new { d.OrgCompId, d.LogligId, d.DateStart, d.DateEnd }).FirstOrDefaultAsync(ct);
        if (row == null) return new Start($"строка входящих #{discoveryId} не найдена", 0, null, default, default, [], [], []);
        var days = (await CompetitionIdentity.ResolveDaysAsync(db, row.OrgCompId, ct))
            .Where(d => d.OrgCompId is null || d.OrgCompId == row.OrgCompId).ToList();
        var dbDays = days.Select(d => ParseDate(d.Date)).OfType<DateOnly>().Distinct().ToList();
        var siteStart = DateOnly.FromDateTime(row.DateStart);
        var siteEnd = DateOnly.FromDateTime(row.DateEnd);
        var missing = ProgrammeCheckRules.MissingSiteDays(siteStart, siteEnd, dbDays);
        string? refusal = days.Count == 0 ? "у нас нет ни одного дня этого соревнования — затяните его обычной кнопкой «Затянуть»"
            : row.LogligId == null ? "у федерации не указана ссылка на результаты"
            : null;
        return new Start(refusal, row.OrgCompId, row.LogligId, siteStart, siteEnd, days, dbDays, missing);
    }

    /// <summary>
    /// Кандидат по-прежнему годится (<see cref="SiblingDayRules.CandidateRefusal"/>): заголовки привязанного
    /// и соседа заново с loglig — два запроса, int-номера, URL у провайдера, с его паузой и таймаутом.
    /// </summary>
    private async Task<(string? Refusal, LogligCompetitionSummaryDto? Candidate)> VerifyCandidateAsync(
        Start start, int logligId, CancellationToken ct)
    {
        LogligCompetitionSummaryDto? bound, candidate;
        try
        {
            bound = start.LogligId is int b ? await loglig.FetchCompetitionSummaryAsync(b, ct) : null;
            candidate = await loglig.FetchCompetitionSummaryAsync(logligId, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ("на loglig такого соревнования нет", null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Р124: заголовки loglig {Bound}/{Sibling} не получены", start.LogligId, logligId);
            return ($"loglig не ответил — попробуйте позже ({ex.Message})", null);
        }
        return (SiblingDayRules.CandidateRefusal(start.LogligId, bound?.Title, logligId, candidate,
            start.SiteStart, start.SiteEnd, start.DbDays), candidate);
    }

    /// <summary>Такой день (имя файла + дата) уже лежит в базе — хоть и не в днях этого compID.</summary>
    private async Task<string?> DayInDbRefusalAsync(ParsedCompetition parsed, CancellationToken ct)
    {
        foreach (var c in parsed.Competitions)
        {
            var name = c.Competition.Trim();
            var date = c.Date.Trim();
            var found = await db.Competitions.AsNoTracking()
                .Where(x => x.Date == date && (x.Name == name || x.SubName == name))
                .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
            if (found is int id) return $"день {date} у нас уже есть (соревнование «{name}»)";
        }
        return null;
    }

    private sealed record ExistingDay(Competition Day, IReadOnlyList<string> CategoryKeys);

    /// <summary>Лежащий день, от которого берутся значения по умолчанию, — ближайший по дате к новому.</summary>
    private async Task<ExistingDay> LoadExistingDayAsync(Start start, string date, CancellationToken ct)
    {
        var target = ParseDate(date) ?? default;
        var day = start.Days
            .OrderBy(d => Math.Abs((ParseDate(d.Date) ?? default).DayNumber - target.DayNumber))
            .ThenBy(d => d.Id).First();
        var keys = await db.CategoryCompetitions.AsNoTracking()
            .Where(cc => cc.CompetitionId == day.Id)
            .Join(db.Categories.AsNoTracking(), cc => cc.CategoryId, c => c.Id, (cc, c) => c.Key)
            .Distinct().ToListAsync(ct);
        return new ExistingDay(day, keys);
    }

    /// <summary>
    /// Значения по умолчанию — от лежащего дня: флаги, категории, бассейн (при расхождении с
    /// регламентом — пусто и оба в обосновании). Человек правит их в превью.
    /// </summary>
    private static CompetitionFlagSuggestion DefaultsFromDay(CompetitionFlagSuggestion? f, ExistingDay e, string? pool, bool conflict)
    {
        var d = e.Day;
        var from = $"как у лежащего дня #{d.Id} ({d.Date})";
        var reasons = new Dictionary<string, string>(f?.Reasons ?? new Dictionary<string, string>())
        {
            ["isAward"] = $"{from}: {(d.IsAward ? "да" : "нет")}",
            ["isChampionship"] = $"{from}: {(d.IsChampionship ? "да" : "нет")}",
            ["isMasters"] = $"{from}: {(d.IsMasters ? "да" : "нет")}",
            ["clubPointsDisabled"] = $"{from}: {(d.ClubPointsDisabled ? "да" : "нет")}",
            ["poolType"] = conflict
                ? $"⚠ как у дня {d.Date} ({d.PoolType}), хотя регламент нового дня — {f?.PoolType}"
                : SiblingDayRules.NormalizePool(d.PoolType) != null ? $"{from}: {d.PoolType}" : f?.Reasons.GetValueOrDefault("poolType") ?? "не задан",
        };
        if (e.CategoryKeys.Count > 0) reasons["categories"] = $"{from}: {string.Join(", ", e.CategoryKeys)}";
        return new CompetitionFlagSuggestion(d.IsAward, d.IsChampionship, d.IsMasters, d.ClubPointsDisabled, pool,
            f?.RegulationUrl, reasons, e.CategoryKeys.Count > 0 ? e.CategoryKeys : f?.CategoryKeys ?? [],
            conflict ? null : f?.PoolTypeSource, f?.VenuePool);
    }

    // ── Пересечение заплывов ─────────────────────────────────────────────────

    private async Task<string?> OverlapRefusalAsync(Start start, string resultsJson, CancellationToken ct) =>
        SiblingDayRules.OverlapRefusal(await OverlapAsync(start, resultsJson, ct));

    /// <summary>
    /// Личные строки файла против личных строк всех дней старта (<see cref="SiblingDayRules.OverlapCheck"/>:
    /// заплыв+дорожка, без них — время, нет и времени — сверять нечем).
    /// </summary>
    private async Task<OverlapResult> OverlapAsync(Start start, string resultsJson, CancellationToken ct)
    {
        var file = JsonImportService.DeserializeResultItems(resultsJson)
            .Where(i => i.IsRelay != true)
            .Select(i => new SiblingSwim(SiblingDayRules.Person(i.LastName, i.FirstName), i.BirthYear ?? 0,
                JsonImportService.NormalizeStyleName(i.EventStyleName), (i.EventStyleLen ?? "").Trim(), i.Heat ?? 0, i.Lane ?? 0,
                i.TimeFail ? null : JsonImportService.ParseTimeToMs(i.Time)))
            .ToList();
        var dayIds = start.Days.Select(d => d.Id).ToList();
        var inDb = await db.Results.AsNoTracking()
            .Where(r => dayIds.Contains(r.CompetitionId) && r.RelayId == null)
            .Select(r => new
            {
                r.Swimmer!.LastName, r.Swimmer.FirstName, r.Swimmer.BirthYear, Style = r.Style!.Name, r.Distance, r.Heat, r.Lane,
                r.CompetitionDate, r.TimeMillisecond, r.TimeFail,
            })
            .ToListAsync(ct);
        var dbRows = inDb.Select(r => new SiblingSwim(SiblingDayRules.Person(r.LastName, r.FirstName), r.BirthYear,
                r.Style, r.Distance.Trim(), r.Heat, r.Lane, r.TimeFail ? null : r.TimeMillisecond, DateOnly.FromDateTime(r.CompetitionDate)))
            .ToList();
        return SiblingDayRules.OverlapCheck(file, dbRows);
    }

    private static string Fmt(DateOnly d) => d.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static DateOnly? ParseDate(string? date) =>
        DateOnly.TryParseExact(date?.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
