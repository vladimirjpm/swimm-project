using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Импорт запускает пересчёт объединённых мест («Combine All Results»). Без него свежее
/// соревнование приезжало с пустым CombinedPlace и тоггл показывал пустую таблицу
/// (docs/points-rules-per-competition-plan.md §3.4 — риск материализации).
///
/// Сам пересчёт (CompetitionRecalculationService) использует ExecuteUpdate и на InMemory
/// не работает, поэтому здесь проверяется ВЫЗОВ — шпионом, а не результат.
/// </summary>
public class ImportRecalculationTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NullCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private sealed class RecalcSpy : ICompetitionRecalculationService
    {
        public List<int> Calls { get; } = [];
        public bool Throw { get; init; }

        public Task<int> RecalculateCompetitionAsync(int competitionId, CancellationToken ct = default)
        {
            Calls.Add(competitionId);
            if (Throw) throw new InvalidOperationException("provider does not support ExecuteUpdate");
            return Task.FromResult(7);
        }

        public Task<int> RecalculateAllCombinedAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private static object Item(string lastName, int lane, string competition = "Combine Meet",
        string date = "01/06/2026") => new
    {
        country = "ISR",
        competition,
        date,
        event_style_name = "Freestyle",
        event_style_len = "50",
        event_style_gender = "male",
        pool_type = "25m",
        position = 1,
        heat = 1,
        lane,
        last_name = lastName,
        first_name = "Tal",
        birth_year = 2005,
        club = "Club",
        time = "00:30.00"
    };

    private static Stream ToStream(object payload) =>
        new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

    [Fact]
    public async Task Import_TriggersRecalculation_ForCreatedCompetition()
    {
        await using var db = CreateDb(nameof(Import_TriggersRecalculation_ForCreatedCompetition));
        var spy = new RecalcSpy();
        var svc = new JsonImportService(db, new NullCache(), spy);

        var result = await svc.ImportAsync(ToStream(new[] { Item("Cohen", lane: 1) }));

        Assert.Empty(result.ErrorMessages);
        var compId = (await db.Competitions.SingleAsync()).Id;
        Assert.Equal([compId], spy.Calls);
        Assert.Contains(result.DiagnosticLog, l => l.Contains("Combine All Results"));
    }

    [Fact]
    public async Task Import_TriggersRecalculation_OncePerTouchedCompetition()
    {
        await using var db = CreateDb(nameof(Import_TriggersRecalculation_OncePerTouchedCompetition));
        var spy = new RecalcSpy();
        var svc = new JsonImportService(db, new NullCache(), spy);

        // Два дня = два соревнования; две строки одного дня не должны давать двойной вызов.
        await svc.ImportAsync(ToStream(new[]
        {
            Item("Cohen", 1, competition: "День 1", date: "01/06/2026"),
            Item("Levi", 2, competition: "День 1", date: "01/06/2026"),
            Item("Dan", 3, competition: "День 2", date: "02/06/2026")
        }));

        var ids = await db.Competitions.Select(c => c.Id).ToListAsync();
        Assert.Equal(2, spy.Calls.Count);
        Assert.Equal(ids.OrderBy(x => x), spy.Calls.OrderBy(x => x));
    }

    [Fact]
    public async Task RecalculationFailure_DoesNotRollBackImport()
    {
        // Импорт к моменту пересчёта уже закоммичен: производная величина не имеет права
        // утащить за собой загруженные результаты.
        await using var db = CreateDb(nameof(RecalculationFailure_DoesNotRollBackImport));
        var svc = new JsonImportService(db, new NullCache(), new RecalcSpy { Throw = true });

        var result = await svc.ImportAsync(ToStream(new[] { Item("Cohen", lane: 1) }));

        Assert.Empty(result.ErrorMessages);
        Assert.Single(await db.Results.ToListAsync());
        Assert.Contains(result.DiagnosticLog, l => l.Contains("пересчёт не удался"));
    }

    [Fact]
    public async Task Import_WithoutRecalculationService_StillImports()
    {
        await using var db = CreateDb(nameof(Import_WithoutRecalculationService_StillImports));
        var svc = new JsonImportService(db, new NullCache());

        var result = await svc.ImportAsync(ToStream(new[] { Item("Cohen", lane: 1) }));

        Assert.Empty(result.ErrorMessages);
        Assert.Single(await db.Results.ToListAsync());
    }
}
