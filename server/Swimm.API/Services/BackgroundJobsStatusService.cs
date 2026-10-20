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
    IAdminRepository adminRepository,
    IRecordSourceCheckService recordChecks,
    IPushSender pushSender) : IBackgroundJobsStatus
{
    /// <summary>Сколько строк истории показывать на задачу (в базе держится <see cref="JobRunLog.KeepPerJob"/>).</summary>
    public const int HistoryRows = 15;

    private bool AdminEnabled => HostingMode.AdminEnabled(configuration);
    private bool BackgroundJobsEnabled => HostingMode.BackgroundJobsEnabled(configuration, environment);

    public async Task<BackgroundJobsReport> GetReportAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var log = (await jobRuns.ListLogAsync(HistoryRows, ct)).ToLookup(r => r.Name);
        var schedules = await BuildSchedulesAsync(name => [.. log[name]], now, ct);

        var countriesCheck = (await recordChecks.GetFreshnessAsync(ct))
            .FirstOrDefault(f => f.Source == RecordSources.WorldRecordsCountries);
        IReadOnlyList<JobCard> queues = [BuildImportQueue(now), BuildCountryRunQueue(countriesCheck, now)];
        var services = BuildServices(now);

        var summary = JobsOverviewBuilder.Build([.. schedules, .. queues, .. services],
            await jobRuns.GetStatsAsync(now, ct), Infra(), now);
        var recentImports = (await adminRepository.GetImportHistoryAsync()).Take(5).ToList();

        return new BackgroundJobsReport(environment.EnvironmentName, AdminEnabled, BackgroundJobsEnabled,
            schedules, queues, services, summary, recentImports);
    }

    public async Task<JobsSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        // Дашборду не нужны история прогонов и журналы очередей — только состояние и счётчики.
        var now = DateTime.UtcNow;
        var schedules = await BuildSchedulesAsync(_ => [], now, ct);
        IReadOnlyList<JobCard> cards =
            [.. schedules, BuildImportQueue(now), BuildCountryRunQueue(null, now), .. BuildServices(now)];
        return JobsOverviewBuilder.Build(cards, await jobRuns.GetStatsAsync(now, ct), Infra(), now);
    }

    private JobsInfraState Infra()
    {
        var importJobs = importQueue.Snapshot();
        var countryRuns = countryRunQueue.Snapshot();
        var ping = dbStatus.LastPing;
        return new JobsInfraState(
            AdminEnabled,
            environment.IsDevelopment(),
            importJobs.Count(j => j.State == ImportJobState.Queued) + countryRuns.Count(r => r.State == RecordCountryRunState.Queued),
            importJobs.Count(j => j.State == ImportJobState.Running) + countryRuns.Count(r => r.State == RecordCountryRunState.Running),
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
            "Для будущих стартов в окне StartListDaysAhead добывает loglig-id и забирает стартовый " +
            "протокол (CompetitionEntries). Интервал общий с «Входящими».",
            "5 мин", "StartListDaysAhead"),
        [JobNames.LogligVerify] = ("Сверка предложенных loglig-id", "LogligSuggestionVerificationBackgroundService",
            "Берёт loglig-id, предложенные пользователями (Suggested), сверяет карточку loglig " +
            "(имя + год рождения) и переводит в Verified или Rejected.",
            "15 мин", null),
        [JobNames.LogligBatch] = ("Пакетная привязка loglig-id", "LogligBatchBackgroundService",
            "Прогоняет непривязанных пловцов через поиск и сверку. ПЛАТНО: каждый пловец — 1–3 " +
            "запроса Serper. Ручной батч в /Admin/Swimmers/Loglig пишет в историю, расписание не сдвигает.",
            "15 мин", "LogligBatchPerRun"),
    };

    private async Task<IReadOnlyList<JobCard>> BuildSchedulesAsync(
        Func<string, IReadOnlyList<JobRunLogRow>> history, DateTime now, CancellationToken ct)
    {
        var states = (await jobRuns.ListStatesAsync(ct)).ToDictionary(s => s.Name);
        return
        [
            .. JobNames.All.Select(j => BuildSchedule(j, states.GetValueOrDefault(j.Name), history(j.Name), now)),
            BuildTrainingReminders(states.GetValueOrDefault(JobNames.TrainingReminders), history(JobNames.TrainingReminders), now),
        ];
    }

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
        };
    }

    private JobCard BuildSchedule(JobDefinition job, JobRunStateRow? state, IReadOnlyList<JobRunLogRow> history, DateTime now)
    {
        var info = ScheduleInfo[job.Name];
        var processOn = BackgroundJobsEnabled;
        var enabled = job.IsEnabled(settings);
        var interval = job.Interval(settings);
        var s = JobsOverviewBuilder.Schedule(processOn, enabled, job.EnabledKey, state, interval, now);

        var facts = new List<JobFact>
        {
            new("В этом процессе", processOn ? "запущена" : "не запущена — фоновые расписания выключены (BackgroundJobs:Enabled)",
                processOn ? "success" : "neutral"),
            new("Настройка", $"{job.EnabledKey} = {(enabled ? "true" : "false")}" +
                             (settings.Get(job.EnabledKey) is null ? " (в Admin/Settings ключа нет — значение по умолчанию)" : ""),
                enabled ? "success" : "neutral"),
            new("Интервал", $"{JobTimeText.Human(interval)} ({job.IntervalKey}); после неудачи — через " +
                            $"{JobTimeText.Human(JobSchedule.RetryAfterFailure)}; журнал смотрит раз в {info.Tick}"),
        };
        if (info.SettingsExtra is { } extraKey)
            facts.Add(new("Параметр", $"{extraKey} = {settings.Get(extraKey)?.Value ?? "по умолчанию"}"));

        facts.Add(new("Последний запуск", JobTimeText.When(state?.LastAttemptAt, now)));
        facts.Add(new("Последний успех", JobTimeText.When(state?.LastSuccessAt, now)));
        if (s.Failed && state?.LastError is { } error)
            facts.Add(new("Ошибка", error, "danger"));
        facts.Add(new("Следующий",
            !processOn ? "здесь не запустится — расписания работают в ProdAdmin"
            : !enabled ? "не запустится — выключена в настройках"
            : s.NextDueAt is not { } next || next <= now ? "пора — при ближайшей проверке журнала"
            : $"{JobTimeText.Format(next)} ({JobTimeText.Until(next, now)})",
            s.Alert == "warning" ? "warning" : null));

        var badge = !processOn ? new JobBadge("не в этом процессе", "neutral")
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
            Status = s.Status,
            Alert = s.Alert,
        };
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
        };
    }

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
        };
    }

    private static JobFact RegisteredFact(bool registered, string rule) =>
        new("В этом процессе", registered ? $"запущена ({rule})" : $"не запущена — {rule}", registered ? "success" : "neutral");
}
