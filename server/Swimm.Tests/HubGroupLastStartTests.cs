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
/// «Последний старт» группы (<c>last_start</c>, карточка таба Overview).
///
/// Раньше клиент резал его из ленты последних заплывов, обрезанной до 25 строк, и на
/// трёхдневном чемпионате (hapoel-dolphine-netanya-test, 28–30.07.2026) показывал «25 swims ·
/// 1 gold» при 113 заплывах и шести золотах, а пятью строками — 12-е, 11-е и 10-е места.
/// Эти тесты держат: весь турнир, единое правило медали, эстафеты по членству, лучшие сверху.
/// </summary>
public class HubGroupLastStartTests
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

    private sealed class World
    {
        public Club Club = null!;
        public Style Style = null!;
        public HubGroup Group = null!;
        public Swimmer A = null!, B = null!, C = null!, Outsider = null!;
    }

    private static async Task<World> SeedAsync(SwimmDbContext db)
    {
        var w = new World
        {
            Club = new Club { Name = "הפועל דולפין נתניה", NameEn = "Hapoel Dolphine Netanya" },
            Style = new Style { Name = "freestyle" },
            Group = new HubGroup
            {
                Name = "Dolphins", Slug = "dolphins",
                Owner = new AppUser { Email = "owner@example.com", DisplayName = "Owner", SecurityStamp = "s" }
            },
            A = Swimmer("A"), B = Swimmer("B"), C = Swimmer("C"), Outsider = Swimmer("Outsider"),
        };
        db.AddRange(w.Club, w.Style, w.Group, w.A, w.B, w.C, w.Outsider);
        await db.SaveChangesAsync();

        // Ростер — A, B, C. Outsider в группе не состоит.
        foreach (var s in new[] { w.A, w.B, w.C })
            db.HubGroupMembers.Add(new HubGroupMember { HubGroupId = w.Group.Id, SwimmerId = s.Id, Role = "member" });
        await db.SaveChangesAsync();
        return w;
    }

    private static Swimmer Swimmer(string last) =>
        new() { LastName = last, FirstName = "F", LastNameEn = last, FirstNameEn = "F", BirthYear = 2012 };

    private static Competition Day(string date, CompetitionEvent? ev = null, bool isAward = true) =>
        new() { Name = ev?.Name ?? "Meet " + date, Date = date, PoolType = "50m", IsAward = isAward, Event = ev };

    private static ResultRecord Swim(World w, Swimmer s, Competition day, int? position,
        string? heatType = null, string? round = null, bool timeFail = false, Relay? relay = null) =>
        new()
        {
            SwimmerId = s.Id, ClubId = w.Club.Id, Competition = day, StyleId = w.Style.Id,
            Distance = relay != null ? "4X50" : "50", Gender = "male",
            CompetitionDate = DateTime.ParseExact(day.Date, "dd/MM/yyyy", null),
            Position = position, HeatType = heatType, Round = round, TimeFail = timeFail,
            TimeOriginal = timeFail ? "" : "00:30.00", Relay = relay,
        };

    private static HubGroupPublicRepository Repo(SwimmReadDbContext db) => new(db, db, new SettingsStub());

    [Fact]
    public async Task LastStart_CoversWholeMultiDayEvent_WithProductMedalRule()
    {
        await using var db = CreateDb(nameof(LastStart_CoversWholeMultiDayEvent_WithProductMedalRule));
        var w = await SeedAsync(db);

        var ev = new CompetitionEvent { Name = "Summer Champs 2026" };
        var day1 = Day("28/07/2026", ev);
        var day2 = Day("30/07/2026", ev);
        var older = Day("01/06/2026");

        // Эстафету плыл C, а строка — на Outsider (владелец-нога не из ростера).
        var relay = new Relay { TeamName = "Dolphins", SwimmersName = "Outsider F, C F" };
        db.AddRange(ev, day1, day2, older, relay);
        await db.SaveChangesAsync();
        db.RelayMembers.AddRange(
            new RelayMember { RelayId = relay.Id, SwimmerId = w.Outsider.Id, LegOrder = 1 },
            new RelayMember { RelayId = relay.Id, SwimmerId = w.C.Id, LegOrder = 2 });

        db.Results.AddRange(
            Swim(w, w.A, older, 1),                              // прошлый старт — мимо
            Swim(w, w.A, day1, 1, heatType: HeatTypes.Prelim),   // место есть, медали нет (Р34)
            Swim(w, w.B, day1, 1),                               // золото
            Swim(w, w.C, day1, null, timeFail: true),            // снят — в конец
            Swim(w, w.B, day2, 2),                               // серебро
            Swim(w, w.C, day2, 3, round: ResultRounds.FinalOpen),// «כללי» — не медаль (Р43)
            Swim(w, w.Outsider, day2, 3, relay: relay),          // бронза по членству
            Swim(w, w.A, day2, 12));                             // свежая, но 12-я
        await db.SaveChangesAsync();

        var last = (await Repo(db).GetBySlugAsync(w.Group.Slug))!.LastStart;

        Assert.NotNull(last);
        Assert.Equal(ev.Id, last!.EventId);
        Assert.Equal(day2.Id, last.CompetitionId);
        Assert.Equal("Summer Champs 2026", last.Name);
        Assert.Equal("28/07/2026", last.DateFrom);
        Assert.Equal("30/07/2026", last.DateTo);
        Assert.Equal(7, last.Swims);
        Assert.Equal((1, 1, 1), (last.Golds, last.Silvers, last.Bronzes));

        // Медали по местам, потом остальные места по возрастанию; 12-е и снятый не влезли в пять.
        Assert.Equal(
            [(1, false), (2, false), (3, true), (1, false), (3, false)],
            last.Rows.Select(r => (r.Position ?? 0, r.IsRelay)));
        Assert.Equal(HeatTypes.Prelim, last.Rows[3].HeatType);
        Assert.Equal(ResultRounds.FinalOpen, last.Rows[4].Round);
    }

    [Fact]
    public async Task LastStart_SingleDayWithoutAwards_HasNoMedals()
    {
        await using var db = CreateDb(nameof(LastStart_SingleDayWithoutAwards_HasNoMedals));
        var w = await SeedAsync(db);
        var league = Day("10/08/2026", isAward: false);
        db.Add(league);
        await db.SaveChangesAsync();
        db.Results.AddRange(Swim(w, w.A, league, 1), Swim(w, w.B, league, 2));
        await db.SaveChangesAsync();

        var last = (await Repo(db).GetBySlugAsync(w.Group.Slug))!.LastStart;

        Assert.NotNull(last);
        Assert.Null(last!.EventId);
        Assert.Equal(league.Id, last.CompetitionId);
        Assert.Equal(last.DateFrom, last.DateTo);
        Assert.Equal(2, last.Swims);
        // На лиге мест 1–3 сколько угодно, а наград нет.
        Assert.Equal((0, 0, 0), (last.Golds, last.Silvers, last.Bronzes));
        Assert.Equal([1, 2], last.Rows.Select(r => r.Position ?? 0));
    }

    [Fact]
    public async Task LastStart_NullWhenRosterNeverSwam()
    {
        await using var db = CreateDb(nameof(LastStart_NullWhenRosterNeverSwam));
        var w = await SeedAsync(db);

        var page = await Repo(db).GetBySlugAsync(w.Group.Slug);

        Assert.Null(page!.LastStart);
    }
}
