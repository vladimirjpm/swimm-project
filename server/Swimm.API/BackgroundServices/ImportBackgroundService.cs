using System.Collections.Concurrent;
using Swimm.API.Services;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Services;

namespace Swimm.API.BackgroundServices;

/// <summary>
/// Обрабатывает очередь задач импорта JSON в фоне.
/// Singleton-lifetime через IHostedService; для IImportService (scoped) создаём scope вручную.
///
/// Две последовательные петли:
/// <list type="bullet">
/// <item>импорт — задача за задачей: импорт, статус «imported» строке входящих, проба клубного
///   зачёта (один запрос);</item>
/// <item>шаги, которые ходят на loglig по запросу на заплыв/дисциплину, — доклейка промежуточных
///   и штамповка loglig-id, а за ними, отдельной очередью, игрок loglig у каждой строки (Р122):
///   он самый долгий и идёт, только когда доклейки и штамповки всей пачки закончены и импортов в
///   очереди нет, — шаг игроков одного старта не задерживает промежуточные следующего. Своя очередь с ОДНИМ читателем: на loglig всегда идёт один старт за
///   раз (пачка до 30 — не шквал запросов), а следующий импорт пачки их не ждёт.</item>
/// </list>
/// Все пост-шаги — только после импорта, который что-то записал (<see cref="ImportPostStepRules"/>):
/// отказ («Дубль», Р119, откат) их не запускает. Сбой пост-шага итог импорта не меняет.
/// </summary>
public sealed class ImportBackgroundService : BackgroundService
{
    private readonly ImportJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImportBackgroundService> _logger;

    /// <summary>Что сделать на loglig после импорта задачи <paramref name="JobId"/>.</summary>
    /// <param name="DayId">Р124: день-сосед — шаги только по нему и по <paramref name="LogligId"/> (номер соседа, а не записи федерации).</param>
    internal sealed record LogligStep(Guid JobId, int OrgCompId, bool Splits, bool Stamp, bool RowPlayers = false,
        int? DayId = null, int? LogligId = null);

    // Две очереди одного читателя: промежуточные + штамповка — первыми; игроки по строкам —
    // когда первая пуста и импортов не осталось. Сигнал — на каждую постановку в любую из них.
    private readonly ConcurrentQueue<LogligStep> _logligSteps = new();
    private readonly ConcurrentQueue<LogligStep> _rowPlayerSteps = new();
    private readonly SemaphoreSlim _stepSignal = new(0);

    /// <summary>Как часто ждущие шаги игроков проверяют, кончилась ли пачка импортов.</summary>
    internal static TimeSpan RowPlayersPoll { get; set; } = TimeSpan.FromSeconds(3);

