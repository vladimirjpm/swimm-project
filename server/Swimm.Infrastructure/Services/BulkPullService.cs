using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Application.Validation;
using Swimm.Infrastructure.Repositories;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Пакетное затягивание входящих (docs/plans/bulk-pull-plan.md).
///
/// Синглтон: пачка живёт дольше HTTP-запроса, который её поставил, и её состояние спрашивают
/// поллингом. Всё, что ходит в БД и в сеть, берётся из СВОЕГО scope на каждую строку — так
/// длинная фоновая работа не держит один DbContext на десять минут.
/// </summary>
public sealed class BulkPullService : IBulkPullService
{
    /// <summary>Категория, которую получают все импортируемые пачкой (решение Влада 2026-08-23).</summary>
    public const string DefaultCategoryKey = "results-8-99";

    public int MaxBatchSize => BulkPullLimits.MaxBatchSize;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BulkPullService> _logger;
    private readonly ConcurrentDictionary<Guid, BatchState> _batches = new();

    public BulkPullService(IServiceScopeFactory scopes, ILogger<BulkPullService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    private sealed class BatchState
    {
        public required Guid Id { get; init; }
        public required List<DiscoveredCompetitionDto> Queue { get; init; }
        public required List<string> SkippedChampionships { get; init; }
        public List<string> SkippedNotReady { get; init; } = [];
        public List<BulkPullRowDto> Rows { get; } = [];
        /// <summary>
        /// Строки, уже отправленные в импорт, → задача очереди (под замком <see cref="Rows"/>).
        /// По задаче статус пачки показывает ход импорта и момент, когда он доработал.
        /// </summary>
        public Dictionary<int, Guid> QueuedJobs { get; } = [];
        public volatile bool Finished;
        public int Total => Queue.Count;
    }

    public async Task<BulkPullBatchDto> StartAsync(
        IReadOnlyList<int> discoveredIds, bool includeChampionships, CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var discovery = scope.ServiceProvider.GetRequiredService<ICompetitionDiscoveryService>();
        var all = await discovery.GetAllAsync(ct);

        var wanted = discoveredIds.Distinct().ToHashSet();
        // Потолок — не молчаливое обрезание: в пачку уходит ровно то, что на кнопке, а кнопка сама
        // берёт не больше потолка (BulkPullSelection) и говорит, сколько осталось следующим заходом.
        if (wanted.Count > MaxBatchSize)
            throw new ArgumentOutOfRangeException(nameof(discoveredIds),
                $"В пачке {wanted.Count} строк — больше потолка {MaxBatchSize}. Остальное — следующим заходом.");
        var rows = all.Where(d => wanted.Contains(d.Id)).ToList();

        // Кто попадает в пачку, решает страница правилом BulkPullSelection — сюда приходят уже
        // отобранные id. Вкладка могла простоять час: строку за это время скрыли, пометили «✓ имп.»
        // или «∅», затянули, перенесли (стала «впереди» / «идёт»), results-auto поставил вердикт
        // «не закончено» / «пусто» / «сбой». Поэтому каждая присланная строка ЗАНОВО проверяется
        // тем же правилом на сейчас — по тем же объединённым строкам списка, что у страницы
        // (ICompetitionAdminRepository.GetUnifiedAsync → CompetitionRowStateRules → IsCandidate).
        // Не прошедшие пропускаются и уходят в SkippedNotReady; новых строк проверка не добавляет.
        var stillReady = await StillReadyAsync(scope.ServiceProvider, ct);
        var notReady = rows.Where(d => !stillReady.Contains(d.Id)).ToList();
        var pullable = rows.Except(notReady).ToList();

        var championships = pullable
            .Where(d => CompetitionAdminRepository.IsChampionship(d.Name))
            .ToList();

        if (!includeChampionships)
            pullable = pullable.Except(championships).ToList();

        // Порядок — по дате: разбирать пачку удобнее в том же порядке, в каком она в списке.
        var queue = pullable.OrderBy(d => d.DateStart).ThenBy(d => d.Id).ToList();

        var batch = new BatchState
        {
            Id = Guid.NewGuid(),
            Queue = queue,
            SkippedChampionships = includeChampionships ? [] : championships.Select(c => c.Name).ToList(),
            SkippedNotReady = notReady.Select(d => d.Name).ToList()
        };

        if (queue.Count == 0)
        {
            batch.Finished = true;
            _batches[batch.Id] = batch;
            return ToDto(batch, notReady.Count > 0
                ? "Нечего тянуть: строки кнопки уже не ждут затягивания — обнови страницу (F5)."
                : "Нечего тянуть: в выборке нет строк с непустым протоколом.");
        }

        _batches[batch.Id] = batch;
        _logger.LogInformation("Bulk pull: пачка {BatchId} — {Count} строк (чемпионаты {Champ})",
            batch.Id, queue.Count, includeChampionships ? "включены" : "исключены");

        // Фоном и последовательно. CancellationToken запроса сюда НЕ передаём: пачка должна
        // пережить ответ на HTTP-запрос, который её поставил.
        _ = Task.Run(() => RunAsync(batch), CancellationToken.None);

        return ToDto(batch);
    }

    /// <summary>
    /// DiscoveredId строк, которые СЕЙЧАС «ждут затягивания» и которых нет в базе, — то же правило,
    /// что считает кнопку на странице (<see cref="BulkPullSelection.IsCandidate"/> над строками
    /// списка без фильтров страницы: сезон, месяц, дисциплина и прочее здесь не важны).
    /// </summary>
    private static async Task<HashSet<int>> StillReadyAsync(IServiceProvider sp, CancellationToken ct)
    {
        var repo = sp.GetRequiredService<ICompetitionAdminRepository>();
        var today = CompetitionRowStateRules.IsraelToday(
            (sp.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow());
        var list = await repo.GetUnifiedAsync(null, null, season: null, stage: null, showSynthetic: true,
            month: null, page: 1, pageSize: 20, discipline: "all",
            state: CompetitionStateFilters.Waiting, today: today, unpaged: true);
        return list.Page.Items.Where(BulkPullSelection.IsCandidate).Select(u => u.Site!.DiscoveredId).ToHashSet();
    }

    public BulkPullBatchDto? GetStatus(Guid batchId) =>
        _batches.TryGetValue(batchId, out var batch) ? ToDto(batch) : null;

    private async Task RunAsync(BatchState batch)
    {
        try
        {
            foreach (var row in batch.Queue)
            {
                BulkPullRowDto result;
                try
                {
                    result = (await PullOneAsync(row, CancellationToken.None)).Row;
                }
                catch (Exception ex)
                {
                    // Одна упавшая строка не должна ронять пачку: остальные ещё можно затянуть.
                    _logger.LogWarning(ex, "Bulk pull: строка {Id} упала", row.Id);
                    result = Row(row, BulkPullVerdict.Failed, [$"Сбой затягивания: {ex.Message}"], null, null, null);
                }

                lock (batch.Rows) batch.Rows.Add(result);
            }
        }
        finally
        {
            batch.Finished = true;
            _logger.LogInformation("Bulk pull: пачка {BatchId} закончена", batch.Id);
        }
    }

    /// <summary>
    /// Одна строка: то же самое «Затянуть», что и в одиночной кнопке (общий шов
    /// <see cref="IDiscoveryPreviewService"/>), плюс регламент — и вердикт классификатора.
    /// Пачка зовёт без отмены (она переживает HTTP-запрос), results-auto (П5) — со своим токеном.
    /// </summary>
    public async Task<BulkPullRowPull> PullOneAsync(DiscoveredCompetitionDto row, CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var previews = scope.ServiceProvider.GetRequiredService<IDiscoveryPreviewService>();
        var regulations = scope.ServiceProvider.GetRequiredService<IRegulationFetchService>();

        var preview = await previews.PreviewAsync(row.Id, ct);

        // Регламент нужен только тем, кого вообще можно импортировать: у упавшего разбора
        // читать его незачем — строку всё равно разбирать руками.
        RegulationFetchDto? regulation = null;
        if (preview.Error is null)
        {
            var logligId = row.LogligId
                ?? (await scope.ServiceProvider.GetRequiredService<ICompetitionDiscoveryService>()
                        .GetAllAsync(ct))
                    .FirstOrDefault(d => d.Id == row.Id)?.LogligId;

            if (logligId is int id)
                regulation = await regulations.FetchAsync(id, ct);
        }

        var (verdict, reasons) = BulkPullClassifier.Classify(
            preview, regulation, CompetitionAdminRepository.IsChampionship(row.Name));

        return new BulkPullRowPull(
            Row(row, verdict, reasons, preview, regulation, preview.PreviewId == Guid.Empty ? null : preview.PreviewId),
            preview, regulation);
    }

    private static BulkPullRowDto Row(
        DiscoveredCompetitionDto row,
        BulkPullVerdict verdict,
        IReadOnlyList<string> reasons,
        DiscoveryPreviewResult? preview,
        RegulationFetchDto? regulation,
        Guid? previewId)
    {
        var analysis = regulation?.Analysis;
        var isChampionship = CompetitionAdminRepository.IsChampionship(row.Name, analysis?.IsChampionship ?? false);
        // Бассейн: регламент → справочник бассейнов по месту (П4, ответ уже в превью) → у
        // НЕ-чемпионата честное умолчание 25 м (лиги). Чемпионат и двусмысленное место — null:
        // импорт пачкой такую строку пропустит (И-30, Р-3).
        var pool = PoolTypeChooser.ChooseForBulk(analysis?.PoolType, null, preview?.Flags?.VenuePool, isChampionship);
        return new BulkPullRowDto(
            DiscoveredId: row.Id,
            OrgCompId: row.OrgCompId,
            Name: row.Name,
            Date: row.DateStart.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            Verdict: verdict,
            Reasons: reasons,
            PreviewId: previewId,
            ResultCount: preview?.Parsed?.ResultCount ?? 0,
            RecordCount: preview?.RecordPreview?.Count ?? 0,
            DayCount: preview?.Parsed?.Competitions.Count ?? 0,
            ExistingCompetitionId: preview?.ExistingCompetitionId,
            RegulationUrl: regulation?.Url,
            RegulationFindings: analysis?.Findings ?? [],
            HasMedals: analysis?.HasMedals ?? false,
            HasClubStanding: analysis?.HasClubStanding ?? false,
            IsChampionship: isChampionship,
            PointRuleClubsId: preview?.ClubStanding?.MatchedRuleId,
            PoolType: pool.PoolType,
            PoolTypeSource: pool.Source);
    }

    public async Task<BulkImportResultDto> ImportAsync(
        Guid batchId, IReadOnlyList<int> discoveredIds, CancellationToken ct = default)
    {
        if (!_batches.TryGetValue(batchId, out var batch))
            return new BulkImportResultDto(0, ["Пачка не найдена (перезапуск приложения?) — затяните заново."]);

        using var scope = _scopes.CreateScope();
        var previews = scope.ServiceProvider.GetRequiredService<IDiscoveryPreviewService>();
        var jobs = scope.ServiceProvider.GetRequiredService<IImportJobQueue>();

        var wanted = discoveredIds.Distinct().ToHashSet();
        List<BulkPullRowDto> rows;
        lock (batch.Rows) rows = batch.Rows.Where(r => wanted.Contains(r.DiscoveredId)).ToList();

        var queued = 0;
        var skipped = new List<string>();

        foreach (var row in rows)
        {
            if (row.PreviewId is not Guid previewId)
            {
                skipped.Add($"{row.Name}: затянуть не удалось — импортировать нечего");
                continue;
            }

            var entry = previews.GetEntry(previewId);
            if (entry is null)
            {
                skipped.Add($"{row.Name}: разбор истёк — затяните заново");
                continue;
            }

            if (row.PoolType is null)
            {
                skipped.Add($"{row.Name}: длина бассейна не определена — импортируйте по одной, выбрав 25/50");
                continue;
            }

            // Флаги соревнования едут ТЕМ ЖЕ путём, что и у одиночного превью — опциями
            // импорта. Перезапись и удаление лишнего не применяются НИКОГДА: массового
            // удаления результатов одной кнопкой мы не даём.
            var options = new ImportEventOptions(
                EventId: null,
                NewEventName: row.DayCount > 1 ? row.Name : null,
                OverwriteExisting: false,
                DeleteMissing: false,
                PointRuleClubsId: row.PointRuleClubsId,
                IsAward: row.HasMedals,
                IsChampionship: row.IsChampionship,
                PoolType: row.PoolType);

            var jobId = jobs.Enqueue(
                Encoding.UTF8.GetBytes(entry.Parsed.ResultsJson),
                entry.FileName,
                [DefaultCategoryKey],
                options,
                entry.DiscoveredId,
                row.OrgCompId);

            previews.RemoveEntry(previewId);
            lock (batch.Rows) batch.QueuedJobs[row.DiscoveredId] = jobId;
            queued++;
        }

        _logger.LogInformation("Bulk pull: пачка {BatchId} — в очередь ушло {Queued}, пропущено {Skipped}",
            batchId, queued, skipped.Count);

        return await Task.FromResult(new BulkImportResultDto(queued, skipped));
    }

    private BulkPullBatchDto ToDto(BatchState batch, string? error = null)
    {
        List<(BulkPullRowDto Row, Guid? JobId)> snapshot;
        lock (batch.Rows)
            snapshot = [.. batch.Rows.Select(r =>
                (r, batch.QueuedJobs.TryGetValue(r.DiscoveredId, out var job) ? job : (Guid?)null))];

        // Очередь — синглтон; ход импорта берём у неё на каждый опрос, а не храним копией.
        IImportJobQueue? jobs = null;
        if (snapshot.Any(s => s.JobId != null))
        {
            using var scope = _scopes.CreateScope();
            jobs = scope.ServiceProvider.GetService<IImportJobQueue>();
        }

        var rows = snapshot.Select(s => s.JobId is Guid jobId
                ? WithImportStatus(s.Row with { Queued = true }, jobs?.GetStatus(jobId))
                : s.Row)
            .ToList();
        var pending = rows.Any(r => r.ImportState is "queued" or "running");

        return new BulkPullBatchDto(
            batch.Id, batch.Total, rows.Count, batch.Finished, rows, batch.SkippedChampionships, error, pending,
            [.. batch.Queue.Select(q => q.Id)], batch.SkippedNotReady);
    }

    /// <summary>
    /// Статус задачи очереди → поля строки. Нет статуса (очередь задачу не помнит — перезапуск
    /// API) — строка остаётся просто «отправлена», без хода импорта: ждать её нечего.
    /// </summary>
    internal static BulkPullRowDto WithImportStatus(BulkPullRowDto row, ImportJobStatus? status) => status switch
    {
        null => row,
        { State: ImportJobState.Queued } => row with { ImportState = "queued" },
        { State: ImportJobState.Running } => row with { ImportState = "running" },
        { State: ImportJobState.Failed } => row with { ImportState = "failed", ImportError = status.Error },
        _ => row with
        {
            ImportState = "completed",
            ImportCreated = status.Result?.Created,
            ImportError = status.Result is { Errors: > 0 } r
                ? $"ошибок строк: {r.Errors}" + (r.ErrorMessages.FirstOrDefault() is { } first ? $" — {first}" : "")
                : null,
        },
    };
}
