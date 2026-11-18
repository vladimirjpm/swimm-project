using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Swimm.Infrastructure.Services.DataChecks;
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

    private static Swimm.Application.Dtos.ImportEventOptions Upsert() =>
        new(null, null, OverwriteExisting: true, DeleteMissing: false);

    [Fact]
    public async Task Import_AdditionToLoadedDay_NotAMismatch_AndCheckStaysQuiet()
    {
        // Эйлат 05.11.2026 (comp 1874): день затягивали частями — дневная сессия 657, эстафеты
        // +10, вечер +99. Сверка последнего файла против всего дня давала «файл 99, БД 766» —
        // ложную ошибку import.reconciliation-mismatch. Добавка, легшая целиком, — не расхождение.
        await using var db = CreateDb(nameof(Import_AdditionToLoadedDay_NotAMismatch_AndCheckStaysQuiet));
        var service = new JsonImportService(db, new NullCache());

        await service.ImportAsync(ToStream(new[] { Item("A", 1), Item("B", 2) }), "day1.json");
        var addition = await service.ImportAsync(
            ToStream(new[] { Item("C", 1, "Backstroke"), Item("D", 2, "Backstroke"), Item("E", 3, "Backstroke") }),
            "day1-evening.json", eventOptions: Upsert());

        Assert.Equal(0, addition.ReconciliationMismatches);
        Assert.Contains("Добавка к дню", addition.Reconciliation);
        Assert.DoesNotContain("РАСХОЖДЕНИЕ", addition.Reconciliation);

        var outcome = await new ReconciliationMismatchCheck(db).RunAsync();
        Assert.Equal(0, outcome.Total);
    }

    [Fact]
    public async Task Import_FullReimportWithLeftovers_StillAMismatch()
    {
        // Лига 5 (comp 1580): тот же протокол затянут повторно, а в дне остались лишние строки
        // (501 в файле, 515 в БД). День вырос НЕ на размер файла — это не добавка, а то самое
        // расхождение, ради которого сверка существует.
        await using var db = CreateDb(nameof(Import_FullReimportWithLeftovers_StillAMismatch));
        var service = new JsonImportService(db, new NullCache());

        await service.ImportAsync(ToStream(new[] { Item("A", 1), Item("B", 2) }), "meet.json");

        var comp = await db.Competitions.SingleAsync();
        var row = await db.Results.FirstAsync();
        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = row.SwimmerId, ClubId = row.ClubId, StyleId = row.StyleId,
            Distance = "100", Gender = "male", CompetitionDate = row.CompetitionDate
        });
        await db.SaveChangesAsync();

        var again = await service.ImportAsync(ToStream(new[] { Item("A", 1), Item("B", 2) }), "meet.json",
            eventOptions: Upsert());

        Assert.True(again.ReconciliationMismatches > 0);
        Assert.DoesNotContain("Добавка", again.Reconciliation);

        var outcome = await new ReconciliationMismatchCheck(db).RunAsync();
        Assert.Equal(1, outcome.Total);
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