    public ImportBackgroundService(
        IImportJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ImportBackgroundService> logger)
    {
        // IImportJobQueue зарегистрирован как ImportJobQueue (singleton); кастуем безопасно.
        _queue = (ImportJobQueue)queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(RunImportsAsync(stoppingToken), RunLogligStepsAsync(stoppingToken));

    private async Task RunImportsAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var (jobId, data, fileName, categoryKeys, eventOptions, discoveredId, orgCompId) in _queue.ConsumeAsync(stoppingToken))
            {
                // «Импорт занят» — до конца постановки его шагов: шаг игроков прошлого старта не
                // должен вклиниться между Completed и постановкой промежуточных этого.
                Volatile.Write(ref _importBusy, 1);
                try
                {
                _queue.SetRunning(jobId);
                _logger.LogInformation("Import job {JobId} started: {FileName}", jobId, fileName);

                ImportResult result;
                LogligStep? logligStep;
                await using var scope = _scopeFactory.CreateAsyncScope();
                try
                {
                    var importService = scope.ServiceProvider.GetRequiredService<IImportService>();

                    using var stream = new MemoryStream(data, writable: false);
                    result = await importService.ImportAsync(stream, fileName, categoryKeys, eventOptions, orgCompId);

                    // Р124: день-сосед — собрать в событие и аудит ДО Completed: панель по Completed
                    // перезагружается и должна увидеть уже собранное событие.
                    var sibling = eventOptions?.SiblingDay is { } target
                        ? await CompleteSiblingDayAsync(scope.ServiceProvider, jobId, target, result)
                        : null;

                    // «Доклейка в очереди» — ДО Completed: страница перезагружается по Completed и
                    // должна уже видеть, что за импортом идёт доклейка (иначе шаг 9 молчит).
                    logligStep = sibling is { CompetitionId: int dayId }
                        ? PlanLogligStep(scope.ServiceProvider, jobId, result, eventOptions!.SiblingDay!.OrgCompId) is { } s
                            ? s with { DayId = dayId, LogligId = eventOptions.SiblingDay.LogligId }
                            : null
                        : PlanLogligStep(scope.ServiceProvider, jobId, result, orgCompId);
                    _queue.SetCompleted(jobId, result);
                    _logger.LogInformation("Import job {JobId} completed: {Created} created, {Errors} errors",
                        jobId, result.Created, result.Errors);
                }
                catch (Exception ex)
                {
                    _queue.SetFailed(jobId, ex.Message);
                    _logger.LogError(ex, "Import job {JobId} failed", jobId);
                    // Р124: день-сосед — аудит «не записан» и снять замок «день затягивается».
                    if (eventOptions?.SiblingDay is { } failedSibling)
                        await SiblingImportFailedAsync(scope.ServiceProvider, jobId, failedSibling, ex.Message);
                    continue;
                }

                // Импорт уже Completed — что бы ни случилось дальше, его итог не меняется.
                try
                {
                    await AfterImportAsync(scope.ServiceProvider, jobId, result, discoveredId, orgCompId, logligStep, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Import job {JobId}: пост-шаги сорвались", jobId);
                }
                }
                finally
                {
                    Volatile.Write(ref _importBusy, 0);
                    _stepSignal.Release();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Штатная остановка хоста: ReadAllAsync бросает OperationCanceledException при отмене токена.
            // Это не ошибка — просто выходим из цикла.
        }
    }

    private async Task SiblingImportFailedAsync(IServiceProvider sp, Guid jobId, SiblingDayTarget target, string error)
    {
        try
        {
            if (sp.GetService<ICurrentActor>() is HttpCurrentActor actor) actor.BackgroundName = "auto:pull-sibling-day";
            await sp.GetRequiredService<ISiblingDayPullService>().ImportFailedAsync(target, error);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Import job {JobId}: аудит упавшего дня-соседа не записан", jobId);
        }
    }

    /// <summary>
    /// Р124: после импорта дня-соседа — сбор в событие и аудит (<see cref="ISiblingDayPullService.CompleteAsync"/>).
    /// Итог — строкой <see cref="ImportResult.FollowUp"/> (её покажет панель) и в журнал задачи.
    /// Сбой не меняет итог импорта: день записан, собрать его можно руками (--join-days).
    /// </summary>
    private async Task<SiblingDayCompletion?> CompleteSiblingDayAsync(IServiceProvider sp, Guid jobId,
        SiblingDayTarget target, ImportResult result)
    {
        if (sp.GetService<ICurrentActor>() is HttpCurrentActor actor)
            actor.BackgroundName = "auto:pull-sibling-day";
        try
        {
            var done = await sp.GetRequiredService<ISiblingDayPullService>().CompleteAsync(target, result);
            result.FollowUp = done.Message;
            result.FollowUpEventId = done.EventId;
            result.DiagnosticLog.Add("Р124: " + done.Message);
            return done;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Import job {JobId}: день-сосед loglig {LogligId} — сбор в событие сорвался", jobId, target.LogligId);
            result.FollowUp = $"⚠ День записан, но сбор в событие сорвался: {ex.Message}. Соберите руками (--join-days).";
            return null;
        }
    }

    /// <summary>
    /// Какие шаги loglig пойдут за этим импортом; при доклейке — сразу «в очереди» у задачи.
    /// null — никаких: импорт ничего не записал, нет compID или все тумблеры выключены.
    /// Сбой чтения настроек импорт не роняет — шагов просто не будет.
    /// </summary>
    private LogligStep? PlanLogligStep(IServiceProvider sp, Guid jobId, ImportResult result, int? orgCompId)
    {
        if (!ImportPostStepRules.Succeeded(result) || orgCompId is not int compId) return null;
        try
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            var splits = settings.GetValue(SplitAutoAttachRules.SettingKey, true);
            var stamp = settings.GetValue("LogligStampOnImport", true);
            var rowPlayers = settings.GetValue(LogligRowPlayerRules.SettingKey, true);
            if (!splits && !stamp && !rowPlayers) return null;
            if (splits) _queue.SetSplits(jobId, new ImportSplitsStatus { State = "queued" });
            return new LogligStep(jobId, compId, splits, stamp, rowPlayers);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Import job {JobId}: настройки шагов loglig не прочитаны — шаги пропущены", jobId);
            return null;
        }
    }

