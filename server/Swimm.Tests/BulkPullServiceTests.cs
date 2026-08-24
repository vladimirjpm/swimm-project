using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Пакетное затягивание: отбор строк в пачку и правила импорта
/// (docs/plans/bulk-pull-plan.md §1, §5). В сеть не ходим — превью и регламент подменены.
/// </summary>
public class BulkPullServiceTests
{
    // ── фейки зависимостей ────────────────────────────────────────────────────

    private sealed class FakeDiscovery(List<DiscoveredCompetitionDto> rows) : ICompetitionDiscoveryService
    {
        public Task<IReadOnlyList<DiscoveredCompetitionDto>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DiscoveredCompetitionDto>>(rows);

        public Task<DiscoverySyncResult> SyncAsync(int? year = null, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<DiscoveredCompetitionDto?> RefreshDetailsAsync(int id, CancellationToken ct = default)
            => Task.FromResult(rows.FirstOrDefault(r => r.Id == id));
        public Task<bool> SetStatusAsync(int id, string status, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> SetDisciplineAsync(int id, string discipline, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<DiscoveryBackfillRow>> BackfillImportedOrgCompIdsAsync(bool apply, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> AddLanguagesAsync(int id, IEnumerable<string> languages, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> SetLastErrorAsync(int id, string? error, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> SetEmptySourceAsync(int id, bool empty, string by, CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>Превью, которое всегда удаётся: один день, без рекордов и совпадений.</summary>
    private sealed class FakePreviews : IDiscoveryPreviewService
    {
        private readonly Dictionary<Guid, DiscoveryPreviewEntry> _entries = [];
        public List<int> Pulled { get; } = [];

        public TimeSpan EntryLifetime => TimeSpan.FromMinutes(60);

        public Task<DiscoveryPreviewResult> PreviewAsync(int discoveredId, CancellationToken ct = default)
        {
            Pulled.Add(discoveredId);
            var parsed = new ParsedCompetition
            {
                Format = "IsrOrg",
                ResultsJson = """[{"swimmer":"A"}]""",
                ResultCount = 10,
                Competitions = [new ParsedCompetitionSummary("Meet", "01/11/2025", 10)]
            };
            var previewId = Guid.NewGuid();
            _entries[previewId] = new DiscoveryPreviewEntry(parsed, $"file-{discoveredId}.pdf", discoveredId, null);
            return Task.FromResult(new DiscoveryPreviewResult(
                previewId, parsed, ["he"], null, [], new ImportRecordPreviewDto { Count = 0 }, null));
        }

        public DiscoveryPreviewEntry? GetEntry(Guid previewId) => _entries.GetValueOrDefault(previewId);
        public void RemoveEntry(Guid previewId) => _entries.Remove(previewId);
        public Task<DiscoveryProtocolPdf> FetchProtocolAsync(
            int discoveredId, string language, bool refreshIfMissing, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeRegulations : IRegulationFetchService
    {
        public Task<RegulationFetchDto> FetchAsync(int logligId, CancellationToken ct = default)
            => Task.FromResult(new RegulationFetchDto(true, "https://loglig/doc/1",
                new RegulationAnalysisDto(HasMedals: true, HasClubStanding: false, IsChampionship: false, [])));
    }

    private sealed class FakeQueue : IImportJobQueue
    {
        public List<(string FileName, IReadOnlyList<string>? Categories, ImportEventOptions? Options, string Payload)> Jobs { get; } = [];

        public Guid Enqueue(byte[] data, string fileName, IReadOnlyList<string>? categoryKeys = null,
            ImportEventOptions? eventOptions = null, int? discoveredId = null, int? orgCompId = null)
        {
            Jobs.Add((fileName, categoryKeys, eventOptions, System.Text.Encoding.UTF8.GetString(data)));
            return Guid.NewGuid();
        }

        public ImportJobStatus? GetStatus(Guid jobId) => null;
    }

    // ── сборка ────────────────────────────────────────────────────────────────

    private static DiscoveredCompetitionDto Row(
        int id, string name = "ליגה מס 3", string status = "new", int? matchedCompetitionId = null) =>
        new(id, OrgCompId: 16700 + id, name,
            DateStart: new DateTime(2025, 11, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(id),
            DateEnd: new DateTime(2025, 11, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(id),
            Venue: null, LogligId: 12000 + id, Status: status,
            DiscoveredAt: DateTime.UtcNow, LastSeenAt: DateTime.UtcNow, LastError: null,
            MatchedCompetitionName: matchedCompetitionId is null ? null : "уже в БД",
            MatchedCompetitionId: matchedCompetitionId, Languages: null);

    private static (BulkPullService Service, FakeQueue Queue, FakePreviews Previews) Build(
        params DiscoveredCompetitionDto[] rows)
    {
        var queue = new FakeQueue();
        var previews = new FakePreviews();

        var services = new ServiceCollection();
        services.AddSingleton<ICompetitionDiscoveryService>(new FakeDiscovery([.. rows]));
        services.AddSingleton<IDiscoveryPreviewService>(previews);
        services.AddSingleton<IRegulationFetchService>(new FakeRegulations());
        services.AddSingleton<IImportJobQueue>(queue);

        var provider = services.BuildServiceProvider();
        var service = new BulkPullService(
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BulkPullService>.Instance);

        return (service, queue, previews);
    }

    private static async Task<BulkPullBatchDto> WaitFinishedAsync(BulkPullService service, Guid batchId)
    {
        for (var i = 0; i < 100; i++)
        {
            var batch = service.GetStatus(batchId);
            if (batch is { Finished: true }) return batch;
            await Task.Delay(20);
        }

        throw new TimeoutException("Пачка не закончилась за 2 секунды");
    }

    // ── отбор строк ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SkipsChampionships_ByDefault()
    {
        var (service, _, _) = Build(Row(1), Row(2, "אליפות ישראל ארנה חורף 2026"));

        var started = await service.StartAsync([1, 2], includeChampionships: false);
        var batch = await WaitFinishedAsync(service, started.BatchId);

        Assert.Equal(1, batch.Total);
        Assert.Single(batch.SkippedChampionships);
        Assert.DoesNotContain(batch.Rows, r => r.DiscoveredId == 2);
    }

    [Fact]
    public async Task IncludesChampionships_WhenAsked()
    {
        var (service, _, _) = Build(Row(1), Row(2, "אליפות ישראל ארנה חורף 2026"));

        var started = await service.StartAsync([1, 2], includeChampionships: true);
        var batch = await WaitFinishedAsync(service, started.BatchId);

        Assert.Equal(2, batch.Total);
        Assert.Empty(batch.SkippedChampionships);
    }

    [Fact]
    public async Task SkipsHiddenAndAlreadyImported()
    {
        // Скрытая админом строка — решение принято; связанная с БД — уже затянута.
        var (service, _, previews) = Build(
            Row(1),
            Row(2, status: "ignored"),
            Row(3, matchedCompetitionId: 555));

        var started = await service.StartAsync([1, 2, 3], includeChampionships: false);
        await WaitFinishedAsync(service, started.BatchId);

        Assert.Equal([1], previews.Pulled);
    }

    [Fact]
    public async Task CapsBatchSize()
    {
        // 40 строк в выборке — в работу уходит только MaxBatchSize: пачка на весь сезон
        // это получасовой забор и десятки мегабайт разборов в памяти.
        var rows = Enumerable.Range(1, 40).Select(i => Row(i, $"Meet {i}")).ToArray();
        var (service, _, _) = Build(rows);

        var started = await service.StartAsync([.. rows.Select(r => r.Id)], includeChampionships: false);

        Assert.Equal(service.MaxBatchSize, started.Total);
    }

    // ── импорт ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_NeverOverwritesAndAlwaysSetsTheDefaultCategory()
    {
        var (service, queue, _) = Build(Row(1));
        var started = await service.StartAsync([1], includeChampionships: false);
        await WaitFinishedAsync(service, started.BatchId);

        var result = await service.ImportAsync(started.BatchId, [1]);

        Assert.Equal(1, result.Queued);
        var job = Assert.Single(queue.Jobs);
        Assert.Equal([BulkPullService.DefaultCategoryKey], job.Categories);
        Assert.False(job.Options!.OverwriteExisting);
        Assert.False(job.Options.DeleteMissing);
    }

    [Fact]
    public async Task Import_CarriesFlagsFromRegulation()
    {
        var (service, queue, _) = Build(Row(1));
        var started = await service.StartAsync([1], includeChampionships: false);
        await WaitFinishedAsync(service, started.BatchId);

        await service.ImportAsync(started.BatchId, [1]);

        // Регламент фейка говорит про медали → флаг уезжает опцией импорта, тем же путём,
        // каким его отправляет одиночное превью (второго механизма быть не должно).
        var options = Assert.Single(queue.Jobs).Options!;
        Assert.True(options.IsAward);
        Assert.False(options.IsChampionship);
    }

    [Fact]
    public async Task Import_SkipsRowsWhosePreviewExpired()
    {
        var (service, queue, previews) = Build(Row(1));
        var started = await service.StartAsync([1], includeChampionships: false);
        var batch = await WaitFinishedAsync(service, started.BatchId);

        previews.RemoveEntry(batch.Rows[0].PreviewId!.Value);
        var result = await service.ImportAsync(started.BatchId, [1]);

        Assert.Equal(0, result.Queued);
        Assert.Single(result.Skipped);
        Assert.Empty(queue.Jobs);
    }

    [Fact]
    public async Task Import_UnknownBatchIsReportedNotThrown()
    {
        var (service, _, _) = Build(Row(1));

        var result = await service.ImportAsync(Guid.NewGuid(), [1]);

        Assert.Equal(0, result.Queued);
        Assert.Single(result.Skipped);
    }
}
