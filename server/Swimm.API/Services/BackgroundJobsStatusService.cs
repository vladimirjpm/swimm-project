using System.Diagnostics;
using System.Text.Json;

using Swimm.API.Hosting;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.API.Services;

/// <summary>
/// Состояние всех фоновых служб процесса — для /Admin/Jobs и блока «Jobs» дашборда
/// (docs/admin-pages/jobs.md). Три вида: РАСПИСАНИЯ (сами ходят наружу по таймеру; журнал
/// <c>Sys_JobRuns</c> + история <c>Sys_JobRunLog</c>), ОЧЕРЕДИ (исполняют то, что поставила
/// админка; их история — в уже существующих таблицах) и СЛУЖЕБНЫЕ (пинг БД, сверка с продом,
/// мост кэша; живое состояние из памяти процесса). Какие службы запущены — решает Program.cs по
/// флагам <see cref="HostingMode"/>; новая служба = карточка здесь (docs/pre-push-rules.md №14).
///
/// Живёт в API-слое, как <see cref="HttpCurrentActor"/>: ему нужны флаги процесса и
/// <see cref="DbStatusService"/>. Подсчёт сводки — чистый <see cref="JobsOverviewBuilder"/>.
/// В ProdAdmin журнал и история — прод-базы: расписания работают только там.
/// </summary>
public sealed class BackgroundJobsStatusService(
    IJobRunStore jobRuns,
    ISettingsService settings,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    DbStatusService dbStatus,
    IDataSyncStatus dataSync,
    ICacheBridgeStatus cacheBridge,
    IImportJobQueue importQueue,
    IRecordCountryRunQueue countryRunQueue,
    IClubPairLogligCheckQueue clubLogligQueue,
    IAdminRepository adminRepository,
    IRecordSourceCheckService recordChecks,
    IPushSender pushSender,
    IResultsAutoWaitingQuery waitingQuery,
    ICompetitionDiscoveryService discovery,
    JobRunOrigin origin) : IBackgroundJobsStatus
{
    /// <summary>Сколько строк истории показывать на задачу (в базе держится <see cref="JobRunLog.KeepPerJob"/>).</summary>
    public const int HistoryRows = 15;

    /// <summary>
    /// Свежесть источников рекордов в мониторинге запоминается (своя память сервиса, НЕ кэш сайта):
    /// <c>GetFreshnessAsync</c> — десятки запросов, а ручку опрашивают раз в 30 с. Ключ — последняя
    /// попытка records-check и последний прогон по странам: после проверки запомненное
    /// промахивается само; Apply и CLI <c>--records-check</c> видны не позже этого срока.
    /// </summary>
    private static readonly TimeSpan FreshnessMemoFor = TimeSpan.FromMinutes(10);

    private static (string Key, DateTime At, IReadOnlyList<RecordSourceFreshnessDto> Value)? _freshnessMemo;

    /// <summary>Когда запущен этот процесс (UTC) — «пульс» зоны local.</summary>
    private static readonly Lazy<DateTime> ProcessStartedAt = new(() => Process.GetCurrentProcess().StartTime.ToUniversalTime());

    private bool AdminEnabled => HostingMode.AdminEnabled(configuration);
    private bool BackgroundJobsEnabled => HostingMode.BackgroundJobsEnabled(configuration, environment);

    /// <summary>Мониторинг /Admin/Jobs: зона прод только вне Development (решение 20.11.2026).</summary>
    private string ZoneOf(bool processOn, bool watchingProd) =>
        JobsMonitorBuilder.Zone(processOn, watchingProd, environment.IsDevelopment());

    public async Task<BackgroundJobsReport> GetReportAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var log = (await jobRuns.ListLogAsync(HistoryRows, ct)).ToLookup(r => r.Name);
        var schedules = BuildSchedules(await StatesAsync(ct), name => [.. log[name]], now);

        var countriesCheck = (await recordChecks.GetFreshnessAsync(ct))
            .FirstOrDefault(f => f.Source == RecordSources.WorldRecordsCountries);
        IReadOnlyList<JobCard> queues = [BuildImportQueue(now), BuildCountryRunQueue(countriesCheck, now), BuildClubLogligQueue(now)];
        var services = BuildServices(now);

        var waiting = await waitingQuery.ListAsync(now, ct);
        var summary = JobsOverviewBuilder.Build([.. schedules, .. queues, .. services],
            await jobRuns.GetStatsAsync(now, ct), Infra(), now, waiting.Count);
        var recentImports = await adminRepository.GetImportHistoryAsync(RecentImportRows);

        return new BackgroundJobsReport(environment.EnvironmentName, AdminEnabled, BackgroundJobsEnabled,
            schedules, queues, services, summary, recentImports);
    }

    private const int RecentImportRows = 5;

    public async Task<JobsSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        // Дашборду не нужны история прогонов и журналы очередей — только состояние и счётчики.
        var now = DateTime.UtcNow;
        var schedules = BuildSchedules(await StatesAsync(ct), _ => [], now);
        IReadOnlyList<JobCard> cards =
            [.. schedules, BuildImportQueue(now), BuildCountryRunQueue(null, now), BuildClubLogligQueue(now), .. BuildServices(now)];
        var waiting = await waitingQuery.ListAsync(now, ct);
        return JobsOverviewBuilder.Build(cards, await jobRuns.GetStatsAsync(now, ct), Infra(), now, waiting.Count);
    }

    /// <summary>
    /// Мониторинг одним JSON (GET /api/admin/jobs/monitor). Запросов к базе за опрос: состояние
    /// задач, лёгкий журнал (без Details), «ждёт тебя» (+ соревнования, если кандидаты есть), пять
    /// последних импортов; свежесть рекордов — запомненная (<see cref="FreshnessMemoFor"/>). Статистика
    /// 24 ч / 7 дн считается по уже прочитанному журналу тем же <see cref="JobRunStats.From"/>.
    /// </summary>
    public async Task<JobsMonitor> GetMonitorAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var states = await StatesAsync(ct);
        var log = await jobRuns.ListLogLightAsync(ct);
        var byJob = log.ToLookup(r => r.Name);
        var schedules = BuildSchedules(states, name => [.. byJob[name].Take(HistoryRows)], now);

        var freshness = await FreshnessMemoAsync(states, now, ct);
        var countriesCheck = freshness.FirstOrDefault(f => f.Source == RecordSources.WorldRecordsCountries);
        var recordsTile = RecordsCheckTileBuilder.Build(freshness, now);
        schedules = [.. schedules.Select(c => c.Key == JobNames.RecordsCheck
            ? c with { Facts = [.. c.Facts, new JobFact("Источники", $"проверены {recordsTile.Value}; {recordsTile.Sub}",
                recordsTile.Tone == "ok" ? null : recordsTile.Tone)] }
            : c)];
        IReadOnlyList<JobCard> cards =
            [.. schedules, BuildImportQueue(now), BuildCountryRunQueue(countriesCheck, now), BuildClubLogligQueue(now), .. BuildServices(now)];

        var waiting = await waitingQuery.ListAsync(now, ct);
        var stats = JobRunStats.From(log.Select(r => (r.StartedAt, r.Success)), now);
        var summary = JobsOverviewBuilder.Build(cards, stats, Infra(), now, waiting.Count);
        var recentImports = await adminRepository.GetImportHistoryAsync(RecentImportRows);

        return JobsMonitorBuilder.Build(new JobsMonitorInput(
            now,
            environment.EnvironmentName,
            environment.IsProdAdmin() ? "prod" : "local",
            cards,
            log,
            summary,
            waiting,
            origin.Value,
            ProcessStartedAt.Value,
            states.GetValueOrDefault(JobNames.TrainingReminders)?.LastAttemptAt,
            recordsTile,
            recentImports));
    }

    public async Task<JobRunDetailsView?> GetRunDetailsAsync(long id, CancellationToken ct = default)
    {
        var row = await jobRuns.GetLogAsync(id, ct);
        if (row is null) return null;

        JsonElement? details = null;
        if (row.Details is { } json)
        {
            try { using var doc = JsonDocument.Parse(json); details = doc.RootElement.Clone(); }
            catch (JsonException) { details = JsonSerializer.SerializeToElement(json); } // не JSON — отдаём строкой
        }

        DiscoveryRunChangesView? changes = null;
        if (row.Name == JobNames.Discovery && JobRunDetails.ParseDiscovery(row.Details) is { } parsed)
        {
            // Какой старт уже есть в базе — так же, как страница (JobsModel.BuildRunChangesAsync).
            var competitionIdByOrgCompId = (await discovery.GetAllAsync(ct))
                .Where(d => d.MatchedCompetitionId is not null)
                .GroupBy(d => d.OrgCompId)
                .ToDictionary(g => g.Key, g => g.First().MatchedCompetitionId!.Value);
            changes = DiscoveryRunChangesBuilder.Build(row.Id, row.StartedAt, parsed, competitionIdByOrgCompId);
        }

        return new JobRunDetailsView(row.Id, row.Name, row.Trigger, row.StartedAt, row.FinishedAt, row.Success,
            row.RunBy, JobsMonitorBuilder.OriginCode(row.RunBy), row.Summary, row.Error, details, changes);
    }

    private async Task<IReadOnlyDictionary<string, JobRunStateRow>> StatesAsync(CancellationToken ct) =>
        (await jobRuns.ListStatesAsync(ct)).ToDictionary(s => s.Name);

    private async Task<IReadOnlyList<RecordSourceFreshnessDto>> FreshnessMemoAsync(
        IReadOnlyDictionary<string, JobRunStateRow> states, DateTime now, CancellationToken ct)
    {
        var lastCountryRun = countryRunQueue.Snapshot().Max(r => r.CompletedAt);
        var key = $"{states.GetValueOrDefault(JobNames.RecordsCheck)?.LastAttemptAt:O}|{lastCountryRun:O}";
        if (_freshnessMemo is { } memo && memo.Key == key && now - memo.At < FreshnessMemoFor)
            return memo.Value;
        var fresh = await recordChecks.GetFreshnessAsync(ct);
        _freshnessMemo = (key, now, fresh);
        return fresh;
    }

    private JobsInfraState Infra()
    {
        var importJobs = importQueue.Snapshot();
        var countryRuns = countryRunQueue.Snapshot();
        var clubLogligRuns = clubLogligQueue.Snapshot();
        var ping = dbStatus.LastPing;
        return new JobsInfraState(
            AdminEnabled,
            environment.IsDevelopment(),
            importJobs.Count(j => j.State == ImportJobState.Queued) + countryRuns.Count(r => r.State == RecordCountryRunState.Queued)
                + clubLogligRuns.Count(r => r.State == ClubPairLogligRunStates.Queued),
            importJobs.Count(j => j.State == ImportJobState.Running) + countryRuns.Count(r => r.State == RecordCountryRunState.Running)
                + clubLogligRuns.Count(r => r.State == ClubPairLogligRunStates.Running),
            ping?.Ok,
            ping?.At.UtcDateTime,
            dataSync.Current,
            cacheBridge.State());
    }

    // ── Расписания ─────────────────────────────────────────────────────────────

    /// <summary>Что делает задача и как часто фон смотрит журнал (IdleCheck сервиса).</summary>
    private static readonly Dictionary<string, (string Title, string Service, string What, string Tick, string? SettingsExtra)> ScheduleInfo = new()
    {
        [JobNames.Discovery] = ("Входящие isr.org.il", "CompetitionDiscoveryBackgroundService",
            "Забирает список стартов текущего сезона с isr.org.il (competitions.asp) во «Входящие» " +
            "(Sys_DiscoveredCompetitions): новые старты, переносы дат, пропавшие из списка. Кнопка " +
            "«Проверить сейчас» в /Admin/Competitions и --discovery-sync пишут сюда же.",
            "5 мин", null),
        [JobNames.StartList] = ("Стартовые протоколы", "CompetitionDiscoveryBackgroundService (второй проход)",
            "Забирает стартовый протокол (CompetitionEntries) будущих стартов в окне StartListDaysAhead. " +
            "Интервал — шаг тика; кого смотреть в тик, решает правило по близости старта (П3): 3–14 дней " +
            "раз в сутки, ближайшие два дня каждые 3 ч (посев уже забран — 6 ч), день старта каждые 3 ч до " +
            "первого заплыва, потом не трогает. Раз в сутки (первый тик дня) дочитывает loglig-id, в том " +
            "числе прошедшим за 30 дней со статусом new (iframe часто появляется после старта). Итог: " +
            "сколько посмотрели, сколько «рано», сколько уже начались.",
            "5 мин", JobNames.StartListDaysAheadKey),
        [JobNames.ResultsAuto] = ("Результаты прошедших стартов", "CompetitionDiscoveryBackgroundService (третий проход)",
            "П5: за тик одна прошедшая строка «Входящих» (new, с loglig-id, с полудня дня окончания по Израилю " +
            "и не старше 7 дней): разбор как у пакетной кнопки → гейты (🟢 классификатора, не чемпионат, один " +
            "день, соревнование закончено, бассейн из регламента или справочника) → вердикт и причина у строки " +
            "(чип и фильтр в /Admin/Competitions, блок дашборда). Режим ResultsAutoMode: off — не работает, " +
            "report — только сообщает, import — пока как report (импорт — П6). НИЧЕГО НЕ ИМПОРТИРУЕТ.",
            "5 мин", null), // режим показан в факте «Настройка»
        [JobNames.LogligVerify] = ("Сверка предложенных loglig-id", "LogligSuggestionVerificationBackgroundService",
            "Берёт loglig-id, предложенные пользователями (Suggested), сверяет карточку loglig " +
            "(имя + год рождения) и переводит в Verified или Rejected.",
            "15 мин", null),
        [JobNames.LogligBatch] = ("Пакетная привязка loglig-id", "LogligBatchBackgroundService",
            "Прогоняет непривязанных пловцов через поиск и сверку. ПЛАТНО: каждый пловец — 1–3 " +
            "запроса Serper. Ручной батч в /Admin/Swimmers/Loglig пишет в историю, расписание не сдвигает.",
            "15 мин", "LogligBatchPerRun"),
        [JobNames.RecordsCheck] = ("Проверка источников рекордов", "RecordsCheckBackgroundService",
            "То же, что кнопка «Проверить все источники» на /Admin/Import: скачивает World Aquatics " +
            "(open, masters, junior) и справочники isr.org.il, строит дифф с базой и пишет журнал " +
            "Sys_RecordSourceChecks. Apply НЕ делает — «ждёт Apply» видно на дашборде и в /Admin/Import. " +
            "Прогон по странам сюда не входит.",
            "15 мин", null),
        [JobNames.SwimmerClubCheck] = ("Смена клуба пловца ↔ loglig", "SwimmerClubCheckBackgroundService",
            "Импорт клуб в карточке пловца сам не меняет (Р98): заводит кандидата (Sys_SwimmerClubCandidates). " +
            "Эта задача открывает карточку loglig (~1 в секунду, ответ кэшируется на сутки) и меняет клуб, " +
            "только если loglig показывает новый; остальные ждут с причиной на дашборде («Club changes»). " +
            "Сразу после импорта сверяет новых кандидатов (в истории — «после импорта»), раз в сутки — всех ждущих.",
            "15 мин или сигнал импорта", null),
    };

    private IReadOnlyList<JobCard> BuildSchedules(
        IReadOnlyDictionary<string, JobRunStateRow> states, Func<string, IReadOnlyList<JobRunLogRow>> history, DateTime now) =>
    [
        .. JobNames.All.Select(j => BuildSchedule(j, states.GetValueOrDefault(j.Name), history(j.Name), now)),
        BuildTrainingReminders(states.GetValueOrDefault(JobNames.TrainingReminders), history(JobNames.TrainingReminders), now),
    ];

    /// <summary>Заявка на прогон для мониторинга: живая или брошенная (старше <see cref="JobSchedule.ClaimStaleAfter"/>).</summary>
    private static JobClaimInfo? Claim(JobRunStateRow? state, DateTime now) =>
        state?.ClaimedAt is { } at
            ? new JobClaimInfo(at, state.ClaimedBy, JobSchedule.IsClaimLive(now, at), JobsMonitorBuilder.OriginCode(state.ClaimedBy))
            : null;

    private JobSettingValue SettingValue(string key) =>
        settings.Get(key) is { } s ? new JobSettingValue(key, s.Value, false) : new JobSettingValue(key, null, true);

    /// <summary>
    /// Напоминания о тренировках (docs/plans/web-push-reminders-plan.md) — ПРОД-РАСПИСАНИЕ: работает
    /// на проде под <c>PushReminders:Enabled</c>, а не в ProdAdmin. В ProdAdmin карточка всё равно
    /// «следит»: журнал Sys_JobRuns — прод-базы, и замолчавший прод-джоб (тик раз в 5 минут) виден
    /// здесь просрочкой. История — только тики, которые что-то отправили или упали.
    /// </summary>
    private JobCard BuildTrainingReminders(JobRunStateRow? state, IReadOnlyList<JobRunLogRow> history, DateTime now)
    {
        var processOn = HostingMode.PushRemindersEnabled(configuration);
        var watchingProd = environment.IsProdAdmin();
        var tick = TrainingReminderRules.Tick;
        var failed = state?.LastAttemptAt is { } attempt && (state.LastSuccessAt is null || attempt > state.LastSuccessAt.Value);
        var next = state?.LastAttemptAt is { } last ? last + tick : (DateTime?)null;
        var active = processOn || watchingProd;
        var silent = active && next is { } due && now - due > JobsOverviewBuilder.OverdueAfter;

        var facts = new List<JobFact>
        {
            new("В этом процессе", processOn ? "запущена (PushReminders:Enabled = true)"
                : watchingProd ? "не запущена — работает на ПРОДЕ; здесь журнал прод-базы"
                : "не запущена — PushReminders:Enabled = false (работает только на проде)",
                processOn ? "success" : "neutral"),
            new("Ключи VAPID", pushSender.IsConfigured ? "заданы (WebPush:PublicKey / PrivateKey)" : "НЕ заданы — push не уходят",
                pushSender.IsConfigured ? "success" : processOn ? "danger" : "neutral"),
            new("Тик", $"{JobTimeText.Human(tick)}; от дубля — UNIQUE журнала Sys_ReminderDeliveries"),
            new("Последний тик", JobTimeText.When(state?.LastAttemptAt, now), silent ? "warning" : null),
            new("Последний успех", JobTimeText.When(state?.LastSuccessAt, now)),
        };
        if (failed && state?.LastError is { } error)
            facts.Add(new("Ошибка", error, "danger"));

        var badge = !active ? new JobBadge("не в этом процессе", "neutral")
            : failed ? new JobBadge("последний тик упал", "danger")
            : state?.LastAttemptAt is null ? new JobBadge("ещё не запускалась", "warning")
            : silent ? new JobBadge("молчит", "warning")
            : new JobBadge("работает", "success");

        var lastText = state?.LastAttemptAt is { } at
            ? (failed ? $"ошибка {JobTimeText.Ago(at, now)}" : $"тик {JobTimeText.Ago(at, now)}")
            : "ещё не запускалась";
        return new JobCard(JobNames.TrainingReminders, "Напоминания о тренировках (push)", JobKinds.Schedule,
            "TrainingReminderBackgroundService",
            "Раз в 5 минут смотрит занятия групп и шлёт Web Push участникам, которым пора напомнить " +
            "(за 1–2 часа или вечером накануне; не ответившим «No», не на перерыве; тихие часы 22–07). " +
            "Работает НА ПРОДЕ круглосуточно — свой флаг PushReminders:Enabled, не BackgroundJobs.",
            badge, facts, history, null)
        {
            Active = active,
            Failed = failed,
            NextDueAt = active ? next : null,
            Status = !active ? "работает только на проде (PushReminders:Enabled)"
                : silent ? $"молчит: {lastText}" : lastText,
            Alert = active && failed ? "danger" : silent ? "warning" : null,
            // Честность (решение 20.11.2026): пустые тики в историю не пишутся — отдаём последний тик
            // (LastRunAt) и непустые прогоны; сплошную ленту «тикает» клиент рисует сам (TimelineMode).
            Monitor = new JobMonitorExtras
            {
                Zone = ZoneOf(processOn, watchingProd),
                TimelineMode = JobTimelineModes.Ticker,
                Interval = tick,
                LastRunAt = state?.LastAttemptAt,
                LastSuccessAt = state?.LastSuccessAt,
                NextAt = active ? next : null,
                LastError = failed ? state?.LastError : null,
                Claim = Claim(state, now),
            },
        };
    }

    /// <summary>Задачи <c>CompetitionDiscoveryBackgroundService</c> — прод-расписание под <c>AutoPull:Enabled</c> (П2).</summary>
    private static bool IsAutoPullJob(string name) => name is JobNames.Discovery or JobNames.StartList or JobNames.ResultsAuto;

    /// <summary>
    /// Где идёт задача и что видно отсюда. У discovery/start-list (П2, prod-auto-pull-plan §4.2) три
    /// случая: здесь прод-расписание (AutoPull:Enabled), здесь по-старому (ProdAdmin, флаг на проде
    /// выключен) или здесь не идёт, потому что забирает прод, — тогда ProdAdmin «следит» за журналом
    /// прод-базы, как у напоминаний (<see cref="BuildTrainingReminders"/>): просрочка и сбой видны.
    /// </summary>
    private (bool ProcessOn, bool Watching, string Text) Placement(JobDefinition job)
    {
        if (!IsAutoPullJob(job.Name))
            return BackgroundJobsEnabled
                ? (true, false, "запущена")
                : (false, false, "не запущена — фоновые расписания выключены (BackgroundJobs:Enabled)");

        var here = HostingMode.DiscoveryScheduleEnabled(configuration, environment);
        if (here && HostingMode.AutoPullEnabled(configuration))
            return (true, false, "запущена — прод-расписание (AutoPull:Enabled)");
        if (here)
            return (true, false, "запущена — по расписанию ProdAdmin (на проде AutoPull:Enabled = false)");
        if (environment.IsProdAdmin() && HostingMode.ProdAutoPullEnabled(environment.ContentRootPath))
            return (false, true, "не запущена — работает на ПРОДЕ (AutoPull:Enabled в appsettings.Production.json); " +
                                 "здесь журнал прод-базы, кнопка «Проверить сейчас» и CLI");
        return (false, false, "не запущена — фоновые расписания выключены (BackgroundJobs:Enabled), AutoPull:Enabled = false");
    }

    private JobCard BuildSchedule(JobDefinition job, JobRunStateRow? state, IReadOnlyList<JobRunLogRow> history, DateTime now)
    {
        var info = ScheduleInfo[job.Name];
        var (processOn, watching, placement) = Placement(job);
        var enabled = job.IsEnabled(settings);
        var interval = job.Interval(settings);
        // results-auto (П5): главный выключатель — режим; off — задача выключена, как EnabledKey = false.
        var modeOff = job.Name == JobNames.ResultsAuto
            && ResultsAutoModes.Parse(settings.Get(ResultsAutoModes.SettingKey)?.Value) == ResultsAutoModes.Off;
        var offKey = modeOff && enabled ? ResultsAutoModes.SettingKey : job.EnabledKey;
        enabled &= !modeOff;
        // Следим за прод-джобом как за своим: просрочка и упавший прогон должны желтить/краснить и здесь.
        var s = JobsOverviewBuilder.Schedule(processOn || watching, enabled, offKey, state, interval, now);

        var facts = new List<JobFact>
        {
            new("В этом процессе", placement, processOn ? "success" : "neutral"),
            new("Настройка", $"{job.EnabledKey} = {(job.IsEnabled(settings) ? "true" : "false")}" +
                             (settings.Get(job.EnabledKey) is null ? " (в Admin/Settings ключа нет — значение по умолчанию)" : "") +
                             (job.Name == JobNames.ResultsAuto
                                 ? $"; {ResultsAutoModes.SettingKey} = {ResultsAutoModes.Parse(settings.Get(ResultsAutoModes.SettingKey)?.Value)}" +
                                   (modeOff ? " — задача выключена режимом" : "")
                                 : ""),
                enabled ? "success" : "neutral"),
            new("Интервал", $"{JobTimeText.Human(interval)} ({job.IntervalKey}); после неудачи — через " +
                            $"{JobTimeText.Human(JobSchedule.RetryAfterFailure)}; журнал смотрит раз в {info.Tick}"),
        };
        if (info.SettingsExtra is { } extraKey)
            facts.Add(new("Параметр", $"{extraKey} = {settings.Get(extraKey)?.Value ?? "по умолчанию"}"));

        // Заявка на прогон (замок П1): идёт сейчас — кто и с какого времени; старше
        // ClaimStaleAfter — процесс убили посреди работы, следующий прогон её перехватит.
        if (s.Running)
            facts.Add(new("Идёт сейчас", $"с {JobTimeText.When(state!.ClaimedAt, now)}, запустил {state.ClaimedBy ?? "?"}", "info"));
        else if (state?.ClaimedAt is not null)
            facts.Add(new("Брошенная заявка", $"{JobTimeText.When(state.ClaimedAt, now)}, {state.ClaimedBy ?? "?"} — процесс не снял её " +
                "(убит посреди прогона); следующий прогон заберёт сам", "warning"));

        facts.Add(new("Последний запуск", JobTimeText.When(state?.LastAttemptAt, now)));
        facts.Add(new("Последний успех", JobTimeText.When(state?.LastSuccessAt, now)));
        if (s.Failed && state?.LastError is { } error)
            facts.Add(new("Ошибка", error, "danger"));
        facts.Add(new("Следующий",
            s.Running ? "после текущего прогона"
            : !processOn && !watching ? "здесь не запустится — расписания работают в ProdAdmin"
            : !enabled ? "не запустится — выключена в настройках"
            : watching && (s.NextDueAt is not { } due || due <= now) ? "пора — при ближайшей проверке журнала на ПРОДЕ"
            : s.NextDueAt is not { } next || next <= now ? "пора — при ближайшей проверке журнала"
            : $"{JobTimeText.Format(next)} ({JobTimeText.Until(next, now)})",
            s.Alert == "warning" ? "warning" : null));

        var badge = s.Running ? new JobBadge("идёт сейчас", "info")
            : !processOn && !watching ? new JobBadge("не в этом процессе", "neutral")
            : !enabled ? new JobBadge("выключена", "neutral")
            : s.Failed ? new JobBadge("последний запуск упал", "danger")
            : s.Alert == "warning" ? new JobBadge("просрочена", "warning")
            : state?.LastSuccessAt is null ? new JobBadge("ещё не запускалась", "warning")
            : new JobBadge("работает", "success");

        return new JobCard(job.Name, info.Title, JobKinds.Schedule, info.Service, info.What, badge, facts, history, null)
        {
            Active = s.Active,
            Failed = s.Failed,
            NextDueAt = s.NextDueAt,
            Status = watching ? $"на проде (AutoPull:Enabled): {s.Status}" : s.Status,
            Alert = s.Alert,
            Monitor = new JobMonitorExtras
            {
                // Зона прод — ровно условие факта «не запущена — работает на ПРОДЕ…» (Placement.Watching).
                Zone = ZoneOf(processOn, watching),
                TimelineMode = JobTimelineModes.Runs,
                Interval = interval,
                IntervalKey = job.IntervalKey,
                Toggle = new JobToggle(job.EnabledKey, job.IsEnabled(settings), settings.Get(job.EnabledKey) is null),
                Settings =
                [
                    SettingValue(job.IntervalKey),
                    .. job.Name == JobNames.ResultsAuto ? [SettingValue(ResultsAutoModes.SettingKey)] : Array.Empty<JobSettingValue>(),
                    .. info.SettingsExtra is { } extra ? [SettingValue(extra)] : Array.Empty<JobSettingValue>(),
                ],
                LastRunAt = state?.LastAttemptAt,
                LastSuccessAt = state?.LastSuccessAt,
                NextAt = s.NextDueAt,
                NextIsRetry = s.Failed,
                // start-list (П3): шаг — только тик; забор конкретного старта решает правило близости.
                NextApprox = job.Name == JobNames.StartList,
                Claim = Claim(state, now),
                LastError = s.Failed ? state?.LastError : null,
                Actions = ScheduleActions(job.Name),
            },
        };
    }

    /// <summary>Ссылки и кнопки панели деталей расписания — те, что есть на страницах сейчас.</summary>
    private static IReadOnlyList<JobAction> ScheduleActions(string name)
    {
        var list = new List<JobAction>();
        switch (name)
        {
            case JobNames.Discovery:
            case JobNames.StartList:
                list.Add(new(JobAction.Link, "Входящие и «Проверить сейчас» — /Admin/Competitions", "/Admin/Competitions"));
                break;
            case JobNames.ResultsAuto:
                list.Add(new(JobAction.Link, "Ждут тебя — /Admin/Competitions", $"/Admin/Competitions?filter={AutoPullFilters.Blocked}"));
                break;
            case JobNames.RecordsCheck:
                // Та же кнопка, что плитка «Рекорды · источники» и /Admin/Import: прогон ложится в журнал records-check.
                list.Add(new(JobAction.Post, "Проверить сейчас", "/api/admin/records/check-all", "до 5 минут; после ответа — обновить"));
                list.Add(new(JobAction.Link, "Дифф рекордов — /Admin/Import", "/Admin/Import?diff=records"));
                break;
            case JobNames.LogligBatch:
                list.Add(new(JobAction.Link, "Ручной батч — /Admin/Swimmers/Loglig", "/Admin/Swimmers/Loglig"));
                break;
        }
        list.Add(new(JobAction.Link, "Включатель и интервал — /Admin/Settings", "/Admin/Settings"));
        return list;
    }

    // ── Очереди ────────────────────────────────────────────────────────────────

    private JobCard BuildImportQueue(DateTime now)
    {
        var jobs = importQueue.Snapshot();
        var queued = jobs.Count(j => j.State == ImportJobState.Queued);
        var running = jobs.Count(j => j.State == ImportJobState.Running);
        var last = jobs.Where(j => j.CompletedAt is not null).MaxBy(j => j.CompletedAt);
        var facts = new List<JobFact>
        {
            RegisteredFact(AdminEnabled, "вместе с админкой, Admin:Enabled"),
            new("Сейчас", $"в очереди {queued}, идёт {running}"),
            new("С запуска процесса", $"выполнено {jobs.Count(j => j.State == ImportJobState.Completed)}, " +
                                     $"упало {jobs.Count(j => j.State == ImportJobState.Failed)}"),
        };
        if (last is not null)
            facts.Add(new("Последнее задание", $"{JobTimeText.When(last.CompletedAt!.Value.UtcDateTime, now)} — " +
                (last.State == ImportJobState.Failed ? $"ошибка: {last.Error}" : last.Result?.Message ?? "готово"),
                last.State == ImportJobState.Failed ? "danger" : null));

        var badge = !AdminEnabled ? new JobBadge("не в этом процессе", "neutral")
            : running > 0 ? new JobBadge("импортирует", "info")
            : new JobBadge("ждёт задания", "success");

        return new JobCard("import", "Импорт протоколов", JobKinds.Queue, "ImportBackgroundService",
            "Исполняет импорты, поставленные в /Admin/Import и «Затянуть» во «Входящих»: разбор → " +
            "запись в базу → клубный зачёт и loglig-id из протокола. Очередь в памяти: после " +
            "перезапуска счётчики с нуля; постоянная история — Sys_ImportHistory.",
            badge, facts, [], new JobLink("/Admin/ImportHistory", "Вся история импортов"))
        {
            Active = AdminEnabled,
            Status = QueueStatus(queued, running, last is null ? null
                : last.State == ImportJobState.Failed ? $"последнее упало {JobTimeText.Ago(last.CompletedAt!.Value.UtcDateTime, now)}"
                : $"последнее ок {JobTimeText.Ago(last.CompletedAt!.Value.UtcDateTime, now)}"),
            Monitor = new JobMonitorExtras
            {
                Zone = JobZones.Local,
                TimelineMode = JobTimelineModes.Runs,
                LastRunAt = last?.CompletedAt?.UtcDateTime,
                LastSummary = last is null ? null : last.State == ImportJobState.Failed ? last.Error : last.Result?.Message ?? "готово",
                LastError = last?.State == ImportJobState.Failed ? last.Error : null,
                // Начала у задания импорта нет — только постановка в очередь: «идёт с» — от неё.
                StateSince = jobs.Where(j => j.State == ImportJobState.Running).Min(j => (DateTimeOffset?)j.QueuedAt)?.UtcDateTime,
                Queue = new JobQueueCounts(queued, running, jobs.Count(j => j.State == ImportJobState.Completed),
                    jobs.Count(j => j.State == ImportJobState.Failed)),
                QueueRuns = QueueRuns(jobs.Where(j => j.State != ImportJobState.Queued)
                    .Select(j => (j.QueuedAt, j.CompletedAt, j.State == ImportJobState.Running, j.State == ImportJobState.Failed))),
                Actions = [new(JobAction.Link, "Вся история импортов — /Admin/ImportHistory", "/Admin/ImportHistory"),
                           new(JobAction.Link, "Поставить импорт — /Admin/Import", "/Admin/Import")],
            },
        };
    }

    /// <summary>Прогоны очереди из памяти процесса → метки таймлайна и полоска исходов (свежие первыми).</summary>
    private static IReadOnlyList<JobWindowRun> QueueRuns(
        IEnumerable<(DateTimeOffset Start, DateTimeOffset? End, bool Running, bool Failed)> runs) =>
        [.. runs
            .Select(r => new JobWindowRun(null, r.Start.UtcDateTime, r.Running ? null : r.End?.UtcDateTime,
                r.Running ? JobRunOutcomes.Running : r.Failed ? JobRunOutcomes.Failed : JobRunOutcomes.Success,
                JobTriggers.Manual, null, null))
            .OrderByDescending(r => r.FinishedAt ?? DateTime.MaxValue)];

    private JobCard BuildCountryRunQueue(RecordSourceFreshnessDto? check, DateTime now)
    {
        var runs = countryRunQueue.Snapshot();
        var current = runs.FirstOrDefault(r => r.State == RecordCountryRunState.Running);
        var last = runs.Where(r => r.CompletedAt is not null).MaxBy(r => r.CompletedAt);
        var facts = new List<JobFact>
        {
            RegisteredFact(AdminEnabled, "вместе с админкой, Admin:Enabled"),
        };
        if (current is not null)
            facts.Add(new("Идёт прогон", $"{current.Done} из {current.Total} стран, сейчас {current.CurrentCode ?? "—"}, строк {current.RowsFetched}", "info"));
        if (last is not null)
            facts.Add(new("Последний с запуска процесса", $"{JobTimeText.When(last.CompletedAt!.Value.UtcDateTime, now)} — " +
                (last.State == RecordCountryRunState.Failed ? $"ошибка: {last.Error}"
                    : $"{last.Done} из {last.Total} стран, строк {last.RowsFetched}, упало {last.Failed.Count}"),
                last.State == RecordCountryRunState.Failed ? "danger" : null));

        // Постоянная история — журнал свежести рекордов (Sys_RecordSourceChecks): туда пишет
        // только ПОЛНЫЙ прогон, прогон подмножеством стран — отладка.
        if (check is not null)
        {
            facts.Add(new("Последняя полная сверка", JobTimeText.When(check.CheckedAt, now)));
            if (check.LastAttemptAt != check.CheckedAt)
                facts.Add(new("Последняя попытка", JobTimeText.When(check.LastAttemptAt, now)));
            facts.Add(new("Исход", check.LastOutcome switch
            {
                null => "—",
                "changes-found" => $"есть изменения: +{check.PendingAdded} / ~{check.PendingChanged}" +
                                   (check.AppliedAt is null ? ", ждут Apply" : $", применены {JobTimeText.When(check.AppliedAt, now)}"),
                "unchanged" => "без изменений",
                "failed" => $"сбой: {check.LastError}",
                var other => other,
            }, check.LastOutcome == "failed" ? "danger" : null));
        }

        var badge = !AdminEnabled ? new JobBadge("не в этом процессе", "neutral")
            : current is not null ? new JobBadge("идёт прогон", "info")
            : new JobBadge("ждёт задания", "success");

        return new JobCard("record-country-run", "Рекорды по странам", JobKinds.Queue, "RecordCountryRunBackgroundService",
            "Прогон «национальные рекорды по странам» (11.1.2): ~470 файлов, час-два. Запускается " +
            "кнопкой в /Admin/Import, итог — дифф для Apply.",
            badge, facts, [], new JobLink("/Admin/Import", "Запуск и дифф — /Admin/Import"))
        {
            Active = AdminEnabled,
            Status = current is not null && AdminEnabled
                ? $"идёт прогон: {current.Done} из {current.Total} стран"
                : QueueStatus(runs.Count(r => r.State == RecordCountryRunState.Queued), 0, last is null ? null
                    : last.State == RecordCountryRunState.Failed ? $"последний упал {JobTimeText.Ago(last.CompletedAt!.Value.UtcDateTime, now)}"
                    : $"последний ок {JobTimeText.Ago(last.CompletedAt!.Value.UtcDateTime, now)}"),
            Monitor = new JobMonitorExtras
            {
                Zone = JobZones.Local,
                TimelineMode = JobTimelineModes.Runs,
                LastRunAt = last?.CompletedAt?.UtcDateTime,
                LastSummary = last is null ? null
                    : last.State == RecordCountryRunState.Failed ? last.Error
                    : $"{last.Done} из {last.Total} стран, строк {last.RowsFetched}, упало {last.Failed.Count}",
                LastError = last?.State == RecordCountryRunState.Failed ? last.Error : null,
                LastSuccessAt = check?.CheckedAt,
                StateSince = current?.QueuedAt.UtcDateTime,
                Progress = current is null ? null
                    : new JobProgress(current.Done, current.Total, current.CurrentCode, "стран", $"строк {current.RowsFetched}"),
                Queue = new JobQueueCounts(runs.Count(r => r.State == RecordCountryRunState.Queued), current is null ? 0 : 1,
                    runs.Count(r => r.State == RecordCountryRunState.Completed), runs.Count(r => r.State == RecordCountryRunState.Failed)),
                QueueRuns = QueueRuns(runs.Where(r => r.State != RecordCountryRunState.Queued)
                    .Select(r => (r.QueuedAt, r.CompletedAt, r.State == RecordCountryRunState.Running, r.State == RecordCountryRunState.Failed))),
                Actions = [new(JobAction.Link, "Запуск и дифф — /Admin/Import", "/Admin/Import")],
            },
        };
    }

    private JobCard BuildClubLogligQueue(DateTime now)
    {
        var runs = clubLogligQueue.Snapshot();
        var current = runs.FirstOrDefault(r => r.Active);
        var last = runs.Where(r => r.CompletedAt is not null).MaxBy(r => r.CompletedAt);
        static string Scope(ClubPairLogligRunStatus r) => r.AllPairs ? "все пары" : $"пара #{r.ClubAId}/#{r.ClubBId}";
        var facts = new List<JobFact>
        {
            RegisteredFact(AdminEnabled, "вместе с админкой, Admin:Enabled"),
        };
        if (current is not null)
            facts.Add(new("Идёт проверка", current.State == ClubPairLogligRunStates.Queued ? $"{Scope(current)}: в очереди"
                : $"{Scope(current)}: карточек {current.SwimmersDone} из {current.SwimmersTotal}, пар {current.PairsDone} из {current.PairsTotal}", "info"));
        if (last is not null)
            facts.Add(new("Последняя с запуска процесса", $"{JobTimeText.When(last.CompletedAt!.Value.UtcDateTime, now)} — {Scope(last)}: " +
                (last.State == ClubPairLogligRunStates.Failed ? $"ошибка: {last.Error}"
                    : $"пар {last.PairsDone}, карточек {last.SwimmersDone}, не открылось {last.Failed}"),
                last.State == ClubPairLogligRunStates.Failed ? "danger" : null));

        var badge = !AdminEnabled ? new JobBadge("не в этом процессе", "neutral")
            : current is not null ? new JobBadge("идёт проверка", "info")
            : new JobBadge("ждёт задания", "success");

        return new JobCard("club-pair-loglig", "Пары клубов: проверка в loglig", JobKinds.Queue, "ClubPairLogligCheckBackgroundService",
            "«Проверить в loglig» на /Admin/Clubs (Р112): у пар «Общие пловцы» открывает карточки loglig общих " +
            "пловцов с ответом старше суток (~1 в секунду) и сохраняет клуб в Sys_SwimmerLogligClubs. Одна пара " +
            "или все сразу; активный прогон один. История — только с запуска процесса.",
            badge, facts, [], new JobLink("/Admin/Clubs?filter=dedup-unsure", "Запуск и сводки — /Admin/Clubs"))
        {
            Active = AdminEnabled,
            Status = current is not null && AdminEnabled
                ? $"идёт проверка: {current.SwimmersDone} из {current.SwimmersTotal} карточек"
                : QueueStatus(0, 0, last is null ? null
                    : last.State == ClubPairLogligRunStates.Failed ? $"последняя упала {JobTimeText.Ago(last.CompletedAt!.Value.UtcDateTime, now)}"
                    : $"последняя ок {JobTimeText.Ago(last.CompletedAt!.Value.UtcDateTime, now)}"),
            Monitor = new JobMonitorExtras
            {
                Zone = JobZones.Local,
                TimelineMode = JobTimelineModes.Runs,
                LastRunAt = last?.CompletedAt?.UtcDateTime,
                LastSummary = last is null ? null
                    : last.State == ClubPairLogligRunStates.Failed ? last.Error
                    : $"{Scope(last)}: пар {last.PairsDone}, карточек {last.SwimmersDone}, не открылось {last.Failed}",
                LastError = last?.State == ClubPairLogligRunStates.Failed ? last.Error : null,
                StateSince = current is null ? null : (current.StartedAt ?? current.QueuedAt).UtcDateTime,
                Progress = current is null || current.State == ClubPairLogligRunStates.Queued ? null
                    : new JobProgress(current.SwimmersDone, current.SwimmersTotal, Scope(current), "карточек",
                        $"пар {current.PairsDone} из {current.PairsTotal}"),
                Queue = new JobQueueCounts(runs.Count(r => r.State == ClubPairLogligRunStates.Queued),
                    runs.Count(r => r.State == ClubPairLogligRunStates.Running),
                    runs.Count(r => r.CompletedAt is not null && r.State != ClubPairLogligRunStates.Failed),
                    runs.Count(r => r.State == ClubPairLogligRunStates.Failed)),
                QueueRuns = QueueRuns(runs.Where(r => r.State != ClubPairLogligRunStates.Queued)
                    .Select(r => (r.StartedAt ?? r.QueuedAt, r.CompletedAt, r.Active, r.State == ClubPairLogligRunStates.Failed))),
                Actions = [new(JobAction.Link, "Запуск и сводки — /Admin/Clubs", "/Admin/Clubs?filter=dedup-unsure")],
            },
        };
    }

    private string QueueStatus(int queued, int running, string? last) =>
        !AdminEnabled ? "выключена флагом процесса (Admin:Enabled)"
        : (running > 0 || queued > 0 ? $"идёт {running}, в очереди {queued}" : "ждёт задания")
          + (last is null ? "" : $"; {last}");

    // ── Служебные ──────────────────────────────────────────────────────────────

    private IReadOnlyList<JobCard> BuildServices(DateTime now) => [BuildDbPing(now), BuildDataSync(now), BuildCacheBridge(now)];

    private JobCard BuildDbPing(DateTime now)
    {
        var ping = dbStatus.LastPing;
        var facts = new List<JobFact>
        {
            RegisteredFact(true, "всегда"),
            new("Интервал", "30 с (первый — при старте)"),
            new("Последний пинг", ping is null ? "ещё не было" : $"{JobTimeText.When(ping.At.UtcDateTime, now)} — {(ping.Ok ? "база отвечает" : "база НЕ отвечает")}",
                ping is { Ok: false } ? "danger" : null),
            new("С запуска процесса", ping is null ? "—" : $"пингов {ping.Pings}, неудачных {ping.Failures}"),
        };
        if (ping?.LastError is { } error)
            facts.Add(new("Последняя ошибка", $"{JobTimeText.When(ping.LastErrorAt?.UtcDateTime, now)} — {error}", ping.Ok ? null : "danger"));

        var badge = ping is null ? new JobBadge("ждёт первого пинга", "warning")
            : ping.Ok ? new JobBadge("база доступна", "success")
            : new JobBadge("база недоступна", "danger");
        return new JobCard("db-ping", "Пинг базы", JobKinds.Service, "DbPingBackgroundService",
            "Раз в 30 секунд проверяет, что база отвечает. Флаг читают /readyz и /api/db-status " +
            "— по нему сайт и мониторинг видят, что база лежит; вход и проверка куки обновляют его и сами.",
            badge, facts, [], null)
        {
            Active = true,
            Status = ping is null ? "ждёт первого пинга"
                : ping.Ok ? $"база доступна, пинг {JobTimeText.Ago(ping.At.UtcDateTime, now)}"
                : $"база НЕ отвечает, пинг {JobTimeText.Ago(ping.At.UtcDateTime, now)}",
            Alert = ping is { Ok: false } ? "danger" : null,
            Monitor = new JobMonitorExtras
            {
                Interval = TimeSpan.FromSeconds(30),
                LastRunAt = ping?.At.UtcDateTime,
                LastSuccessAt = ping is { Ok: true } ? ping.At.UtcDateTime : null,
                LastSummary = ping is null ? null
                    : $"{(ping.Ok ? "база доступна" : "база НЕ отвечает")} · пингов {ping.Pings}, неудачных {ping.Failures}",
                LastError = ping is { Ok: false } ? ping.LastError : null,
            },
        };
    }

    private JobCard BuildDataSync(DateTime now)
    {
        var registered = environment.IsDevelopment();
        var state = dataSync.Current;
        var facts = new List<JobFact>
        {
            RegisteredFact(registered, "только Development"),
            new("Настройка", dataSync.Enabled ? "ProdVersionConnection задана" : "ProdVersionConnection не задана — сверка молчит",
                dataSync.Enabled ? "success" : "neutral"),
            new("Интервал", "15 мин (первая — через 5 с после старта)"),
        };
        if (state is not null)
        {
            facts.Add(new("Последняя сверка", JobTimeText.When(state.CheckedAt.UtcDateTime, now)));
            facts.Add(state.Error is { } error
                ? new("Итог", $"сверить не удалось: {error}", "warning")
                : new("Итог", state.Behind
                    ? $"локальная база ОТСТАЁТ: прод v{state.ProdVersion}, локальная снята с v{state.SyncedFromVersion?.ToString() ?? "—"} → /db-sync-from-prod"
                    : $"не отстаёт: прод v{state.ProdVersion}, локальная снята с v{state.SyncedFromVersion}",
                    state.Behind ? "warning" : "success"));
            if (state.ProdLastChangedAt is { } changed)
                facts.Add(new("Последняя правка на проде", $"{JobTimeText.When(changed.UtcDateTime, now)} — {state.ProdLastTable}"));
        }

        var active = registered && dataSync.Enabled;
        var badge = !registered ? new JobBadge("не в этом процессе", "neutral")
            : !dataSync.Enabled ? new JobBadge("выключена", "neutral")
            : state is null ? new JobBadge("ещё не сверяла", "warning")
            : state.Error is not null ? new JobBadge("прод недоступен", "warning")
            : state.Behind ? new JobBadge("локальная отстаёт", "warning")
            : new JobBadge("в синхроне", "success");
        return new JobCard("data-sync-check", "Отставание локальной базы от прода", JobKinds.Service, "DataSyncCheckBackgroundService",
            "Только в разработке: сравнивает версию данных прода (Sys_DataVersion) с той, от которой " +
            "снята локальная копия. Отстала — Warning в логе и жёлтая полоса в админке (В17).",
            badge, facts, [], null)
        {
            Active = active,
            Status = !registered ? "не запущена — только Development"
                : !dataSync.Enabled ? "выключена настройкой (ProdVersionConnection не задана)"
                : state is null ? "ещё не сверяла"
                : state.Error is not null ? $"прод недоступен ({JobTimeText.Ago(state.CheckedAt.UtcDateTime, now)})"
                : state.Behind ? $"локальная база отстаёт ({JobTimeText.Ago(state.CheckedAt.UtcDateTime, now)})"
                : $"в синхроне ({JobTimeText.Ago(state.CheckedAt.UtcDateTime, now)})",
            Alert = active && state is { Error: null, Behind: true } ? "warning" : null,
            Monitor = new JobMonitorExtras
            {
                Interval = TimeSpan.FromMinutes(15),
                LastRunAt = state?.CheckedAt.UtcDateTime,
                LastSummary = state is null ? null
                    : state.Error is not null ? "прод недоступен"
                    : $"{(state.Behind ? "отстаёт" : "в синхроне")}: прод v{state.ProdVersion}, локальная v{state.SyncedFromVersion?.ToString() ?? "—"}",
                LastError = state?.Error,
            },
        };
    }

    private JobCard BuildCacheBridge(DateTime now)
    {
        var s = cacheBridge.State();
        var facts = new List<JobFact>
        {
            RegisteredFact(s.Enabled, "CacheBridge:Enabled, по умолчанию да"),
            new("Канал", s.Channel),
            new("Слушает", s.Listening ? $"да, с {JobTimeText.When(s.ListeningSince?.UtcDateTime, now)}" : "нет", s.Listening ? "success" : s.Enabled ? "danger" : null),
            new("С запуска процесса", $"отправлено {s.Published} (не ушло {s.PublishFailures}, общим сбросом {s.Oversized}), " +
                                     $"принято {s.Received}, переподключений {s.Reconnects}"),
        };
        if (s.LastReceivedAt is { } received)
            facts.Add(new("Последний чужой сброс", $"{JobTimeText.When(received.UtcDateTime, now)} — {s.LastReceivedReason}"));
        if (s.LastError is { } error)
            facts.Add(new("Последняя ошибка", $"{JobTimeText.When(s.LastErrorAt?.UtcDateTime, now)} — {error}", "danger"));

        var badge = !s.Enabled ? new JobBadge("выключен", "neutral")
            : s.Listening ? new JobBadge("слушает", "success")
            : new JobBadge("не слушает", "danger");
        return new JobCard("cache-bridge", "Мост сброса кэша", JobKinds.Service, "CacheBridgeListener",
            "Слушает канал Postgres (LISTEN): запись из другого процесса (ProdAdmin, CLI) сбрасывает " +
            "кэш и здесь, без ожидания TTL. Подробности — /Admin/Cache.",
            badge, facts, [], new JobLink("/Admin/Cache", "Кэш и журнал сбросов"))
        {
            Active = s.Enabled,
            Status = !s.Enabled ? "выключен настройкой (CacheBridge:Enabled)"
                : s.Listening ? $"слушает; принято {s.Received}, отправлено {s.Published}"
                : "НЕ слушает" + (s.LastError is { } e ? $": {e}" : ""),
            Alert = s.Enabled && !s.Listening ? "danger" : null,
            Monitor = new JobMonitorExtras
            {
                LastRunAt = s.LastReceivedAt?.UtcDateTime,
                StateSince = s.Listening ? s.ListeningSince?.UtcDateTime : null,
                LastSummary = $"принято {s.Received} · отправлено {s.Published}",
                LastError = s.Enabled && !s.Listening ? s.LastError : null,
                Actions = [new(JobAction.Link, "Кэш и журнал сбросов — /Admin/Cache", "/Admin/Cache")],
            },
        };
    }

    private static JobFact RegisteredFact(bool registered, string rule) =>
        new("В этом процессе", registered ? $"запущена ({rule})" : $"не запущена — {rule}", registered ? "success" : "neutral");
}
