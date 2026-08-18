using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сверка импорта на живом <see cref="JsonImportService"/> (фаза Д1): пишется ли журнал
/// и видно ли расхождение. Приёмочный критерий Д1 — «кривой импорт заметен БЕЗ участия
/// человека», поэтому проверяем не только счётчики, но и текст в логе импорта.
/// </summary>
public class ImportReconciliationIntegrationTests
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

    private static object Item(string lastName, int lane, string style = "Freestyle", string len = "50") => new
    {
        country = "ISR",
        competition = "Meet",
        date = "01/06/2026",
        event_style_name = style,
        event_style_len = len,
        event_style_gender = "male",
        pool_type = "25m",
        position = lane,
        heat = 1,
        lane,
        last_name = lastName,
        first_name = "Tal",
        birth_year = 2012,
        club = "Club",
        time = "00:30.00"
    };

    private static Stream ToStream(object payload) =>
        new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

    [Fact]
    public async Task Import_WritesReconciliation_EvenWhenEverythingMatches()
    {
        // Журнал ведётся всегда, а не только при проблемах: по нему разбирают инциденты
        // задним числом («что реально приехало в тот раз»).
        await using var db = CreateDb(nameof(Import_WritesReconciliation_EvenWhenEverythingMatches));

        var result = await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(new[] { Item("A", 1), Item("B", 2) }), "meet.json");

        var rows = await db.ImportReconciliations.ToListAsync();
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("ok", r.Status));
        Assert.Equal("meet.json", rows[0].ImportFileName);

        var total = rows.Single(r => r.EventKey.Length == 0);
        Assert.Equal(2, total.ExpectedRows);
        Assert.Equal(2, total.ActualRows);

        Assert.Equal(0, result.ReconciliationMismatches);
        Assert.Contains("сошлось", result.Reconciliation);
    }

    [Fact]
    public async Task Import_LeftoverRowsFromPreviousImport_ReportedAsMismatch()
    {
        // Соревнование уже содержит строки заплыва, которого в файле нет (след прошлого
        // разбора). Импорт их не трогает — сверка обязана это показать.
        await using var db = CreateDb(nameof(Import_LeftoverRowsFromPreviousImport_ReportedAsMismatch));
        var comp = new Competition { Name = "Meet", Date = "01/06/2026", PoolType = "25m" };
        var style = new Style { Name = "individual_medley" };
        var club = new Club { Name = "Club" };
        var swimmer = new Swimmer { LastName = "Old", FirstName = "Row", BirthYear = 2012 };
        db.AddRange(comp, style, club, swimmer);
        await db.SaveChangesAsync();
        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            Distance = "4X50", Gender = "male", CompetitionDate = new DateTime(2026, 6, 1)
        });
        await db.SaveChangesAsync();

        var result = await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(new[] { Item("A", 1) }), "meet.json",
                eventOptions: new Swimm.Application.Dtos.ImportEventOptions(null, null, OverwriteExisting: true, DeleteMissing: false));

        var stale = await db.ImportReconciliations
            .SingleAsync(r => r.EventKey.StartsWith("individual_medley"));

        Assert.Equal(0, stale.ExpectedRows);
        Assert.Equal(1, stale.ActualRows);
        Assert.Equal("mismatch", stale.Status);

        Assert.True(result.ReconciliationMismatches > 0);
        Assert.Contains("РАСХОЖДЕНИЕ", result.Reconciliation);
        Assert.Contains(result.DiagnosticLog, l => l.Contains("РАСХОЖДЕНИЕ"));
    }

    [Fact]
    public async Task Reconciliation_Failure_DoesNotBreakImport()
    {
        // Прибор не имеет права уронить импорт: результаты уже закоммичены.
        // Косвенно: импорт без единой строки не должен падать на сверке.
        await using var db = CreateDb(nameof(Reconciliation_Failure_DoesNotBreakImport));

        var result = await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(System.Array.Empty<object>()), "empty.json");

        Assert.Empty(result.ErrorMessages);
        Assert.Empty(await db.ImportReconciliations.ToListAsync());
    }
}
