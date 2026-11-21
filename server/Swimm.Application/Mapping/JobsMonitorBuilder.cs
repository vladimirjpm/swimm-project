using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>Всё, из чего собирается ответ мониторинга; время и состояние процесса — снаружи.</summary>
/// <param name="Log">Лёгкий журнал (<c>IJobRunStore.ListLogLightAsync</c>), свежие первыми.</param>
/// <param name="Journal"><c>prod</c> — журнал прод-базы (ProdAdmin) | <c>local</c>.</param>
/// <param name="LocalProcess">Подпись этого процесса («среда@машина#pid», <c>JobRunOrigin</c>).</param>
/// <param name="ProdTickAt">Последний тик <c>training-reminders</c> (<c>Sys_JobRuns.LastAttemptAt</c>) — «пульс» прода.</param>
public sealed record JobsMonitorInput(
    DateTime Now,
    string Environment,
    string Journal,
    IReadOnlyList<JobCard> Cards,
    IReadOnlyList<JobRunLogRow> Log,
    JobsSummary Summary,
    IReadOnlyList<ResultsAutoWaitingRow> Waiting,
    string LocalProcess,
    DateTime LocalStartedAt,
    DateTime? ProdTickAt,
    RecordsCheckTile? RecordsSources,
    IReadOnlyList<ImportHistoryDto> RecentImports);

/// <summary>
/// Мониторинг фоновых служб (GET /api/admin/jobs/monitor, docs/admin-pages/jobs.md «Контракт
/// мониторинга»): статус из шести, зона, окно таймлайна, план, исходы, панели. Чистые функции —
/// проверяются тестами без базы (<c>JobsMonitorBuilderTests</c>).
/// </summary>
public static class JobsMonitorBuilder
{
    /// <summary>Окно таймлайна (решение Влада 20.11.2026: только 36 ч).</summary>
    public const int WindowHours = 36;

    /// <summary>Сколько исходов в полоске и строк истории в панели деталей.</summary>
    public const int RecentRuns = 15;

    /// <summary>Строк «Ближайшие запуски».</summary>
    public const int UpcomingRows = 8;

    /// <summary>Потолок плановых меток на дорожку (шаг 1 ч × 18 ч вперёд — 18; запас).</summary>
    public const int MaxPlanned = 48;

    /// <summary>
    /// Статус по бейджу нынешней карточки — таблица «Статусы» передачи: danger → упала, warning →
    /// просрочена (в том числе «ещё не запускалась», «молчит», «прод недоступен», «ждёт первого
    /// пинга»), info → идёт, success → в норме (у очереди — «ждёт задания»), neutral → выключена.
    /// Бейдж уже решён по приоритетам (идёт &gt; выключена &gt; упала &gt; просрочена), здесь не повторяем.
    /// </summary>
    public static string Status(string kind, string badgeVariant) => badgeVariant switch
    {
        "danger" => JobStatuses.Failed,
        "warning" => JobStatuses.Overdue,
        "info" => JobStatuses.Running,
        "success" => kind == JobKinds.Queue ? JobStatuses.Idle : JobStatuses.Ok,
        _ => JobStatuses.Off,
    };

    /// <summary>
    /// Зона службы. Прод — задача не запущена здесь, но этот процесс следит за её прод-журналом
    /// (то самое условие «не запущена — работает на ПРОДЕ…»). В Development зоны прод нет.
    /// </summary>
    public static string Zone(bool processOn, bool watchingProd, bool isDevelopment) =>
        !isDevelopment && !processOn && watchingProd ? JobZones.Prod : JobZones.Local;

    /// <summary>Откуда шёл прогон по подписи «среда@машина#pid»; null — подписи нет (строки до 20.11.2026).</summary>
    public static string? OriginCode(string? runBy)
    {
        if (string.IsNullOrWhiteSpace(runBy)) return null;
        var at = runBy.IndexOf('@');
        return (at > 0 ? runBy[..at] : runBy) switch
        {
            "Production" => JobOrigins.Prod,
            "ProdAdmin" => JobOrigins.ProdAdmin,
            "Development" => JobOrigins.Local,
            _ => JobOrigins.Other,
        };
    }

    /// <summary>
    /// Скользящее окно 36 ч: начало — час «сейчас» минус 18 ч. «Сейчас» всегда посередине (не у
    /// края), граница суток всегда видна (окно длиннее суток).
    /// </summary>
    public static (DateTime From, DateTime To) Window(DateTime now)
    {
        var hour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        var from = hour.AddHours(-WindowHours / 2);
        return (from, from.AddHours(WindowHours));
    }

