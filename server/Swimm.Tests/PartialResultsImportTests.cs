using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// «Результаты неполные» (Р114) сквозь импорт: <see cref="JsonImportService"/> ставит отметку по
/// строкам дня, повторный импорт (upsert) снимает её полным протоколом и сохраняет «принято как
/// есть» на том же неполном. Строки результатов при этом лежат как напечатаны (Р76).
/// </summary>
public class PartialResultsImportTests
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

    private static object Row(string ev, string style, string last, string? time, bool fail, int heat = 1) => new
    {
        country = "ISR",
        competition = "utest ליגת חורף",
        date = "20/11/2026",
        @event = ev,
        event_style_name = style,
        event_style_len = "50",
        event_style_gender = "female",
        pool_type = "25m",
        position = fail ? 0 : 1,
        heat,
        lane = 1,
        last_name = last,
        first_name = "utest",
        birth_year = 2014,
        club = "utest club",
        time = time ?? "",
        time_fail = fail,
    };

    /// <summary>Два события: вольный проплыт, на спине — ещё нет (или уже да).</summary>
    private static object[] Protocol(bool backstrokeSwum) =>
    [
        Row("50m Freestyle - GIRLS 12", "Freestyle", "אאא", "0:32.10", false),
        Row("50m Freestyle - GIRLS 12", "Freestyle", "בבב", "0:33.20", false),
        Row("50m Backstroke - GIRLS 12", "Backstroke", "אאא", backstrokeSwum ? "0:38.00" : null, !backstrokeSwum),
        Row("50m Backstroke - GIRLS 12", "Backstroke", "בבב", backstrokeSwum ? "0:39.00" : null, !backstrokeSwum),
    ];

    private static Stream ToStream(object payload) =>
        new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

    private static readonly ImportEventOptions Repull = new(null, null, OverwriteExisting: true, DeleteMissing: true);

    private static Task<ImportResult> Import(SwimmDbContext db, bool swum, ImportEventOptions? options = null) =>
        new JsonImportService(db, new NullCache()).ImportAsync(ToStream(Protocol(swum)), eventOptions: options);

    [Fact]
    public async Task Partial_import_marks_the_day_and_full_reimport_clears_it()
    {
        await using var db = CreateDb(nameof(Partial_import_marks_the_day_and_full_reimport_clears_it));

        var first = await Import(db, swum: false);
        Assert.Empty(first.ErrorMessages);
        var c = await db.Competitions.AsNoTracking().SingleAsync();
        Assert.Equal("ждут 1 из 2 событий; 50m Backstroke - GIRLS 12: ещё не плыли (0 из 2 со временем)", c.PartialResults);
        Assert.Equal("Waiting for 1 of 2 events: 50m Backstroke - GIRLS 12", c.PartialResultsEn);
        Assert.NotNull(c.PartialResultsAt);
        Assert.Equal(4, await db.Results.CountAsync()); // непроплытые строки лежат как напечатаны

        var second = await Import(db, swum: true, Repull);
        Assert.Empty(second.ErrorMessages);
        c = await db.Competitions.AsNoTracking().SingleAsync();
        Assert.Null(c.PartialResults);
        Assert.Null(c.PartialResultsEn);
        Assert.Null(c.PartialResultsAt);
    }

    [Fact]
    public async Task Accepted_mark_survives_reimport_of_the_same_partial_protocol()
    {
        await using var db = CreateDb(nameof(Accepted_mark_survives_reimport_of_the_same_partial_protocol));
        await Import(db, swum: false);
        var c = await db.Competitions.SingleAsync();
        PartialResultsRule.Accept(PartialResultsState.Of(c), DateTime.UtcNow).WriteTo(c);
        await db.SaveChangesAsync();
        var acceptedAt = c.PartialResultsAcceptedAt;

        await Import(db, swum: false, Repull);
        db.ChangeTracker.Clear();
        c = await db.Competitions.SingleAsync();
        Assert.NotNull(c.PartialResults);
        Assert.Equal(acceptedAt, c.PartialResultsAcceptedAt);
        Assert.False(PartialResultsState.Of(c).IsShown);

        await Import(db, swum: true, Repull);
        db.ChangeTracker.Clear();
        c = await db.Competitions.SingleAsync();
        Assert.Null(c.PartialResults);
        Assert.Null(c.PartialResultsAcceptedAt);
    }

    [Fact]
    public async Task Subset_import_of_one_kind_does_not_touch_the_mark()
    {
        // Пособытийный забор loglig (только личные / только эстафеты) — не весь протокол.
        await using var db = CreateDb(nameof(Subset_import_of_one_kind_does_not_touch_the_mark));
        await Import(db, swum: false);
        var before = PartialResultsState.Of(await db.Competitions.AsNoTracking().SingleAsync());

        await Import(db, swum: true, Repull with { PreserveRelays = true });
        var after = PartialResultsState.Of(await db.Competitions.AsNoTracking().SingleAsync());

        Assert.Equal(before, after);
    }
}
