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
/// П3: запросы обхода стартовых протоколов на НАСТОЯЩЕМ Postgres (рантайм-роль swimm_rw).
/// InMemory не ловит того, что ловит Npgsql: даты с Kind=Unspecified в timestamptz-колонках
/// (<c>DateStart</c>, <c>PulledAt</c>, <c>HeatStartAt</c>) и перевод GroupBy+Min в SQL. Только
/// чтение — локальная база общая с другими сессиями, а запись во «Входящие» сдвинула бы версию
/// данных (<c>Sys_DataVersion_Bump</c>). Сеть и забор — моки. Пропуск, если PG недоступен.
/// </summary>
[Collection(LiveDbCollection.Name)]
public class StartListSchedulePgTests
{
    private const string Runtime =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm_rw;Password=swimm_rw_local_dev";

    private static async Task<bool> Available()
    {
        await using var db = new SwimmDbContext(
            new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql(Runtime).Options);
        try { return await db.Database.CanConnectAsync(); }
        catch { return false; }
    }

    [Fact]
    public async Task Run_on_real_postgres_reads_window_journal_and_first_heats()
    {
        if (!await Available()) return;

        var pull = new Mock<IStartListPullService>();
        pull.Setup(p => p.PullAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => new StartListPullReport(
                id, 1, StartListPullStatus.Ok, null, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, DateTime.UtcNow));
        var discovery = new Mock<ICompetitionDiscoveryService>();

        var services = new ServiceCollection();
        services.AddDbContext<SwimmDbContext>(o => o.UseNpgsql(Runtime));
        services.AddSingleton(pull.Object);
        services.AddSingleton(discovery.Object);
        await using var provider = services.BuildServiceProvider();
        var sweep = new StartListScheduleService(
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<StartListScheduleService>.Instance);

        // Окно в год — чтобы в выборку наверняка попали и старты с журналом, и (если есть)
        // сегодняшние; суточной догрузки нет (прошлый прогон — минуту назад).
        var report = await sweep.RunAsync(daysAhead: 365, previousRunAt: DateTime.UtcNow.AddMinutes(-1));

        Assert.False(report.DetailsRan);
        Assert.Equal(report.Total, report.Due + report.TooEarly + report.Started);
        Assert.Equal(report.Due, report.Pulled);
        pull.Verify(p => p.PullAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(report.Due));
        discovery.VerifyNoOtherCalls();
    }
}