    /// <summary>
    /// Плановые моменты после <paramref name="now"/> до <paramref name="to"/>: первый — «пора»
    /// (<paramref name="nextAt"/>; повтор после ошибки — тоже он), дальше шагом интервала (как будто
    /// каждый прогон удался). Пора уже прошла или не известна (не запускалась) — отсчёт от «сейчас».
    /// </summary>
    public static IReadOnlyList<DateTime> Planned(DateTime? nextAt, TimeSpan interval, DateTime now, DateTime to)
    {
        if (interval <= TimeSpan.Zero) return [];
        var t = nextAt is { } n && n > now ? n : now + interval;
        var result = new List<DateTime>();
        while (t <= to && result.Count < MaxPlanned)
        {
            result.Add(t);
            t += interval;
        }
        return result;
    }

    /// <summary>Исходы последних прогонов, свежие первыми; идущий — первым.</summary>
    public static IReadOnlyList<string> Outcomes(IEnumerable<JobRunLogRow> historyNewestFirst, bool running) =>
        (running ? [JobRunOutcomes.Running] : Array.Empty<string>())
            .Concat(historyNewestFirst.Select(r => r.Success ? JobRunOutcomes.Success : JobRunOutcomes.Failed))
            .Take(RecentRuns)
            .ToList();

    /// <summary>Шесть счётчиков сводки по статусам служб; «ждёт задания» входит в «в норме».</summary>
    public static JobsCounters Counters(IEnumerable<string> statuses, int waiting)
    {
        var list = statuses.ToList();
        return new JobsCounters(
            list.Count(s => s == JobStatuses.Failed),
            list.Count(s => s == JobStatuses.Overdue),
            list.Count(s => s == JobStatuses.Running),
            waiting,
            list.Count(s => s is JobStatuses.Ok or JobStatuses.Idle),
            list.Count(s => s == JobStatuses.Off));
    }

    public static JobHistoryEntry History(JobRunLogRow r) => new(
        r.Id, r.StartedAt, r.FinishedAt, DurationSeconds(r.StartedAt, r.FinishedAt),
        r.Trigger, JobTimeText.Trigger(r.Trigger), OriginCode(r.RunBy), JobTimeText.Origin(r.RunBy), r.RunBy,
        r.Success, r.Summary, r.Error, r.HasDetails);

    /// <summary>Служба целиком: структурные поля карточки + история и окно из журнала.</summary>
    public static JobMonitorItem Item(JobCard card, IReadOnlyList<JobRunLogRow> jobLogNewestFirst, DateTime now, DateTime from, DateTime to)
    {
        var m = card.Monitor;
        var status = Status(card.Kind, card.Badge.Variant);
        var history = jobLogNewestFirst.Take(RecentRuns).Select(History).ToList();
        var last = history.FirstOrDefault();
        var claimLive = m.Claim is { Live: true };

        IReadOnlyList<JobWindowRun> windowRuns;
        IReadOnlyList<string> outcomes;
        if (card.Kind == JobKinds.Schedule)
        {
            var runs = jobLogNewestFirst
                .Where(r => r.FinishedAt >= from && r.StartedAt <= to)
                .Select(r => new JobWindowRun(r.Id, r.StartedAt, r.FinishedAt,
                    r.Success ? JobRunOutcomes.Success : JobRunOutcomes.Failed, r.Trigger, OriginCode(r.RunBy), r.RunBy))
                .ToList();
            if (claimLive)
                runs.Insert(0, new JobWindowRun(null, m.Claim!.Since, null, JobRunOutcomes.Running,
                    JobTriggers.Schedule, m.Claim.Origin, m.Claim.By));
            windowRuns = [.. runs.OrderBy(r => r.StartedAt)];
            outcomes = Outcomes(jobLogNewestFirst, claimLive);
        }
        else
        {
            windowRuns = [.. m.QueueRuns.Where(r => (r.FinishedAt ?? now) >= from).OrderBy(r => r.StartedAt)];
            outcomes = [.. m.QueueRuns.Take(RecentRuns).Select(r => r.Outcome)];
        }

        var planned = card.Active && status != JobStatuses.Off && m.TimelineMode == JobTimelineModes.Runs && m.Interval is { } interval
            ? Planned(m.NextAt, interval, now, to)
            : [];

        var overdueSince = status == JobStatuses.Overdue && m.NextAt is { } due && due < now ? due : (DateTime?)null;

        return new JobMonitorItem
        {
            Key = card.Key,
            Title = card.Title,
            Kind = card.Kind,
            Zone = m.Zone,
            Status = status,
            Badge = card.Badge,
            StatusText = card.Status,
            Alert = card.Alert,
            Active = card.Active,
            What = card.What,
            Service = card.Service,
            IntervalSeconds = m.Interval is { } i ? (int)i.TotalSeconds : null,
            IntervalKey = m.IntervalKey,
            Toggle = m.Toggle,
            Settings = m.Settings,
            LastRunAt = m.LastRunAt,
            LastSuccessAt = m.LastSuccessAt,
            LastRun = last,
            LastDurationSeconds = last?.DurationSeconds,
            NextAt = m.NextAt,
            NextIsRetry = m.NextIsRetry,
            NextApprox = m.NextApprox,
            DueNow = card.Active && status != JobStatuses.Off && !claimLive && card.Kind == JobKinds.Schedule
                     && (m.NextAt is null || m.NextAt <= now),
            OverdueSince = overdueSince,
            OverdueSeconds = overdueSince is { } since ? (int)(now - since).TotalSeconds : null,
            Claim = m.Claim,
            LastError = m.LastError,
            LastSummary = m.LastSummary ?? (last is null ? null : last.Success ? last.Summary : last.Error),
            StateSince = m.StateSince ?? (claimLive ? m.Claim!.Since : null),
            RecentOutcomes = outcomes,
            TimelineMode = m.TimelineMode,
            WindowRuns = windowRuns,
            Planned = planned,
            Progress = m.Progress,
            Queue = m.Queue,
            Facts = card.Facts,
            Actions = m.Actions,
            History = history,
        };
    }

