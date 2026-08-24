using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
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

    public int MaxBatchSize => 30;

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
        public List<BulkPullRowDto> Rows { get; } = [];
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
        var rows = all.Where(d => wanted.Contains(d.Id)).ToList();

        // Тянуть нечего: скрытые админом, с пустым протоколом и уже связанные с БД строкой.
        // Те же правила, что у счётчика долга на чипах месяцев — иначе кнопка и счётчик
        // разойдутся в числах (docs/admin-pages/competitions.md).
        var pullable = rows
            .Where(d => !string.Equals(d.Status, "ignored", StringComparison.OrdinalIgnoreCase))
            .Where(d => d.MatchedCompetitionId is null)
            .ToList();

        var championships = pullable
            .Where(d => CompetitionAdminRepository.IsChampionship(d.Name))
            .ToList();

        if (!includeChampionships)
            pullable = pullable.Except(championships).ToList();

        // Порядок — по дате: разбирать пачку удобнее в том же порядке, в каком она в списке.
        var queue = pullable.OrderBy(d => d.DateStart).Take(MaxBatchSize).ToList();

        var batch = new BatchState
        {
            Id = Guid.NewGuid(),
            Queue = queue,
            SkippedChampionships = includeChampionships ? [] : championships.Select(c => c.Name).ToList()
        };

        if (queue.Count == 0)
        {
            batch.Finished = true;
            _batches[batch.Id] = batch;
            return ToDto(batch, "Нечего тянуть: в выборке нет строк с непустым протоколом.");
        }

        _batches[batch.Id] = batch;
        _logger.LogInformation("Bulk pull: пачка {BatchId} — {Count} строк (чемпионаты {Champ})",
            batch.Id, queue.Count, includeChampionships ? "включены" : "исключены");

        // Фоном и последовательно. CancellationToken запроса сюда НЕ передаём: пачка должна
        // пережить ответ на HTTP-запрос, который её поставил.
        _ = Task.Run(() => RunAsync(batch), CancellationToken.None);

        return ToDto(batch);
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
                    result = await PullOneAsync(row);
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
    /// </summary>
    private async Task<BulkPullRowDto> PullOneAsync(DiscoveredCompetitionDto row)
    {
        using var scope = _scopes.CreateScope();
        var previews = scope.ServiceProvider.GetRequiredService<IDiscoveryPreviewService>();
        var regulations = scope.ServiceProvider.GetRequiredService<IRegulationFetchService>();

        var preview = await previews.PreviewAsync(row.Id, CancellationToken.None);

        // Регламент нужен только тем, кого вообще можно импортировать: у упавшего разбора
        // читать его незачем — строку всё равно разбирать руками.
        RegulationFetchDto? regulation = null;
        if (preview.Error is null)
        {
            var logligId = row.LogligId
                ?? (await scope.ServiceProvider.GetRequiredService<ICompetitionDiscoveryService>()
                        .GetAllAsync(CancellationToken.None))
                    .FirstOrDefault(d => d.Id == row.Id)?.LogligId;

            if (logligId is int id)
                regulation = await regulations.FetchAsync(id, CancellationToken.None);
        }

        var (verdict, reasons) = BulkPullClassifier.Classify(
            preview, regulation, CompetitionAdminRepository.IsChampionship(row.Name));

        return Row(row, verdict, reasons, preview, regulation, preview.PreviewId == Guid.Empty ? null : preview.PreviewId);
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
            IsChampionship: (analysis?.IsChampionship ?? false) || CompetitionAdminRepository.IsChampionship(row.Name),
            PointRuleClubsId: preview?.ClubStanding?.MatchedRuleId,
            PoolType: preview?.Parsed?.Competitions.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.PoolType))?.PoolType);
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

            jobs.Enqueue(
                Encoding.UTF8.GetBytes(entry.Parsed.ResultsJson),
                entry.FileName,
                [DefaultCategoryKey],
                options,
                entry.DiscoveredId,
                row.OrgCompId);

            previews.RemoveEntry(previewId);
            queued++;
        }

        _logger.LogInformation("Bulk pull: пачка {BatchId} — в очередь ушло {Queued}, пропущено {Skipped}",
            batchId, queued, skipped.Count);

        return await Task.FromResult(new BulkImportResultDto(queued, skipped));
    }

    private static BulkPullBatchDto ToDto(BatchState batch, string? error = null)
    {
        List<BulkPullRowDto> rows;
        lock (batch.Rows) rows = [.. batch.Rows];

        return new BulkPullBatchDto(
            batch.Id, batch.Total, rows.Count, batch.Finished, rows, batch.SkippedChampionships, error);
    }
}
