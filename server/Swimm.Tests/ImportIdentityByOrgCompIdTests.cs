using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Идентичность соревнования по штампу сайта (docs/data-integrity.md, фаза Д2).
///
/// Инцидент И-3: тот же протокол пришёл под названием «…חלק ב'», матчинг по имени промахнулся,
/// и переимпорт создал ПОЛНЫЙ дубликат события на 1837 строк. Теперь при известном compID день
/// ищется ПО ДАТЕ внутри связанного события, а название — косметика.
/// </summary>
public class ImportIdentityByOrgCompIdTests
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

    private static object Item(string competition, string date, string lastName, int lane) => new
    {
        country = "ISR",
        competition,
        date,
        event_style_name = "Freestyle",
        event_style_len = "50",
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

    /// <summary>Двухдневное событие со штампом compID — как после первого импорта из Discovery.</summary>
    private static async Task<(int eventId, int day1, int day2)> SeedEventAsync(SwimmDbContext db, int orgCompId)
    {
        var ev = new CompetitionEvent { Name = "Старое имя", OrgCompId = orgCompId };
        db.CompetitionEvents.Add(ev);
        await db.SaveChangesAsync();

        var d1 = new Competition { Name = "Старое имя", Date = "01/06/2026", PoolType = "25m", EventId = ev.Id, OrgCompId = orgCompId, DayNumber = 1 };
        var d2 = new Competition { Name = "Старое имя", Date = "02/06/2026", PoolType = "25m", EventId = ev.Id, DayNumber = 2 };
        db.Competitions.AddRange(d1, d2);
        await db.SaveChangesAsync();
        return (ev.Id, d1.Id, d2.Id);
    }

    [Fact]
    public async Task Reimport_WithChangedName_MatchesExistingDays_NotCreatesDuplicates()
    {
        await using var db = CreateDb(nameof(Reimport_WithChangedName_MatchesExistingDays_NotCreatesDuplicates));
        var (_, day1, day2) = await SeedEventAsync(db, orgCompId: 6622);

        // Файл пришёл под ДРУГИМ названием — ровно случай И-3.
        var result = await new JsonImportService(db, new NullCache()).ImportAsync(
            ToStream(new[]
            {
                Item("Новое имя часть Б", "01/06/2026", "A", 1),
                Item("Новое имя часть Б", "02/06/2026", "B", 2)
            }),
            "file.json", orgCompId: 6622);

        Assert.Empty(result.ErrorMessages);

        // Ни одного нового соревнования: строки легли в существующие дни.
        var comps = await db.Competitions.ToListAsync();
        Assert.Equal(2, comps.Count);
        Assert.Equal(1, await db.Results.CountAsync(r => r.CompetitionId == day1));
        Assert.Equal(1, await db.Results.CountAsync(r => r.CompetitionId == day2));
    }

    [Fact]
    public async Task Reimport_WithoutOrgCompId_StillMatchesByName()
    {
        // Без штампа (ручной PDF, старый JSON) остаётся прежний матчинг по имени —
        // Д2 ничего не ломает там, где идентичности нет.
        await using var db = CreateDb(nameof(Reimport_WithoutOrgCompId_StillMatchesByName));
        var (_, day1, _) = await SeedEventAsync(db, orgCompId: 6622);

        await new JsonImportService(db, new NullCache()).ImportAsync(
            ToStream(new[] { Item("Старое имя", "01/06/2026", "A", 1) }), "file.json");

        Assert.Equal(2, await db.Competitions.CountAsync());
        Assert.Equal(1, await db.Results.CountAsync(r => r.CompetitionId == day1));
    }

    [Fact]
    public async Task Reimport_NewDayInFile_AddedToSameEvent()
    {
        // День, которого в БД ещё нет, приезжает как новый — но событие остаётся одно.
        await using var db = CreateDb(nameof(Reimport_NewDayInFile_AddedToSameEvent));
        var (eventId, _, _) = await SeedEventAsync(db, orgCompId: 6622);

        await new JsonImportService(db, new NullCache()).ImportAsync(
            ToStream(new[] { Item("Новое имя", "03/06/2026", "C", 3) }),
            "file.json",
            eventOptions: new ImportEventOptions(eventId, null, false, false),
            orgCompId: 6622);

        var comps = await db.Competitions.ToListAsync();
        Assert.Equal(3, comps.Count);
        Assert.All(comps, c => Assert.Equal(eventId, c.EventId));
    }

    [Fact]
    public async Task Import_StampsEvent_NotOnlyFirstDay()
    {
        // До Д2 штамп ложился только на первый день, и дни 2–3 оставались без идентичности.
        await using var db = CreateDb(nameof(Import_StampsEvent_NotOnlyFirstDay));
        var ev = new CompetitionEvent { Name = "Событие" };
        db.CompetitionEvents.Add(ev);
        await db.SaveChangesAsync();

        await new JsonImportService(db, new NullCache()).ImportAsync(
            ToStream(new[]
            {
                Item("День 1", "01/06/2026", "A", 1),
                Item("День 2", "02/06/2026", "B", 2)
            }),
            "file.json",
            eventOptions: new ImportEventOptions(ev.Id, null, false, false),
            orgCompId: 777);

        var saved = await db.CompetitionEvents.SingleAsync();
        Assert.Equal(777, saved.OrgCompId);

        // На соревновании штамп по-прежнему один (альтернативный ключ уникален).
        Assert.Equal(1, await db.Competitions.CountAsync(c => c.OrgCompId == 777));
    }
}