    /// <summary>
    /// Быстрые пост-шаги в петле импорта и постановка шагов loglig во вторую очередь.
    /// </summary>
    internal async Task AfterImportAsync(IServiceProvider sp, Guid jobId, ImportResult result,
        int? discoveredId, int? orgCompId, LogligStep? logligStep, CancellationToken stoppingToken)
    {
        // Отказ («Дубль», Р119, пустой JSON, откат на ошибках строк) — не импорт: строка входящих
        // не должна стать «✓ имп.», а loglig не должен получить сотню запросов впустую.
        if (!ImportPostStepRules.Succeeded(result))
        {
            _logger.LogInformation("Import job {JobId}: импорт ничего не записал ({Message}) — пост-шаги пропущены",
                jobId, result.Message);
            return;
        }

        if (discoveredId is int id)
        {
            try
            {
                var discovery = sp.GetRequiredService<ICompetitionDiscoveryService>();
                await discovery.SetStatusAsync(id, "imported", stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Import job {JobId}: не удалось проставить статус imported для discovery {DiscoveredId}", jobId, id);
            }
        }

        // Р124: день-сосед идёт без compID (штамп у лежащего дня), его шаги loglig — по дню и номеру соседа.
        if (orgCompId is not int compId)
        {
            if (logligStep is { DayId: not null })
            {
                _logligSteps.Enqueue(logligStep);
                _stepSignal.Release();
            }
            return;
        }

        // Шаги ниже знают соревнование по compID: файл с /Admin/Import (без compID) их не получает.

        // Есть ли у соревнования ОФИЦИАЛЬНЫЙ клубный зачёт (דירוג מועדונים). Знать это
        // нужно сразу: без него наши клубные очки не с чем сверять, а с ним расхождение
        // значимо. Сбой проверки импорт не роняет — флаг просто останется «не проверяли»,
        // добирается прогоном `--probe-club-standings`.
        try
        {
            var standings = sp.GetRequiredService<IOfficialClubStandingService>();
            var probe = await standings.ProbeAndStampAsync(compId, stoppingToken);
            _logger.LogInformation("Import job {JobId}: клубный зачёт compID {OrgCompId} — {Message}",
                jobId, compId, probe?.Message ?? "проверить не удалось");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Import job {JobId}: не удалось проверить клубный зачёт compID {OrgCompId}", jobId, compId);
        }

        if (logligStep is not null)
        {
            _logligSteps.Enqueue(logligStep);
            _stepSignal.Release();
        }
    }

    private async Task RunLogligStepsAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                stoppingToken.ThrowIfCancellationRequested();

                if (_logligSteps.TryDequeue(out var step))
                {
                    await GuardedAsync(step, () => RunLogligStepAsync(step, stoppingToken), stoppingToken);
                    // Игроки этого старта — в свою очередь: дойдут, когда пачка закончится.
                    if (step.RowPlayers) _rowPlayerSteps.Enqueue(step);
                    continue;
                }

                if (!_rowPlayerSteps.IsEmpty && !ImportsPending() && _rowPlayerSteps.TryDequeue(out var rowStep))
                {
                    await GuardedAsync(rowStep, async () =>
                    {
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        await StampRowPlayersAsync(scope.ServiceProvider, rowStep, stoppingToken);
                    }, stoppingToken);
                    continue;
                }

                // Игроки ждут конца пачки — заглядываем периодически; иначе спим до постановки.
                await _stepSignal.WaitAsync(
                    _rowPlayerSteps.IsEmpty ? Timeout.InfiniteTimeSpan : RowPlayersPoll, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Штатная остановка хоста.
        }
    }

