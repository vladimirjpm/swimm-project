using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
        public Task<(int Checked, int Resolved)> RefreshUpcomingDetailsAsync(int daysAhead, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int?> GetOrgCompIdAsync(int id, CancellationToken ct = default)
            => Task.FromResult(rows.FirstOrDefault(r => r.Id == id)?.OrgCompId);
        public Task<bool> SetStatusAsync(int id, string status, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> SetDisciplineAsync(int id, string discipline, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<DiscoveryBackfillRow>> BackfillImportedOrgCompIdsAsync(bool apply, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> AddLanguagesAsync(int id, IEnumerable<string> languages, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> SetLastErrorAsync(int id, string? error, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> SetEmptySourceAsync(int id, bool empty, string by, CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>Превью, которое всегда удаётся: один день, без рекордов и совпадений.</summary>
    internal sealed class FakePreviews : IDiscoveryPreviewService
    {
        private readonly Dictionary<Guid, DiscoveryPreviewEntry> _entries = [];
        public List<int> Pulled { get; } = [];

        /// <summary>Р119: находка «свёрнутый протокол» для каждого превью (null — нет).</summary>
        public Swimm.Application.Validation.CollapsedProtocolFinding? Collapsed { get; set; }

        public TimeSpan EntryLifetime => TimeSpan.FromMinutes(60);

        public Task<DiscoveryPreviewResult> PreviewSiblingAsync(int discoveredId, int siblingLogligId, CancellationToken ct = default) =>
            throw new NotSupportedException();

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
            _entries[previewId] = new DiscoveryPreviewEntry(parsed, $"file-{discoveredId}.pdf", discoveredId, null,
                CollapsedProtocol: Collapsed);
            return Task.FromResult(new DiscoveryPreviewResult(
                previewId, parsed, ["he"], null, [], new ImportRecordPreviewDto { Count = 0 }, null,
                CollapsedProtocol: Collapsed));
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

    internal sealed class FakeQueue : IImportJobQueue
    {
        public List<(string FileName, IReadOnlyList<string>? Categories, ImportEventOptions? Options, string Payload)> Jobs { get; } = [];

        /// <summary>Статусы задач по discoveredId — тест двигает их, как фоновый импорт.</summary>
        public Dictionary<int, ImportJobStatus> ByRow { get; } = [];
        private readonly Dictionary<Guid, ImportJobStatus> _statuses = [];

        public Guid Enqueue(byte[] data, string fileName, IReadOnlyList<string>? categoryKeys = null,
            ImportEventOptions? eventOptions = null, int? discoveredId = null, int? orgCompId = null)
        {
            Jobs.Add((fileName, categoryKeys, eventOptions, System.Text.Encoding.UTF8.GetString(data)));
            var status = new ImportJobStatus { JobId = Guid.NewGuid(), State = ImportJobState.Queued };
            _statuses[status.JobId] = status;
            if (discoveredId is int id) ByRow[id] = status;
            return status.JobId;
        }

        public ImportJobStatus? GetStatus(Guid jobId) => _statuses.GetValueOrDefault(jobId);

        public IReadOnlyList<ImportJobStatus> Snapshot() => [.. _statuses.Values];

        /// <summary>Очередь «забыла» задачи — как после перезапуска API.</summary>
        public void Forget() => _statuses.Clear();
    }

    // ── сборка ────────────────────────────────────────────────────────────────

    internal static DiscoveredCompetitionDto Row(
        int id, string name = "ליגה מס 3", string status = "new", int? matchedCompetitionId = null) =>
        new(id, OrgCompId: 16700 + id, name,
            DateStart: new DateTime(2025, 11, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(id),
            DateEnd: new DateTime(2025, 11, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(id),
            Venue: null, LogligId: 12000 + id, Status: status,
            DiscoveredAt: DateTime.UtcNow, LastSeenAt: DateTime.UtcNow, LastError: null,
            MatchedCompetitionName: matchedCompetitionId is null ? null : "уже в БД",
            MatchedCompetitionId: matchedCompetitionId, Languages: null);

    /// <summary>
    /// Список страницы для перепроверки «ждёт затягивания» в StartAsync: по умолчанию ждут все
    /// строки, кроме скрытых и связанных с базой (как в списке). Тесты самой перепроверки — с
    /// настоящим репозиторием, BulkPullSelectionTests.
    /// </summary>
    internal static ICompetitionAdminRepository ListOf(IEnumerable<DiscoveredCompetitionDto> rows)
    {
        var items = rows.Select(d => new UnifiedCompetitionRowDto
        {
            Db = d.MatchedCompetitionId is null ? null : new CompetitionRowDto(),
            Site = new UnifiedSiteInfo { DiscoveredId = d.Id, Name = d.Name, DateStart = d.DateStart, Status = d.Status },
            Status = new Swimm.Application.Mapping.CompetitionRowStatus(d.Status == "ignored"
                ? Swimm.Application.Mapping.CompetitionRowState.Closed
                : Swimm.Application.Mapping.CompetitionRowState.ReadyToPull),
        }).ToList();
        var repo = new Moq.Mock<ICompetitionAdminRepository>();
        repo.Setup(r => r.GetUnifiedAsync(Moq.It.IsAny<string?>(), Moq.It.IsAny<string?>(), Moq.It.IsAny<int?>(),
                Moq.It.IsAny<string?>(), Moq.It.IsAny<bool>(), Moq.It.IsAny<int?>(), Moq.It.IsAny<int>(), Moq.It.IsAny<int>(),
                Moq.It.IsAny<string?>(), Moq.It.IsAny<string?>(), Moq.It.IsAny<string?>(), Moq.It.IsAny<string?>(),
                Moq.It.IsAny<DateOnly?>(), Moq.It.IsAny<bool>(), false, Moq.It.IsAny<IReadOnlySet<int>?>()))
            .ReturnsAsync(new UnifiedCompetitionList(new PagedResult<UnifiedCompetitionRowDto>(items, items.Count, 1, Math.Max(1, items.Count)),
                new int[12], 0));
        return repo.Object;
    }

    internal static (BulkPullService Service, FakeQueue Queue, FakePreviews Previews) Build(
        params DiscoveredCompetitionDto[] rows) => Build(ListOf(rows), rows);

    internal static (BulkPullService Service, FakeQueue Queue, FakePreviews Previews) Build(
        ICompetitionAdminRepository list, params DiscoveredCompetitionDto[] rows)
    {
        var queue = new FakeQueue();
        var previews = new FakePreviews();

        var services = new ServiceCollection();
        services.AddSingleton(list);
        services.AddSingleton<ICompetitionDiscoveryService>(new FakeDiscovery([.. rows]));
        services.AddSingleton<IDiscoveryPreviewService>(previews);
        services.AddSingleton<IRegulationFetchService>(new FakeRegulations());
        services.AddSingleton<IImportJobQueue>(queue);

        var provider = services.BuildServiceProvider();
        var service = new BulkPullService(
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BulkPullService>.Instance);

        return (service, queue, previews);
    }

    internal static async Task<BulkPullBatchDto> WaitFinishedAsync(BulkPullService service, Guid batchId)
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
    public async Task RejectsMoreThanTheCap_InsteadOfSilentlyCutting()
    {
        // 40 строк — больше потолка: пачка на весь сезон — получасовой забор и десятки мегабайт
        // разборов в памяти. Раньше сервис молча брал первые 30; теперь кнопка страницы сама берёт
        // не больше потолка (BulkPullSelection), а лишнее сервис отвергает, а не режет.
        var rows = Enumerable.Range(1, 40).Select(i => Row(i, $"Meet {i}")).ToArray();
        var (service, _, previews) = Build(rows);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.StartAsync([.. rows.Select(r => r.Id)], includeChampionships: false));
        Assert.Empty(previews.Pulled);

        var started = await service.StartAsync([.. rows.Take(service.MaxBatchSize).Select(r => r.Id)], includeChampionships: false);
        Assert.Equal(service.MaxBatchSize, started.Total);
    }

    [Fact]
    public async Task Status_CarriesTheQueueIds_InPullOrder()
    {
        var (service, _, _) = Build(Row(3), Row(1), Row(2));
        var started = await service.StartAsync([3, 1, 2], includeChampionships: false);
        Assert.Equal([1, 2, 3], started.QueueIds);
        await WaitFinishedAsync(service, started.BatchId);
        Assert.Equal([1, 2, 3], service.GetStatus(started.BatchId)!.QueueIds);
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
    public async Task Import_MarksTheRowQueued_InTheBatchStatus()
    {
        // Без признака панель после перезагрузки рисовала импортированные строки отмеченными,
        // и отработанная пачка выглядела новым предложением (06.10.2026).
        var (service, _, _) = Build(Row(1), Row(2));
        var started = await service.StartAsync([1, 2], includeChampionships: false);
        await WaitFinishedAsync(service, started.BatchId);

        Assert.All(service.GetStatus(started.BatchId)!.Rows, r => Assert.False(r.Queued));

        await service.ImportAsync(started.BatchId, [1]);

        var rows = service.GetStatus(started.BatchId)!.Rows;
        Assert.True(rows.Single(r => r.DiscoveredId == 1).Queued);
        Assert.False(rows.Single(r => r.DiscoveredId == 2).Queued);
    }

    [Fact]
    public async Task Import_StatusFollowsTheQueue_UntilEveryJobIsDone()
    {
        // «В очередь ушло» не значит «импортировано»: панель ждёт, пока доработает последняя
        // задача, и только потом пишет «импорт завершён» (08.10.2026).
        var (service, queue, _) = Build(Row(1), Row(2), Row(3));
        var started = await service.StartAsync([1, 2, 3], includeChampionships: false);
        await WaitFinishedAsync(service, started.BatchId);

        var before = service.GetStatus(started.BatchId)!;
        Assert.False(before.ImportsPending);
        Assert.All(before.Rows, r => Assert.Null(r.ImportState));

        await service.ImportAsync(started.BatchId, [1, 2]);
        BulkPullRowDto RowOf(int id) => service.GetStatus(started.BatchId)!.Rows.Single(r => r.DiscoveredId == id);

        Assert.True(service.GetStatus(started.BatchId)!.ImportsPending);
        Assert.Equal(("queued", "queued", null), (RowOf(1).ImportState, RowOf(2).ImportState, RowOf(3).ImportState));

        queue.ByRow[1].State = ImportJobState.Running;
        Assert.Equal("running", RowOf(1).ImportState);

        queue.ByRow[1].State = ImportJobState.Completed;
        queue.ByRow[1].Result = new ImportResult { Created = 412 };
        queue.ByRow[2].State = ImportJobState.Failed;
        queue.ByRow[2].Error = "Соревнование с таким именем и датой уже есть";

        var done = service.GetStatus(started.BatchId)!;
        Assert.False(done.ImportsPending);
        Assert.Equal(("completed", 412, (string?)null), (RowOf(1).ImportState, RowOf(1).ImportCreated, RowOf(1).ImportError));
        Assert.Equal(("failed", "Соревнование с таким именем и датой уже есть"), (RowOf(2).ImportState, RowOf(2).ImportError));
    }

    [Fact]
    public async Task Import_QueueForgotTheJob_RowStaysSent_NothingPending()
    {
        // Перезапуск API стирает статусы очереди; пачка такую строку не ждёт вечно.
        var (service, queue, _) = Build(Row(1));
        var started = await service.StartAsync([1], includeChampionships: false);
        await WaitFinishedAsync(service, started.BatchId);
        await service.ImportAsync(started.BatchId, [1]);

        queue.Forget();

        var batch = service.GetStatus(started.BatchId)!;
        Assert.False(batch.ImportsPending);
        var row = Assert.Single(batch.Rows);
        Assert.True(row.Queued);
        Assert.Null(row.ImportState);
    }

    [Fact]
    public void WithImportStatus_CompletedWithRowErrors_NamesTheFirst()
    {
        var row = Assert.Single(BuildRows());
        var status = new ImportJobStatus
        {
            State = ImportJobState.Completed,
            Result = new ImportResult { Created = 10, Errors = 2, ErrorMessages = ["строка 7: нет года", "строка 9: нет года"] },
        };

        var withStatus = BulkPullService.WithImportStatus(row, status);

        Assert.Equal(("completed", 10, "ошибок строк: 2 — строка 7: нет года"),
            (withStatus.ImportState, withStatus.ImportCreated, withStatus.ImportError));

        static IEnumerable<BulkPullRowDto> BuildRows() =>
        [
            new(1, 16701, "ליגה", "01/11/2025", BulkPullVerdict.Clean, [], Guid.NewGuid(), 10, 0, 1, null, null, [],
                false, false, false, null, "25m", Queued: true),
        ];
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