    /// <summary>
    /// «Требует внимания»: упавшие, брошенные заявки, просроченные, затем строки results-auto «ждёт
    /// тебя» — с объяснением одной строкой и ссылкой.
    /// </summary>
    public static IReadOnlyList<JobAttentionItem> Attention(
        IReadOnlyList<JobMonitorItem> items, IReadOnlyList<ResultsAutoWaitingRow> waiting, DateTime now)
    {
        var list = new List<JobAttentionItem>();
        list.AddRange(items.Where(i => i.Status == JobStatuses.Failed).Select(i =>
            new JobAttentionItem(i.Key, i.Title, i.Zone, JobAttentionKinds.Failed, i.Status,
                i.LastError is { } e ? $"Упала: {e}" : i.StatusText, i.LastRunAt, null)));
        list.AddRange(items.Where(i => i.Claim is { Live: false }).Select(i =>
            new JobAttentionItem(i.Key, i.Title, i.Zone, JobAttentionKinds.AbandonedClaim, i.Status,
                $"Брошенная заявка: {i.Claim!.By ?? "?"} взял прогон {JobTimeText.Ago(i.Claim.Since, now)} и не снял — " +
                "процесс убит посреди прогона; следующий прогон заберёт её сам", i.Claim.Since, null)));
        list.AddRange(items.Where(i => i.Status == JobStatuses.Overdue).Select(i =>
            new JobAttentionItem(i.Key, i.Title, i.Zone, JobAttentionKinds.Overdue, i.Status,
                i.OverdueSince is { } since
                    ? $"Пора с {JobTimeText.Format(since)}, прогона нет уже {JobTimeText.Human(now - since)}" +
                      (i.LastSuccessAt is { } ok ? $". Последний успех — {JobTimeText.Ago(ok, now)}" : "")
                    : Capitalize(i.StatusText),
                i.OverdueSince, null)));

        var resultsAuto = items.FirstOrDefault(i => i.Key == JobNames.ResultsAuto);
        list.AddRange(waiting.Select(w =>
            new JobAttentionItem(JobNames.ResultsAuto, resultsAuto?.Title ?? "Результаты прошедших стартов",
                resultsAuto?.Zone ?? JobZones.Local, JobAttentionKinds.Waiting, "waiting",
                WaitingText(w),
                w.CheckedAt,
                // Фильтр autopull-blocked показывает только вердикт blocked — застрявшую ищем по имени.
                w.Verdict == ResultsAutoVerdicts.Blocked
                    ? $"/Admin/Competitions?filter={AutoPullFilters.Blocked}&q={Uri.EscapeDataString(w.Name)}"
                    : $"/Admin/Competitions?q={Uri.EscapeDataString(w.Name)}")));
        return list;
    }

