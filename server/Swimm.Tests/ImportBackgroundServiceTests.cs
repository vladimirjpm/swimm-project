using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Swimm.API.BackgroundServices;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Пост-шаги фонового импорта (решение Влада 23.11.2026: промежуточные доклеиваются сами, как
/// loglig-id). Служба крутится по-настоящему — своя очередь, свой scope, — подменены только
/// сервисы, которые ходят в базу и на loglig.
/// </summary>
public class ImportBackgroundServiceTests
{
    private const int CompId = 16891;

    private sealed class World
    {
        public readonly ImportJobQueue Queue = new();
        public readonly Mock<IImportService> Import = new();
        public readonly Mock<ISplitAutoAttachService> Splits = new();
        public readonly Mock<ILogligStampService> Stamp = new();
        public readonly Mock<ILogligRowPlayerService> RowPlayers = new();
        public readonly Mock<ICompetitionDiscoveryService> Discovery = new();
        public readonly Mock<IOfficialClubStandingService> Standings = new();
        public readonly Dictionary<string, bool> Settings = new();

        public World(ImportResult result)
        {
            Import.Setup(i => i.ImportAsync(It.IsAny<Stream>(), It.IsAny<string?>(),
                    It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<ImportEventOptions?>(), It.IsAny<int?>()))
                .ReturnsAsync(result);
            Splits.Setup(s => s.AttachAfterImportAsync(It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SplitAutoOutcome(SplitAutoKind.Attached, 12, 8, "отчёт", [1, 2]));
            Stamp.Setup(s => s.StampFromProtocolAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LogligStampReport(0, 0, 0, 0, [], "ok"));
            RowPlayers.Setup(s => s.StampAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LogligRowPlayerReport(true, "ok"));
        }

        public IServiceProvider Services()
        {
            var settings = new Mock<ISettingsService>();
            settings.Setup(s => s.GetValue(It.IsAny<string>(), It.IsAny<bool>()))
                .Returns((string key, bool fallback) => Settings.TryGetValue(key, out var v) ? v : fallback);
            return new ServiceCollection()
                .AddScoped(_ => Import.Object)
                .AddScoped(_ => Splits.Object)
                .AddScoped(_ => Stamp.Object)
                .AddScoped(_ => RowPlayers.Object)
                .AddScoped(_ => Discovery.Object)
                .AddScoped(_ => Standings.Object)
                .AddSingleton(settings.Object)
                .BuildServiceProvider();
        }

        /// <summary>Поставить задачу, прогнать службу и дождаться конца пост-шагов.</summary>
        public async Task<ImportJobStatus> RunAsync(int? orgCompId = CompId, int? discoveredId = 7)
        {
            var sp = Services();
            var service = new ImportBackgroundService(Queue, sp.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<ImportBackgroundService>.Instance);
            await service.StartAsync(CancellationToken.None);
            try
            {
                var jobId = Queue.Enqueue(Encoding.UTF8.GetBytes("[]"), "f.json", null, null, discoveredId, orgCompId);
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < deadline)
                {
                    var s = Queue.GetStatus(jobId)!;
                    if (s.State is ImportJobState.Completed or ImportJobState.Failed)
                    {
                        var logligSteps = orgCompId != null && s.Result != null && ImportPostStepRules.Succeeded(s.Result);
                        var stampDone = Stamp.Invocations.Count > 0 || !Settings.GetValueOrDefault("LogligStampOnImport", true);
                        if (!logligSteps)
                        {
                            // Шагов loglig не будет — даём службе шанс ошибиться (вызвать лишнее).
                            await Task.Delay(150);
                            return s;
                        }
                        var rowPlayersDone = RowPlayers.Invocations.Count > 0 || !Settings.GetValueOrDefault(LogligRowPlayerRules.SettingKey, true);
                        if ((s.Splits is null || s.Splits.State == "done") && stampDone && rowPlayersDone) return s;
                    }
                    await Task.Delay(20);
                }
                throw new TimeoutException("служба импорта не закончила пост-шаги");
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
            }
        }
    }

    private static ImportResult Created(int n) => new() { TotalRows = n, Created = n, Message = $"Import complete: {n} created" };

    [Fact]
    public async Task SuccessfulImport_AttachesSplits_ForItsCompId_ThenStamps()
    {
        var w = new World(Created(418));

        var status = await w.RunAsync();

        w.Splits.Verify(s => s.AttachAfterImportAsync(CompId, status.JobId, It.IsAny<CancellationToken>()), Times.Once);
        w.Stamp.Verify(s => s.StampFromProtocolAsync(CompId, It.IsAny<CancellationToken>()), Times.Once);
        w.Discovery.Verify(d => d.SetStatusAsync(7, "imported", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(ImportJobState.Completed, status.State);
        Assert.Equal("done", status.Splits!.State);
        Assert.Equal("attached", status.Splits.Kind);
        Assert.Equal("доклеено: личных 12, ног эстафет 8", status.Splits.Text);
        Assert.Equal(CompId, status.OrgCompId);
    }

    // Страница перезагружается по Completed: в этот момент «доклейка в очереди» уже должна быть видна
    // (живой случай 23.11.2026 — без этого шаг 9 после reload молчал).
    [Fact]
    public async Task SplitsQueued_IsVisible_AtTheMomentOfCompleted()
    {
        var w = new World(Created(10));
        var release = new TaskCompletionSource<SplitAutoOutcome>();
        w.Splits.Setup(s => s.AttachAfterImportAsync(It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(release.Task);
        var sp = w.Services();
        var service = new ImportBackgroundService(w.Queue, sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ImportBackgroundService>.Instance);
        await service.StartAsync(CancellationToken.None);
        try
        {
            var jobId = w.Queue.Enqueue(Encoding.UTF8.GetBytes("[]"), "f.json", null, null, 7, CompId);
            ImportJobStatus s;
            do { await Task.Delay(1); s = w.Queue.GetStatus(jobId)!; } while (s.State != ImportJobState.Completed);

            Assert.NotNull(s.Splits);
            Assert.NotEqual("done", s.Splits!.State);
        }
        finally
        {
            release.SetResult(new SplitAutoOutcome(SplitAutoKind.NothingOnLoglig, 0, 0, "", []));
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task UpsertRepull_WithOnlyUpdatedRows_CountsAsSuccess()
    {
        var w = new World(new ImportResult { TotalRows = 400, Updated = 400, Message = "Import complete (upsert)" });

        var status = await w.RunAsync();

        w.Splits.Verify(s => s.AttachAfterImportAsync(CompId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("done", status.Splits!.State);
    }

    // Отказ импорта («Дубль», Р119, откат) — ни доклейки, ни штамповки, ни «imported» строке входящих.
    [Theory]
    [InlineData("Дубль: соревнование «X» (01/02/2026) уже содержит результаты в БД.")]
    [InlineData("Свёрнутый протокол (Р119): файл одной датой…")]
    [InlineData("Import rolled back: 3 errors out of 400 rows — no data was saved")]
    public async Task RefusedImport_RunsNoPostSteps(string message)
    {
        var w = new World(new ImportResult { TotalRows = 400, Message = message });

        var status = await w.RunAsync();

        w.Splits.Verify(s => s.AttachAfterImportAsync(It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        w.Stamp.Verify(s => s.StampFromProtocolAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        w.Standings.Verify(s => s.ProbeAndStampAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        w.Discovery.Verify(d => d.SetStatusAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(ImportJobState.Completed, status.State);
        Assert.Null(status.Splits);
    }

    [Fact]
    public async Task SplitsFailure_DoesNotChangeImportOutcome_AndStampStillRuns()
    {
        var w = new World(Created(418));
        w.Splits.Setup(s => s.AttachAfterImportAsync(It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("loglig лёг"));

        var status = await w.RunAsync();

        Assert.Equal(ImportJobState.Completed, status.State);
        Assert.Null(status.Error);
        Assert.Equal(418, status.Result!.Created);
        Assert.Equal("failed", status.Splits!.Kind);
        Assert.Equal("не удалось — повторите кнопкой", status.Splits.Text);
        w.Stamp.Verify(s => s.StampFromProtocolAsync(CompId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ToggleOff_NoSplits_StampStillRuns()
    {
        var w = new World(Created(418));
        w.Settings[SplitAutoAttachRules.SettingKey] = false;

        var status = await w.RunAsync();

        w.Splits.Verify(s => s.AttachAfterImportAsync(It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        w.Stamp.Verify(s => s.StampFromProtocolAsync(CompId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(status.Splits);
    }

    // Игрок loglig у строки (Р122) — третий шаг той же очереди, после штамповки; свой тумблер.
    [Fact]
    public async Task SuccessfulImport_StampsRowPlayers_AfterLogligIds()
    {
        var w = new World(Created(418));
        var order = new List<string>();
        w.Stamp.Setup(s => s.StampFromProtocolAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("stamp")).ReturnsAsync(new LogligStampReport(0, 0, 0, 0, [], "ok"));
        w.RowPlayers.Setup(s => s.StampAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("row-players")).ReturnsAsync(new LogligRowPlayerReport(true, "ok"));

        await w.RunAsync();

        w.RowPlayers.Verify(s => s.StampAsync(CompId, "import", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(["stamp", "row-players"], order);
    }

    [Fact]
    public async Task RowPlayersToggleOff_StepSkipped_OthersRun()
    {
        var w = new World(Created(418));
        w.Settings[LogligRowPlayerRules.SettingKey] = false;

        await w.RunAsync();

        w.RowPlayers.Verify(s => s.StampAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        w.Stamp.Verify(s => s.StampFromProtocolAsync(CompId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Сбой шага импорт не роняет и остальным шагам не мешает.
    [Fact]
    public async Task RowPlayersThrows_ImportStaysCompleted()
    {
        var w = new World(Created(418));
        w.RowPlayers.Setup(s => s.StampAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("сломалось"));

        var status = await w.RunAsync();

        Assert.Equal(ImportJobState.Completed, status.State);
        Assert.Equal("attached", status.Splits!.Kind);
    }

    // Отказ импорта («Дубль», откат) — на loglig не ходим вовсе.
    [Fact]
    public async Task RejectedImport_NoRowPlayers()
    {
        var w = new World(new ImportResult { Message = "Дубль" });

        await w.RunAsync();

        w.RowPlayers.Verify(s => s.StampAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Файл с /Admin/Import — без compID: соревнование не найти, доклейка — кнопкой.
    [Fact]
    public async Task FileImportWithoutCompId_NoSplits()
    {
        var w = new World(Created(100));

        var status = await w.RunAsync(orgCompId: null, discoveredId: null);

        w.Splits.Verify(s => s.AttachAfterImportAsync(It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Null(status.Splits);
    }

    // Пачка: шаги loglig идут по одному — следующая доклейка не начинается, пока идёт предыдущая.
    [Fact]
    public async Task Batch_LogligSteps_RunOneAtATime()
    {
        var w = new World(Created(50));
        var inFlight = 0;
        var maxInFlight = 0;
        w.Splits.Setup(s => s.AttachAfterImportAsync(It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                var now = Interlocked.Increment(ref inFlight);
                lock (w) maxInFlight = Math.Max(maxInFlight, now);
                await Task.Delay(30);
                Interlocked.Decrement(ref inFlight);
                return new SplitAutoOutcome(SplitAutoKind.NothingOnLoglig, 0, 0, "пусто", [1]);
            });

        var sp = w.Services();
        var service = new ImportBackgroundService(w.Queue, sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ImportBackgroundService>.Instance);
        await service.StartAsync(CancellationToken.None);
        var jobs = Enumerable.Range(0, 5)
            .Select(i => w.Queue.Enqueue(Encoding.UTF8.GetBytes("[]"), $"f{i}.json", null, null, i, CompId + i))
            .ToList();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && jobs.Any(j => w.Queue.GetStatus(j)!.Splits?.State != "done"))
            await Task.Delay(20);
        await service.StopAsync(CancellationToken.None);

        Assert.All(jobs, j => Assert.Equal("done", w.Queue.GetStatus(j)!.Splits!.State));
        Assert.Equal(1, maxInFlight);
    }
}
