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
/// Э5: выбранные на /Admin/Import правила очков доезжают до создаваемых соревнований.
/// Правило живёт у соревнования, а не у события, поэтому проставляется КАЖДОМУ дню.
/// Правила карточек и подбора — docs/competition-overview-cards.md.
/// </summary>
public class ImportPointRulesTests
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

    private static object Item(string lastName, int lane, string competition = "Comp", string date = "01/06/2026") => new
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

    private static async Task SeedRulesAsync(SwimmDbContext db)
    {
        db.PointRulesClubs.Add(new PointRuleClubs
        {
            Id = 1, Version = "2026.01", Scope = "all", EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        db.PointRulesSwimmers.Add(new PointRuleSwimmers
        {
            Id = 1, Version = "2026.01-hp", Scope = "all", EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Import_StampsSelectedRules_OnCreatedCompetition()
    {
        await using var db = CreateDb(nameof(Import_StampsSelectedRules_OnCreatedCompetition));
        await SeedRulesAsync(db);
        var svc = new JsonImportService(db, new NullCache());

        var result = await svc.ImportAsync(
            ToStream(new[] { Item("Cohen", lane: 1) }),
            eventOptions: new ImportEventOptions(null, null, PointRuleClubsId: 1, PointRuleSwimmersId: 1));

        Assert.Empty(result.ErrorMessages);
        var comp = await db.Competitions.SingleAsync();
        Assert.Equal(1, comp.PointRuleClubsId);
        Assert.Equal(1, comp.PointRuleSwimmersId);
    }

    [Fact]
    public async Task Import_WithoutRules_LeavesAuto()
    {
        await using var db = CreateDb(nameof(Import_WithoutRules_LeavesAuto));
        await SeedRulesAsync(db);
        var svc = new JsonImportService(db, new NullCache());

        var result = await svc.ImportAsync(ToStream(new[] { Item("Cohen", lane: 1) }));

        Assert.Empty(result.ErrorMessages);
        var comp = await db.Competitions.SingleAsync();
        Assert.Null(comp.PointRuleClubsId);
        Assert.Null(comp.PointRuleSwimmersId);
    }

    [Fact]
    public async Task Import_OnlyClubsRule_LeavesSwimmersAuto()
    {
        await using var db = CreateDb(nameof(Import_OnlyClubsRule_LeavesSwimmersAuto));
        await SeedRulesAsync(db);
        var svc = new JsonImportService(db, new NullCache());

        await svc.ImportAsync(
            ToStream(new[] { Item("Cohen", lane: 1) }),
            eventOptions: new ImportEventOptions(null, null, PointRuleClubsId: 1));

        var comp = await db.Competitions.SingleAsync();
        Assert.Equal(1, comp.PointRuleClubsId);
        Assert.Null(comp.PointRuleSwimmersId);
    }

    [Fact]
    public async Task Import_MultiDayEvent_StampsEveryDay()
    {
        await using var db = CreateDb(nameof(Import_MultiDayEvent_StampsEveryDay));
        await SeedRulesAsync(db);
        var svc = new JsonImportService(db, new NullCache());
        var rules = new ImportEventOptions(null, "Многодневка", PointRuleClubsId: 1, PointRuleSwimmersId: 1);

        // Первый день создаёт событие, второй прицепляется к нему по EventId.
        await svc.ImportAsync(ToStream(new[] { Item("Cohen", 1, competition: "День 1", date: "01/06/2026") }), eventOptions: rules);
        var eventId = (await db.CompetitionEvents.SingleAsync()).Id;
        await svc.ImportAsync(
            ToStream(new[] { Item("Levi", 2, competition: "День 2", date: "02/06/2026") }),
            eventOptions: new ImportEventOptions(eventId, null, PointRuleClubsId: 1, PointRuleSwimmersId: 1));

        var comps = await db.Competitions.OrderBy(c => c.DayNumber).ToListAsync();
        Assert.Equal(2, comps.Count);
        Assert.All(comps, c =>
        {
            Assert.Equal(eventId, c.EventId);
            Assert.Equal(1, c.PointRuleClubsId);
            Assert.Equal(1, c.PointRuleSwimmersId);
        });
    }

    [Fact]
    public async Task Reimport_DoesNotChangeExistingBinding()
    {
        await using var db = CreateDb(nameof(Reimport_DoesNotChangeExistingBinding));
        await SeedRulesAsync(db);
        db.PointRulesClubs.Add(new PointRuleClubs
        {
            Id = 2, Version = "2026.02", Scope = "all", EffectiveFrom = new DateOnly(2026, 2, 1)
        });
        await db.SaveChangesAsync();
        var svc = new JsonImportService(db, new NullCache());

        await svc.ImportAsync(
            ToStream(new[] { Item("Cohen", lane: 1) }),
            eventOptions: new ImportEventOptions(null, null, PointRuleClubsId: 1));

        // Переимпорт того же соревнования с другим правилом: привязку не переписываем —
        // менять её осознанно можно на /Admin/Competitions.
        await svc.ImportAsync(
            ToStream(new[] { Item("Cohen", lane: 1) }),
            eventOptions: new ImportEventOptions(null, null, OverwriteExisting: true, PointRuleClubsId: 2));

        var comp = await db.Competitions.SingleAsync();
        Assert.Equal(1, comp.PointRuleClubsId);
    }
}
