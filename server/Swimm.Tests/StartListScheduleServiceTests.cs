using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Плановый обход стартовых протоколов (docs/plans/start-list-plan.md, шаг С10).
///
/// Логика живёт в сервисе, а не внутри <c>BackgroundService</c>, специально: фоновому
/// сервису в слое API положено быть расписанием, а не бизнес-логикой — иначе он сам ходит
/// в <c>SwimmDbContext</c> мимо правила «API инжектит только интерфейсы Application».
/// Поэтому обход проверяется здесь, без ссылки тестов на веб-проект.
/// </summary>
public class StartListScheduleServiceTests
{
    // «Сегодня» по Израилю, как у самого обхода (П3): с 22:00 до 24:00 UTC это уже завтра.
    private static readonly DateTime Today = Swimm.Application.Mapping.StartListSweepRules.IsraelToday(DateTime.UtcNow);

    private sealed class Harness : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required Mock<IStartListPullService> Pull { get; init; }
        public required Mock<ICompetitionDiscoveryService> Discovery { get; init; }

        public StartListScheduleService Service => new(
            Provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StartListScheduleService>.Instance);

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private static Harness Build(
        string dbName,
        IEnumerable<DiscoveredCompetition> rows,
        Action<Mock<IStartListPullService>>? setupPull = null,
        Action<SwimmDbContext>? seed = null)
    {
        var pull = new Mock<IStartListPullService>();
        pull.Setup(p => p.PullAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => Report(id));
        setupPull?.Invoke(pull);

        var discovery = new Mock<ICompetitionDiscoveryService>();
        discovery.Setup(d => d.RefreshUpcomingDetailsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((3, 1));

        var services = new ServiceCollection();
        services.AddDbContext<SwimmDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton(pull.Object);
        services.AddSingleton(discovery.Object);
        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SwimmDbContext>();
            db.DiscoveredCompetitions.AddRange(rows);
            seed?.Invoke(db);
            db.SaveChanges();
        }

        return new Harness { Provider = provider, Pull = pull, Discovery = discovery };
    }

    private static StartListPullReport Report(int orgCompId) => new(
        orgCompId, 1, StartListPullStatus.Ok, null, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, DateTime.UtcNow);

    private static DiscoveredCompetition Comp(
        int orgCompId, int? logligId, int daysFromToday,
        string status = DiscoveredCompetitionStatus.New) =>
        new()
        {
            OrgCompId = orgCompId,
            LogligId = logligId,
            Name = $"Старт {orgCompId}",
            Status = status,
            DateStart = Today.AddDays(daysFromToday),
            DateEnd = Today.AddDays(daysFromToday)
        };

