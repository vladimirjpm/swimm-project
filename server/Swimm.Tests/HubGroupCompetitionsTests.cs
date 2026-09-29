using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Таб Results группы сквозь репозиторий (`GetPageAsync`): `competitions`, `season_bests` и
/// `bests[].competition_id`. Группировку держат юнит-тесты
/// <see cref="HubGroupCompetitionsBuilderTests"/>; здесь — то, что делает ЗАПРОС:
/// эстафету, чья строка на чужом владельце, добирают по членству в два шага, и рекорд
/// группы знает свой старт.
/// </summary>
public class HubGroupCompetitionsTests
{
    private static SwimmReadDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class SettingsStub : ISettingsService
    {
        public IReadOnlyList<AdminSetting> GetAll() => [];
        public AdminSetting? Get(string key) => null;
        public T GetValue<T>(string key, T fallback) => fallback;
        public bool Update(string key, string newValue) => true;
    }

    private static Swimmer NewSwimmer(string last) =>
        new() { LastName = last, FirstName = "F", LastNameEn = last, FirstNameEn = "F", BirthYear = 1980 };

    private static Competition Day(DateTime date, CompetitionEvent? ev = null) =>
        new()
        {
            Name = ev?.Name ?? "Meet " + date.ToString("dd/MM/yyyy"),
            Date = date.ToString("dd/MM/yyyy"),
            PoolType = "50m",
            IsAward = true,
            Event = ev,
        };

    [Fact]
    public async Task GroupPage_ListsMeets_WithRelayByMembership_RecordsAndSeasonBests()
    {
        await using var db = CreateDb(nameof(GroupPage_ListsMeets_WithRelayByMembership_RecordsAndSeasonBests));
        var club = new Club { Name = "Club", NameEn = "Club" };
        var style = new Style { Name = "freestyle" };
        var group = new HubGroup
        {
            Name = "Dolphins", Slug = "dolphins",
            Owner = new AppUser { Email = "owner@example.com", DisplayName = "Owner", SecurityStamp = "s" },
        };
        Swimmer a = NewSwimmer("A"), b = NewSwimmer("B"), outsider = NewSwimmer("Outsider");
        db.AddRange(club, style, group, a, b, outsider);
        await db.SaveChangesAsync();
        db.HubGroupMembers.AddRange(
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = a.Id, Role = "member" },
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = b.Id, Role = "member" });

        // Прошлый сезон — однодневный старт; этот сезон — двухдневный турнир.
        var seasonStart = SeasonMath.StartOf(SeasonMath.StartYearOf(DateTime.Today));
        var lastSeason = Day(seasonStart.AddMonths(-3));
        var ev = new CompetitionEvent { Name = "Autumn Champs" };
        var day1 = Day(seasonStart.AddDays(1), ev);
        var day2 = Day(seasonStart.AddDays(2), ev);
        // Эстафету на day2 плыли outsider (владелец строки) и B.
        var relay = new Relay { TeamName = "Dolphins", SwimmersName = "Outsider F, B F" };
        db.AddRange(lastSeason, ev, day1, day2, relay);
        await db.SaveChangesAsync();
        db.RelayMembers.AddRange(
            new RelayMember { RelayId = relay.Id, SwimmerId = outsider.Id, LegOrder = 1 },
            new RelayMember { RelayId = relay.Id, SwimmerId = b.Id, LegOrder = 2 });

        ResultRecord Swim(Swimmer s, Competition day, int ms, int? position, Relay? r = null) => new()
        {
            SwimmerId = s.Id, ClubId = club.Id, Competition = day, StyleId = style.Id,
            Distance = r != null ? "4X50" : "50", Gender = "male",
            CompetitionDate = DateTime.ParseExact(day.Date, "dd/MM/yyyy", null),
            Position = position, TimeMillisecond = ms, TimeOriginal = "00:30.00", Relay = r,
        };
        db.Results.AddRange(
            Swim(a, lastSeason, 29_000, 1),              // рекорд группы — в прошлом сезоне
            Swim(a, day1, 30_000, 2),                    // в сезоне, но медленнее рекорда
            Swim(b, day2, 31_000, 1),
            Swim(outsider, day2, 120_000, 3, relay));    // эстафета: строка на чужом
        await db.SaveChangesAsync();

        var page = await new HubGroupPublicRepository(db, db, new SettingsStub()).GetPageAsync(group.Id, group.Slug);

        Assert.NotNull(page);
        var meets = page!.Competitions;
        Assert.Equal(2, meets.Count);

        var champs = meets[0];                           // свежий сверху
        Assert.Equal(ev.Id, champs.EventId);
        Assert.Equal(day2.Id, champs.CompetitionId);
        Assert.Equal("Autumn Champs", champs.Name);
        Assert.Equal(3, champs.Swims);                   // эстафета добрана по членству
        Assert.Equal(2, champs.Swimmers);                // A и B; чужой владелец не считается
        Assert.Equal((1, 1, 1), (champs.Golds, champs.Silvers, champs.Bronzes));
        Assert.Equal(0, champs.Records);                 // B медленнее рекорда A, эстафеты в рекорды не идут

        var old = meets[1];
        Assert.Equal(lastSeason.Id, old.CompetitionId);
        Assert.Equal(1, old.Records);                    // единственный рекорд (50 free) — A в прошлом сезоне

        // Рекорды знают свой день; сумма по стартам = число рекордов.
        Assert.All(page.Bests, bst => Assert.NotEqual(0, bst.CompetitionId));
        Assert.Equal(page.Bests.Count, meets.Sum(m => m.Records));

        // Season bests — только заплывы этого сезона: лучший 50 free тут у A (30.00 на day1).
        var sb = Assert.Single(page.SeasonBests);
        Assert.Equal(a.Id, sb.SwimmerId);
        Assert.Equal(30_000, sb.TimeMillisecond);
        Assert.Equal(day1.Id, sb.CompetitionId);
    }

    [Fact]
    public async Task GroupPage_NoSwims_NoMeets()
    {
        await using var db = CreateDb(nameof(GroupPage_NoSwims_NoMeets));
        var group = new HubGroup
        {
            Name = "Empty", Slug = "empty",
            Owner = new AppUser { Email = "o@example.com", DisplayName = "O", SecurityStamp = "s" },
        };
        var s = NewSwimmer("A");
        db.AddRange(group, s);
        await db.SaveChangesAsync();
        db.HubGroupMembers.Add(new HubGroupMember { HubGroupId = group.Id, SwimmerId = s.Id, Role = "member" });
        await db.SaveChangesAsync();

        var page = await new HubGroupPublicRepository(db, db, new SettingsStub()).GetPageAsync(group.Id, group.Slug);

        Assert.Empty(page!.Competitions);
        Assert.Empty(page.SeasonBests);
    }
}