    /// <summary>Идёт или ждёт хоть один импорт — пачка не кончилась, шагам игроков рано.</summary>
    private bool ImportsPending() =>
        Volatile.Read(ref _importBusy) == 1
        || _queue.Snapshot().Any(s => s.State is ImportJobState.Queued or ImportJobState.Running);

    private int _importBusy;

    private async Task GuardedAsync(LogligStep step, Func<Task> run, CancellationToken stoppingToken)
    {
        try
        {
            await run();
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Import job {JobId}: шаги loglig сорвались", step.JobId);
        }
    }

    /// <summary>
    /// Шаги loglig одной задачи: сперва промежуточные (их видно на сайте и их итог ждёт шаг 9
    /// чек-листа), потом loglig-id. Друг от друга они не зависят: доклейка сопоставляет по
    /// дисциплине, времени и году рождения, а не по loglig-id пловца.
    ///
    /// У каждого шага СВОЙ scope (свой DbContext): сорвавшаяся посреди записи доклейка не должна
    /// оставить штамповке трекер с недописанными изменениями.
    /// </summary>
    private async Task RunLogligStepAsync(LogligStep step, CancellationToken stoppingToken)
    {
        if (step.Splits)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await AttachSplitsAsync(scope.ServiceProvider, step, stoppingToken);
        }
        if (step.Stamp)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await StampLogligAsync(scope.ServiceProvider, step, stoppingToken);
        }
    }

    // Игрок loglig у каждой личной строки (Р122, И-69): id со страницы дисциплины — по заплыву,
    // дорожке и времени. Сам ничего не чинит: по этим id проверка Health находит карточку, в
    // которой слиты два человека. Последним из шагов loglig — он самый долгий (все страницы
    // дисциплин) и ничьего итога на странице не держит. Сбой — строка аудита и лог; данные
    // доберёт следующий импорт или CLI `--stamp-row-players`.
    //
    // Тумблер перечитывается ЗДЕСЬ, а не только при постановке: шаг может простоять в очереди
    // всю пачку, и выключатель обязан останавливать уже поставленные шаги.
    private async Task StampRowPlayersAsync(IServiceProvider sp, LogligStep step, CancellationToken stoppingToken)
    {
        if (!sp.GetRequiredService<ISettingsService>().GetValue(LogligRowPlayerRules.SettingKey, true))
        {
            _logger.LogInformation("Import job {JobId}: игроки loglig по строкам compID {OrgCompId} — пропущено, шаг выключен в настройках",
                step.JobId, step.OrgCompId);
            return;
        }

        if (sp.GetService<ICurrentActor>() is HttpCurrentActor actor)
            actor.BackgroundName = LogligRowPlayerRules.ActorName;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        cts.CancelAfter(LogligRowPlayerRules.Timeout);
        try
        {
            var players = sp.GetRequiredService<ILogligRowPlayerService>();
            var report = step is { DayId: int day, LogligId: int lg }
                ? await players.StampDayAsync(day, lg, "import", cts.Token, LogligRowPlayerRules.ReadBudget)
                : await players.StampAsync(step.OrgCompId, "import", cts.Token, LogligRowPlayerRules.ReadBudget);
            _logger.LogInformation("Import job {JobId}: игроки loglig по строкам compID {OrgCompId} — {Message}",
                step.JobId, step.OrgCompId, report.Message);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Import job {JobId}: не удалось записать игроков loglig по строкам compID {OrgCompId}",
                step.JobId, step.OrgCompId);
        }
    }

    // Промежуточные — тем же сервисом, что у кнопки «Промежуточные» и CLI `--attach-splits`
    // (решение Влада 23.11.2026: доклеиваются сами, как loglig-id). Импорт их не пишет (решение
    // 20.09.2026), поэтому это отдельный шаг: сбой или таймаут — запись в журнал задачи и
    // «не удалось — повторите кнопкой» у шага 9.
    private async Task AttachSplitsAsync(IServiceProvider sp, LogligStep step, CancellationToken stoppingToken)
    {
        _queue.SetSplits(step.JobId, new ImportSplitsStatus { State = "running" });
        // Аудит автозапуска — актором auto:import, а не «cli».
        if (sp.GetService<ICurrentActor>() is HttpCurrentActor actor)
            actor.BackgroundName = SplitAutoAttachRules.ActorName;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        cts.CancelAfter(SplitAutoAttachRules.Timeout);
        try
        {
            var attach = sp.GetRequiredService<ISplitAutoAttachService>();
            var outcome = step is { DayId: int day, LogligId: int lg }
                ? await attach.AttachDayAfterImportAsync(day, lg, step.JobId, cts.Token)
                : await attach.AttachAfterImportAsync(step.OrgCompId, step.JobId, cts.Token);
            _queue.SetSplits(step.JobId, new ImportSplitsStatus
            {
                State = "done",
                Kind = SplitAutoAttachRules.Code(outcome.Kind),
                Text = SplitAutoAttachRules.Text(outcome.Kind, outcome.SwimWrites, outcome.LegWrites),
                Message = outcome.Message,
            });
            _logger.LogInformation("Import job {JobId}: промежуточные compID {OrgCompId} — {Text}. {Message}",
                step.JobId, step.OrgCompId, SplitAutoAttachRules.Text(outcome.Kind, outcome.SwimWrites, outcome.LegWrites),
                outcome.Message);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _queue.SetSplits(step.JobId, new ImportSplitsStatus
            {
                State = "done",
                Kind = SplitAutoAttachRules.Code(SplitAutoKind.Failed),
                Text = SplitAutoAttachRules.Text(SplitAutoKind.Failed, 0, 0),
                Message = ex.Message,
            });
            _logger.LogWarning(ex, "Import job {JobId}: не удалось доклеить промежуточные compID {OrgCompId}", step.JobId, step.OrgCompId);
        }
    }

    // Loglig-id пловцам — из протокола этого же старта: на странице заплыва имя напечатано
    // ссылкой на карточку. Ручная привязка идёт по одному человеку и потому почти не движется,
    // а без неё не работает ни сверка подозрительных рекордов, ни карточка пловца.
    //
    // Шаг платный (запрос на заплыв, у чемпионата их под сотню), поэтому он ПОСЛЕ импорта и за
    // тумблером: сбой ничего не ломает, привязки просто не появятся — их доберёт следующий
    // импорт или ручная привязка.
    private async Task StampLogligAsync(IServiceProvider sp, LogligStep step, CancellationToken stoppingToken)
    {
        try
        {
            var stamper = sp.GetRequiredService<ILogligStampService>();
            var report = step is { DayId: int day, LogligId: int lg }
                ? await stamper.StampDayFromProtocolAsync(day, lg, stoppingToken)
                : await stamper.StampFromProtocolAsync(step.OrgCompId, stoppingToken);
            _logger.LogInformation("Import job {JobId}: loglig-id compID {OrgCompId} — {Message}",
                step.JobId, step.OrgCompId, report.Message);
            foreach (var skip in report.Skipped.Take(20))
                _logger.LogInformation("Import job {JobId}: loglig-id пропуск — {Skip}", step.JobId, skip);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Import job {JobId}: не удалось проставить loglig-id compID {OrgCompId}", step.JobId, step.OrgCompId);
        }
    }
}