    /// <summary>
    /// Текст строки «ждёт тебя». У застрявшей (<c>ResultsAutoWaiting.IsStuck</c>) — сколько дней
    /// прошло и что именно не дозрело: сама задача этот вердикт уже не сменит.
    /// </summary>
    private static string WaitingText(ResultsAutoWaitingRow w)
    {
        var reason = w.Reason ?? "причина не записана";
        var head = $"Старт «{w.Name}» #{w.OrgCompId}";
        return w.Verdict switch
        {
            ResultsAutoVerdicts.NotFinished =>
                $"{head} прошёл {w.DateEnd:dd.MM}, а результаты до сих пор неполные — реши, брать ли как есть: {reason}",
            ResultsAutoVerdicts.Empty =>
                $"{head} прошёл {w.DateEnd:dd.MM}, а протокола результатов до сих пор нет: {reason}",
            _ => $"{head} не взят автоматически: {reason}",
        };
    }

    /// <summary>«Идёт сейчас»: с какого момента, кто запустил и прогресс (null — без оценки).</summary>
    public static IReadOnlyList<JobRunningItem> Running(IReadOnlyList<JobMonitorItem> items) =>
        [.. items.Where(i => i.Status == JobStatuses.Running)
            .Select(i => new JobRunningItem(i.Key, i.Title, i.Zone, i.Claim is { Live: true } c ? c.Since : i.StateSince,
                i.Claim is { Live: true } c2 ? c2.By : null, i.Progress))
            .OrderBy(r => r.Since ?? DateTime.MaxValue)];

    /// <summary>«Ближайшие запуски»: работающие службы с моментом «пора» в будущем, по возрастанию.</summary>
    public static IReadOnlyList<JobUpcomingItem> Upcoming(IReadOnlyList<JobMonitorItem> items, DateTime now) =>
        [.. items.Where(i => i.Active && i.Status != JobStatuses.Off && i.NextAt is { } at && at > now)
            .OrderBy(i => i.NextAt)
            .Take(UpcomingRows)
            .Select(i => new JobUpcomingItem(i.Key, i.Title, i.Zone, i.NextAt!.Value, i.NextIsRetry, i.NextApprox))];

    /// <summary>
    /// Зоны в порядке прод → local, только те, где есть службы. Пульс прода — подпись процесса из
    /// последней строки журнала с RunBy «Production@…» и последний тик напоминаний.
    /// </summary>
    public static IReadOnlyList<JobZoneInfo> Zones(
        IReadOnlyList<JobMonitorItem> items, IReadOnlyList<JobRunLogRow> log,
        string localProcess, DateTime localStartedAt, DateTime? prodTickAt)
    {
        var zones = new List<JobZoneInfo>();
        var prod = items.Where(i => i.Zone == JobZones.Prod).Select(i => i.Key).ToList();
        if (prod.Count > 0)
        {
            var prodProcess = log.Where(r => OriginCode(r.RunBy) == JobOrigins.Prod)
                .MaxBy(r => r.StartedAt)?.RunBy;
            zones.Add(new JobZoneInfo(JobZones.Prod, prodProcess, prodTickAt, "tick", prod));
        }
        var local = items.Where(i => i.Zone == JobZones.Local).Select(i => i.Key).ToList();
        if (local.Count > 0)
            zones.Add(new JobZoneInfo(JobZones.Local, localProcess, localStartedAt, "started", local));
        return zones;
    }

    public static JobsMonitor Build(JobsMonitorInput input)
    {
        var (from, to) = Window(input.Now);
        var byJob = input.Log.ToLookup(r => r.Name);
        var items = input.Cards.Select(c => Item(c, [.. byJob[c.Key]], input.Now, from, to)).ToList();

        return new JobsMonitor(
            input.Now, input.Environment, input.Journal, from, to, WindowHours,
            Zones(items, input.Log, input.LocalProcess, input.LocalStartedAt, input.ProdTickAt),
            input.Summary.Counters,
            items,
            Attention(items, input.Waiting, input.Now),
            Running(items),
            Upcoming(items, input.Now),
            input.Waiting,
            input.Summary,
            input.RecordsSources,
            input.RecentImports);
    }

    private static int DurationSeconds(DateTime started, DateTime finished) =>
        Math.Max(0, (int)Math.Round((finished - started).TotalSeconds));

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