    [Fact]
    public async Task Run_PullsOnlyFutureStartsThatHaveLogligId()
    {
        await using var h = Build(nameof(Run_PullsOnlyFutureStartsThatHaveLogligId),
        [
            Comp(1, logligId: 100, daysFromToday: 3),      // берём
            Comp(2, logligId: null, daysFromToday: 3),     // без loglig-id — тянуть нечем
            Comp(3, logligId: 300, daysFromToday: -5),     // прошедший — не наше дело
            Comp(4, logligId: 400, daysFromToday: 99),     // за окном
            Comp(5, logligId: 500, daysFromToday: 3, status: DiscoveredCompetitionStatus.Ignored)
        ]);

        var report = await h.Service.RunAsync(daysAhead: 14);

        Assert.Equal(1, report.Total);
        Assert.Equal(1, report.Pulled);
        h.Pull.Verify(p => p.PullAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        h.Pull.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Run_RefreshesDetailsFirst_AndReportsTheirCounts()
    {
        // Сперва добываем logligId тем, у кого его нет (С2) — без этого весь конвейер
        // стартового протокола у будущего старта начать нечем.
        await using var h = Build(nameof(Run_RefreshesDetailsFirst_AndReportsTheirCounts),
            [Comp(1, logligId: 100, daysFromToday: 2)]);

        var report = await h.Service.RunAsync(daysAhead: 7);

        h.Discovery.Verify(d => d.RefreshUpcomingDetailsAsync(7, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(3, report.DetailsChecked);
        Assert.Equal(1, report.DetailsResolved);
    }

    [Fact]
    public async Task Run_OneFailingCompetition_DoesNotStopTheSweep()
    {
        await using var h = Build(nameof(Run_OneFailingCompetition_DoesNotStopTheSweep),
            [Comp(1, 100, 1), Comp(2, 200, 2), Comp(3, 300, 3)],
            pull => pull.Setup(p => p.PullAsync(2, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("сеть отвалилась")));

        var report = await h.Service.RunAsync(daysAhead: 14);

        Assert.Equal(3, report.Total);
        Assert.Equal(2, report.Pulled);   // упавшее не засчитано, остальные прошли
    }

    [Fact]
    public async Task Run_Cancellation_StopsImmediately_NotSwallowed()
    {
        // Отмену глотать нельзя: при остановке приложения обход обязан прекратиться сразу,
        // а не логировать по предупреждению на каждое оставшееся соревнование.
        using var cts = new CancellationTokenSource();
        await using var h = Build(nameof(Run_Cancellation_StopsImmediately_NotSwallowed),
            [Comp(1, 100, 1), Comp(2, 200, 2), Comp(3, 300, 3)],
            pull => pull.Setup(p => p.PullAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int id, CancellationToken _) => { cts.Cancel(); return Report(id); }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => h.Service.RunAsync(daysAhead: 14, ct: cts.Token));

        h.Pull.Verify(p => p.PullAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Run_NothingToPull_IsQuiet()
    {
        await using var h = Build(nameof(Run_NothingToPull_IsQuiet), []);

        var report = await h.Service.RunAsync(daysAhead: 14);

        Assert.Equal(0, report.Total);
        Assert.Equal(0, report.Pulled);
        h.Pull.VerifyNoOtherCalls();
    }

    // ── П3: частота по близости старта (StartListSweepRules) ─────────────

    private static StartListPull PullRow(int orgCompId, TimeSpan ago, string status = StartListPullStatus.Ok, int entries = 50) =>
        new() { OrgCompId = orgCompId, PulledAt = DateTime.UtcNow - ago, Status = status, Events = 10, Entries = entries };

    [Fact]
    public async Task Run_SkipsRecentlyPulled_AndCountsThemAsTooEarly()
    {
        await using var h = Build(nameof(Run_SkipsRecentlyPulled_AndCountsThemAsTooEarly),
            [Comp(1, 100, 5), Comp(2, 200, 5), Comp(3, 300, 1)],
            seed: db => db.StartListPulls.AddRange(
                PullRow(1, TimeSpan.FromHours(2)),                                    // дальний, смотрели 2 ч назад — рано
                PullRow(2, TimeSpan.FromHours(30)),                                   // дальний, вчера — пора
                PullRow(3, TimeSpan.FromHours(2.5), StartListPullStatus.Empty, 0))); // ближайший без посева — пора

        var report = await h.Service.RunAsync(daysAhead: 14);

        Assert.Equal(3, report.Total);
        Assert.Equal(2, report.Due);
        Assert.Equal(1, report.TooEarly);
        Assert.Equal(2, report.Pulled);
        h.Pull.Verify(p => p.PullAsync(2, It.IsAny<CancellationToken>()), Times.Once);
        h.Pull.Verify(p => p.PullAsync(3, It.IsAny<CancellationToken>()), Times.Once);
        h.Pull.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Run_NearStartWithoutLogligId_IsPulled_FarOneWaitsForDetails()
    {
        // Ближайшему без loglig-id забор сам дочитает comp.asp (И-54); дальнему — только
        // суточная догрузка деталей, в Total он не входит.
        await using var h = Build(nameof(Run_NearStartWithoutLogligId_IsPulled_FarOneWaitsForDetails),
            [Comp(1, logligId: null, daysFromToday: 1), Comp(2, logligId: null, daysFromToday: 6)]);

        var report = await h.Service.RunAsync(daysAhead: 14);

        Assert.Equal(1, report.Total);
        h.Pull.Verify(p => p.PullAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        h.Pull.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Run_StartDayAfterFirstHeat_IsLeftAlone()
    {
        await using var h = Build(nameof(Run_StartDayAfterFirstHeat_IsLeftAlone),
            [Comp(1, 100, 0)],
            seed: db => db.CompetitionEntries.Add(new CompetitionEntry
            {
                OrgCompId = 1,
                HeatStartAt = Swimm.Application.Constants.IsraelTime.ToUtc(Today.AddMinutes(1)), // 00:01 по Израилю
            }));

        var report = await h.Service.RunAsync(daysAhead: 14);

        Assert.Equal(1, report.Started);
        Assert.Equal(0, report.Due);
        h.Pull.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Run_DetailsOnlyOnFirstTickOfTheDay()
    {
        await using var h = Build(nameof(Run_DetailsOnlyOnFirstTickOfTheDay), [Comp(1, 100, 3)]);

        // Прошлый успешный прогон — минуту назад (тот же день по Израилю): догрузки нет.
        var report = await h.Service.RunAsync(daysAhead: 14, previousRunAt: DateTime.UtcNow.AddMinutes(-1));
        Assert.False(report.DetailsRan);
        Assert.Equal(0, report.DetailsChecked);
        h.Discovery.Verify(d => d.RefreshUpcomingDetailsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

        // Прошлый — двое суток назад: первый тик дня, догрузка идёт.
        report = await h.Service.RunAsync(daysAhead: 14, previousRunAt: DateTime.UtcNow.AddDays(-2));
        Assert.True(report.DetailsRan);
        h.Discovery.Verify(d => d.RefreshUpcomingDetailsAsync(14, It.IsAny<CancellationToken>()), Times.Once);
    }
}
