using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Шов страницы спортсмена (A1): одна выборка заплывов, из которой считают все табы.
/// Держим то, что легко потерять при правках: эстафета видна ноге, а не только «владельцу»
/// строки; у эстафетной строки SwimmerId переписан на запрошенного пловца (иначе PB-детекция
/// ключевалась бы по чужому id); поля соревнования приезжают вместе с заплывом.
/// </summary>
public class SwimmerPageRepositoryTests
{
    private static SwimmReadDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>()
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

    private static ISwimmerPageRepository Repo(SwimmReadDbContext db) =>
        new SwimmerPageRepository(db, new NullCacheService());

    private static Competition Comp(
        string name, string date, string pool = PoolTypes.Short,
        bool championship = false, bool award = true, string? overrideKind = null) => new()
        {
            Name = name,
            Date = date,
            PoolType = pool,
            IsChampionship = championship,
            IsAward = award,
            IsMasters = false,
            StandingKindOverride = overrideKind,
        };

    private static ResultRecord Swim(
        Competition c, Swimmer s, Style st, DateTime date, int ms,
        int? position = null, int points = 0, int? relayId = null) => new()
        {
            CompetitionId = c.Id,
            SwimmerId = s.Id,
            StyleId = st.Id,
            ClubId = 0,
            RelayId = relayId,
            CompetitionDate = date,
            Distance = "100",
            Gender = "male",
            AgeGroup = "11-12",
            EventStyleAge = "12",
            TimeMillisecond = ms,
            TimeOriginal = "01:00.00",
            TimeSplit = "",
            Position = position,
            InternationalPoints = points,
        };

    [Fact]
    public async Task Swims_CarryCompetitionContext_AndAreOrderedByDate()
    {
        await using var db = CreateDb(nameof(Swims_CarryCompetitionContext_AndAreOrderedByDate));
        var style = new Style { Name = "freestyle" };
        var champ = Comp("Winter championship", "16/02/2026", championship: true);
        var league = Comp("League 1", "10/12/2025", pool: PoolTypes.Long);
        var swimmer = new Swimmer { FirstName = "Иван", LastName = "Иванов", BirthYear = 2014 };
        db.AddRange(style, champ, league, swimmer);
        await db.SaveChangesAsync();

        db.AddRange(
            Swim(champ, swimmer, style, new DateTime(2026, 2, 16), 61000, position: 1, points: 500),
            Swim(league, swimmer, style, new DateTime(2025, 12, 10), 62000, position: 4, points: 470));
        await db.SaveChangesAsync();

        var rows = await Repo(db).GetSwimsAsync(swimmer.Id);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateTime(2025, 12, 10), rows[0].CompetitionDate);   // от старых к новым
        Assert.Equal(PoolTypes.Long, rows[0].PoolType);
        Assert.False(rows[0].IsChampionship);
        Assert.Equal("League 1", rows[0].CompetitionName);
        Assert.Equal(4, rows[0].Position);
        Assert.Equal(470, rows[0].InternationalPoints);
        Assert.Equal("freestyle", rows[0].StyleName);
        Assert.True(rows[1].IsChampionship);
        Assert.Equal(PoolTypes.Short, rows[1].PoolType);
    }

    [Fact]
    public async Task Relay_IsVisibleToItsLeg_AndSwimmerIdIsRewritten()
    {
        await using var db = CreateDb(nameof(Relay_IsVisibleToItsLeg_AndSwimmerIdIsRewritten));
        var style = new Style { Name = "freestyle" };
        var comp = Comp("Relay meet", "12/02/2026");
        var owner = new Swimmer { FirstName = "Первая", LastName = "Нога", BirthYear = 2013 };
        var leg = new Swimmer { FirstName = "Вторая", LastName = "Нога", BirthYear = 2014 };
        var relay = new Relay { TeamName = "Team A", SwimmersName = "Первая Нога, Вторая Нога" };
        db.AddRange(style, comp, owner, leg, relay);
        await db.SaveChangesAsync();

        // Строка эстафеты в базе привязана к ПЕРВОЙ ноге — второй участник виден только через
        // RelayMembers (docs/relays.md).
        db.Add(Swim(comp, owner, style, new DateTime(2026, 2, 12), 120000, position: 1, relayId: relay.Id));
        db.Add(new RelayMember { RelayId = relay.Id, SwimmerId = leg.Id, LegOrder = 2 });
        await db.SaveChangesAsync();

        var rows = await Repo(db).GetSwimsAsync(leg.Id);

        var row = Assert.Single(rows);
        Assert.True(row.IsRelay);
        Assert.Equal(leg.Id, row.SwimmerId);                 // не «владелец» строки
        Assert.False(SeasonAggregator.IsCountable(row));     // в best/PB эстафета не идёт
        Assert.Equal(1, row.Position);                       // но медаль у неё есть
    }

    [Fact]
    public async Task Relay_CountedOnce_WhenSwimmerIsBothOwnerAndMember()
    {
        await using var db = CreateDb(nameof(Relay_CountedOnce_WhenSwimmerIsBothOwnerAndMember));
        var style = new Style { Name = "freestyle" };
        var comp = Comp("Relay meet", "12/02/2026");
        var swimmer = new Swimmer { FirstName = "Иван", LastName = "Иванов", BirthYear = 2014 };
        var relay = new Relay { TeamName = "Team A", SwimmersName = "Иван Иванов" };
        db.AddRange(style, comp, swimmer, relay);
        await db.SaveChangesAsync();

        db.Add(Swim(comp, swimmer, style, new DateTime(2026, 2, 12), 120000, position: 2, relayId: relay.Id));
        db.Add(new RelayMember { RelayId = relay.Id, SwimmerId = swimmer.Id, LegOrder = 1 });
        await db.SaveChangesAsync();

        var rows = await Repo(db).GetSwimsAsync(swimmer.Id);

        Assert.Single(rows);
    }

    [Fact]
    public async Task UnknownSwimmer_ReturnsEmpty_NotNull()
    {
        await using var db = CreateDb(nameof(UnknownSwimmer_ReturnsEmpty_NotNull));

        Assert.Empty(await Repo(db).GetSwimsAsync(999));
        Assert.Empty(await Repo(db).GetSwimsAsync(0));
        Assert.Empty(await Repo(db).GetSwimsAsync(-1));
    }

    [Fact]
    public async Task WinterChampionshipDates_OnlyChampionshipsInShortCourse()
    {
        await using var db = CreateDb(nameof(WinterChampionshipDates_OnlyChampionshipsInShortCourse));
        db.AddRange(
            Comp("Winter champs", "16/02/2026", PoolTypes.Short, championship: true),
            Comp("Summer champs", "15/07/2026", PoolTypes.Long, championship: true),
            Comp("League", "10/12/2025", PoolTypes.Short),                       // не чемпионат
            Comp("Open water champs", "27/04/2026", PoolTypes.Short, championship: true,
                overrideKind: StandingKinds.OpenWater));                          // роль переопределена
        await db.SaveChangesAsync();

        var dates = await Repo(db).GetWinterChampionshipDatesAsync();

        Assert.Equal([new DateTime(2026, 2, 16)], dates);
    }
}
