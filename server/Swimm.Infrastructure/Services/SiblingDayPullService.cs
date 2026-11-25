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
        var overlapRows = (await OverlapAsync(start, p.Parsed.ResultsJson, ct)).Overlap.Count;
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
            conflict ? MissingDayTexts.PoolConflict(ParseDate(existing.Day.Date) ?? default, existing.Day.PoolType, p.Flags?.PoolType) : null);
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

            // База могла измениться, пока шли запросы: перечитываем всё под шлюзом.
            start = await LoadStartAsync(discoveryId, ct);
            if (start.Refusal != null) return SiblingDayEnqueue.Refused(start.Refusal);
            var dates = entry.Parsed.Competitions.Select(c => c.Date).ToList();
            var refusal = SiblingDayRules.FileDateRefusal(dates, ProgrammeCheckRules.NeighbourDates(candidate!), start.Missing, start.DbDays)
                          ?? await DayInDbRefusalAsync(entry.Parsed, ct)
                          ?? await OverlapRefusalAsync(start, entry.Parsed.ResultsJson, ct);
            if (refusal != null) return SiblingDayEnqueue.Refused(refusal);

            var date = dates[0]!.Trim();
            var existing = await LoadExistingDayAsync(start, date, ct);
            var existingPool = SiblingDayRules.NormalizePool(existing.Day.PoolType);
            if (existingPool != null && existingPool != pool)
                return SiblingDayEnqueue.Refused(
                    $"у дня {existing.Day.Date} бассейн {MissingDayTexts.Pool(existingPool)}, а вы выбрали {MissingDayTexts.Pool(pool)} — у дней одного "
                    + "соревнования бассейн один. Если ошибся уже затянутый день — сначала поправьте его через «Изменить».");

            string? newEventName = null;
            if (existing.Day.EventId == null)
            {
                newEventName = request.EventName?.Trim();
                if (string.IsNullOrEmpty(newEventName)) return SiblingDayEnqueue.Refused("впишите название соревнования — общее для всех дней");
                if (newEventName.Length > 200) return SiblingDayEnqueue.Refused("название длиннее 200 символов — сократите");
            }

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

    public async Task<SiblingDayCompletion> CompleteAsync(SiblingDayTarget target, ImportResult result, CancellationToken ct = default)
    {
        try
        {
            if (!ImportPostStepRules.Succeeded(result))
                return new SiblingDayCompletion(null, null, false, "день не записан — событие не собиралось");

            var newDay = await db.Competitions.AsNoTracking()
                .Where(c => result.CompetitionIds.Contains(c.Id) && c.Id != target.ExistingCompetitionId && c.Date == target.Date)
                .Select(c => new { c.Id, c.EventId })
                .FirstOrDefaultAsync(CancellationToken.None);
            if (newDay == null)
                return await FinishAsync(target, new SiblingDayCompletion(null, null, false,
                    $"⚠ День {target.Date} записан, но найти его после записи не вышло — дни не собраны. Попросите агента проверить."),
                    "новый день среди записанных импортом не найден");

            var newDate = ParseDate(target.Date) ?? default;
            var existingDates = (await db.Competitions.AsNoTracking().Where(c => c.Id == target.ExistingCompetitionId)
                .Select(c => c.Date).ToListAsync(CancellationToken.None)).Select(ParseDate).OfType<DateOnly>().ToList();

            if (target.EventId is int ev)
                return await FinishAsync(target, newDay.EventId == ev
                    ? new SiblingDayCompletion(newDay.Id, ev, true, await DoneTextAsync(ev))
                    : new SiblingDayCompletion(newDay.Id, newDay.EventId, false,
                        MissingDayTexts.NotJoined(newDate, existingDates, $"день не попал в событие #{ev}",
                            $"проверить события #{ev} и дня #{newDay.Id}")),
                    $"новый день #{newDay.Id}, событие #{ev}");

            // Лежащий день одиночный: собираем штатным сбором (как --join-days у 488) с общим именем.
            var join = await joiner.JoinAsync([target.ExistingCompetitionId, newDay.Id], apply: true,
                CancellationToken.None, target.NewEventName);
            return await FinishAsync(target, join.Applied
                ? new SiblingDayCompletion(newDay.Id, join.EventId, true, await DoneTextAsync(join.EventId!.Value))
                : new SiblingDayCompletion(newDay.Id, null, false,
                    MissingDayTexts.NotJoined(newDate, existingDates, join.Refusal ?? "сбор отказал",
                        $"--join-days {target.ExistingCompetitionId},{newDay.Id} --apply")),
                join.Applied
                    ? $"событие #{join.EventId} «{join.Name}»: " + string.Join(", ", join.Days.Select(d => $"день {d.DayNumber} — #{d.CompetitionId} ({Fmt(d.Date)})"))
                      + (join.Differences.Count > 0 ? ". Расходятся настройки: " + string.Join("; ", join.Differences) : "")
                    : $"новый день #{newDay.Id} в событие не собран: {join.Refusal}");
        }
        finally
        {
            Pending.TryRemove(target.OrgCompId, out _);
        }
    }

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
                ? $"⚠ расходится: у лежащего дня #{d.Id} — {d.PoolType}, регламент соседа — {f?.PoolType}. Выберите сами"
                : SiblingDayRules.NormalizePool(d.PoolType) != null ? $"{from}: {d.PoolType}" : f?.Reasons.GetValueOrDefault("poolType") ?? "не задан",
        };
        if (e.CategoryKeys.Count > 0) reasons["categories"] = $"{from}: {string.Join(", ", e.CategoryKeys)}";
        return new CompetitionFlagSuggestion(d.IsAward, d.IsChampionship, d.IsMasters, d.ClubPointsDisabled, pool,
            f?.RegulationUrl, reasons, e.CategoryKeys.Count > 0 ? e.CategoryKeys : f?.CategoryKeys ?? [],
            conflict ? null : f?.PoolTypeSource, f?.VenuePool);
    }

    // ── Пересечение заплывов ─────────────────────────────────────────────────

    private async Task<string?> OverlapRefusalAsync(Start start, string resultsJson, CancellationToken ct)
    {
        var (overlap, fileRows, inDay) = await OverlapAsync(start, resultsJson, ct);
        return SiblingDayRules.OverlapRefusal(overlap, fileRows, inDay);
    }

    /// <summary>Личные строки файла против личных строк всех дней старта — тем же ключом.</summary>
    /// <returns>Совпавшие ключи, личных строк файла и день, где лежат совпавшие (если он один).</returns>
    private async Task<(IReadOnlyList<SiblingSwimKey> Overlap, int FileRows, DateOnly? InDay)> OverlapAsync(
        Start start, string resultsJson, CancellationToken ct)
    {
        var file = JsonImportService.DeserializeResultItems(resultsJson)
            .Where(i => i.IsRelay != true)
            .Select(i => new SiblingSwimKey(SiblingDayRules.Person(i.LastName, i.FirstName), i.BirthYear ?? 0,
                JsonImportService.NormalizeStyleName(i.EventStyleName), (i.EventStyleLen ?? "").Trim(), i.Heat ?? 0, i.Lane ?? 0))
            .ToList();
        var dayIds = start.Days.Select(d => d.Id).ToList();
        var inDb = await db.Results.AsNoTracking()
            .Where(r => dayIds.Contains(r.CompetitionId) && r.RelayId == null)
            .Select(r => new { r.Swimmer!.LastName, r.Swimmer.FirstName, r.Swimmer.BirthYear, Style = r.Style!.Name, r.Distance, r.Heat, r.Lane, r.CompetitionDate })
            .ToListAsync(ct);
        var keyed = inDb.Select(r => (Key: new SiblingSwimKey(SiblingDayRules.Person(r.LastName, r.FirstName), r.BirthYear,
            r.Style, r.Distance.Trim(), r.Heat, r.Lane), Day: DateOnly.FromDateTime(r.CompetitionDate))).ToList();
        var overlap = SiblingDayRules.Overlap(file, keyed.Select(k => k.Key));
        var hit = overlap.ToHashSet();
        var days = keyed.Where(k => hit.Contains(k.Key)).Select(k => k.Day).Distinct().ToList();
        return (overlap, file.Count, days.Count == 1 ? days[0] : null);
    }

    private static string Fmt(DateOnly d) => d.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static DateOnly? ParseDate(string? date) =>
        DateOnly.TryParseExact(date?.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
