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
/// «Перезатянуть» многодневки: превью по умолчанию шлёт «новое событие» (NewEventName). Если дни этого
/// compID уже лежат в событии, день, которого в базе нет, должен лечь в то же событие, а не в новое
/// пустое (16862, 27.11.2026: день 3 убрали до проплыва, перезабор после него завёл бы второе событие).
/// </summary>
public class ImportRepullKeepsEventTests
{
    private const int OrgCompId = 16862;

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NullCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static object Item(string lastName, string date, int lane) => new
    {
        country = "ISR", competition = "Millennium", date,
        event_style_name = "Freestyle", event_style_len = "50", event_style_gender = "male",
        pool_type = "25m", position = lane, heat = 1, lane,
        last_name = lastName, first_name = "A", birth_year = 2010, club = "Club", time = "00:30.00"
    };

    private static Stream ThreeDays() => new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[]
    {
        Item("Cohen", "26/11/2026", 1),
        Item("Levi", "27/11/2026", 2),
        Item("Mizrahi", "28/11/2026", 3),
    })));

    private static ImportEventOptions NewEvent(bool overwrite) =>
        new(EventId: null, NewEventName: "Millennium", OverwriteExisting: overwrite);

    [Fact]
    public async Task Repull_NewEventDefault_MissingDayGoesToExistingEvent()
    {
        await using var db = CreateDb(nameof(Repull_NewEventDefault_MissingDayGoesToExistingEvent));
        var svc = new JsonImportService(db, new NullCacheService());

        var first = await svc.ImportAsync(ThreeDays(), eventOptions: NewEvent(false), orgCompId: OrgCompId);
        Assert.Empty(first.ErrorMessages);
        var eventId = (await db.CompetitionEvents.SingleAsync()).Id;

        // День 3 убран до проплыва (как SQL-файлом на проде).
        var day3 = await db.Competitions.SingleAsync(c => c.Date == "28/11/2026");
        db.Results.RemoveRange(db.Results.Where(r => r.CompetitionId == day3.Id));
        db.Competitions.Remove(day3);
        await db.SaveChangesAsync();

        var repull = await svc.ImportAsync(ThreeDays(), eventOptions: NewEvent(true), orgCompId: OrgCompId);

        Assert.Empty(repull.ErrorMessages);
        Assert.Equal(1, await db.CompetitionEvents.CountAsync());
        var days = await db.Competitions.OrderBy(c => c.Date).ToListAsync();
        Assert.Equal(3, days.Count);
        Assert.All(days, d => Assert.Equal(eventId, d.EventId));
        Assert.Equal(3, days.Single(d => d.Date == "28/11/2026").DayNumber);
    }

    [Fact]
    public async Task FirstImport_NoDaysYet_CreatesNewEvent()
    {
        await using var db = CreateDb(nameof(FirstImport_NoDaysYet_CreatesNewEvent));
        var svc = new JsonImportService(db, new NullCacheService());

        await svc.ImportAsync(ThreeDays(), eventOptions: NewEvent(false), orgCompId: OrgCompId);

        var ev = await db.CompetitionEvents.SingleAsync();
        Assert.Equal("Millennium", ev.Name);
        Assert.Equal(new int?[] { 1, 2, 3 },
            (await db.Competitions.OrderBy(c => c.Date).ToListAsync()).Select(c => c.DayNumber).ToArray());
    }
}
