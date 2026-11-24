using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Переимпорт и игрок loglig у строки (Р122): id доказан заплывом, дорожкой и ВРЕМЕНЕМ, поэтому
/// перезалив, изменивший время или статус строки, его снимает (вернёт шаг после импорта), а
/// перезалив без изменений — сохраняет. Смена пловца id не снимает: это и ловит проверка.
/// </summary>
public class LogligPlayerReimportTests
{
    private const int Player = 240949;

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

    private static object Item(string time, bool fail = false) => new
    {
        country = "ISR",
        competition = "גביע ארזי הלבנון",
        date = "06/10/2023",
        event_style_name = "Freestyle",
        event_style_len = "100",
        event_style_gender = "male",
        pool_type = "25m",
        position = 1,
        heat = 2,
        lane = 8,
        last_name = "בדארנה",
        first_name = "מחמוד",
        birth_year = 2008,
        club = "הפועל לב הגליל סחנין",
        time,
        time_fail = fail,
        time_fail_note = fail ? "DQ" : null,
    };

    private static Stream ToStream(object item) =>
        new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { item })));

    /// <summary>Первый импорт + id игрока у строки, как после шага; возвращает id строки.</summary>
    private static async Task<long> SeedAsync(SwimmDbContext db)
    {
        await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(Item("01:15.56")));
        var row = await db.Results.SingleAsync();
        row.LogligPlayerId = Player;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return row.Id;
    }

    private static Task<ImportResult> ReimportAsync(SwimmDbContext db, object item) =>
        new JsonImportService(db, new NullCache()).ImportAsync(
            ToStream(item), "file.json", null, new ImportEventOptions(null, null, OverwriteExisting: true));

    [Fact]
    public async Task Reimport_SameRow_KeepsPlayer()
    {
        await using var db = CreateDb(nameof(Reimport_SameRow_KeepsPlayer));
        var id = await SeedAsync(db);

        var result = await ReimportAsync(db, Item("01:15.56"));

        Assert.Empty(result.ErrorMessages);
        var row = await db.Results.AsNoTracking().SingleAsync();
        Assert.Equal((id, Player), (row.Id, row.LogligPlayerId));
    }

    [Fact]
    public async Task Reimport_TimeChanged_ClearsPlayer()
    {
        await using var db = CreateDb(nameof(Reimport_TimeChanged_ClearsPlayer));
        var id = await SeedAsync(db);

        await ReimportAsync(db, Item("01:16.00"));

        var row = await db.Results.AsNoTracking().SingleAsync(r => r.Id == id);
        Assert.Equal(76000, row.TimeMillisecond);
        Assert.Null(row.LogligPlayerId);
    }

    [Fact]
    public async Task Reimport_StatusChanged_ClearsPlayer()
    {
        await using var db = CreateDb(nameof(Reimport_StatusChanged_ClearsPlayer));
        var id = await SeedAsync(db);

        // Время то же, но строку сняли: статус другой — доказательство прежнее не годится.
        await ReimportAsync(db, Item("01:15.56", fail: true));

        var row = await db.Results.AsNoTracking().SingleAsync(r => r.Id == id);
        Assert.True(row.TimeFail);
        Assert.Null(row.LogligPlayerId);
    }
}
