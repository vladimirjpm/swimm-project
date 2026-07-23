using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты <see cref="RelayMemberBackfillService"/>: матчинг ног из Relay.SwimmersName
/// по ростеру соревнования, якорь-владелец, fail-safe на несопоставимых именах,
/// идемпотентность и dry-run.
/// </summary>
public class RelayMemberBackfillServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static Swimmer NewSwimmer(string last, string first) =>
        new() { LastName = last, FirstName = first, LastNameEn = "", FirstNameEn = "", BirthYear = 2010 };

    /// <summary>Готовит соревнование, клуб, стиль и ростер из индивидуальных результатов.</summary>
    private static async Task<(Competition comp, Club club, Style style)> SeedMeetAsync(
        SwimmDbContext db, IEnumerable<Swimmer> roster)
    {
        var style = new Style { Name = "individual_medley" };
        var club = new Club { Name = "Dolphin", NameEn = "Dolphin" };
        var comp = new Competition { Name = "Champs", Date = "01/10/2025", PoolType = "50m" };
        db.Styles.Add(style); db.Clubs.Add(club); db.Competitions.Add(comp);
        await db.SaveChangesAsync();

        foreach (var s in roster)
            db.Results.Add(new ResultRecord
            {
                SwimmerId = s.Id, CompetitionId = comp.Id, ClubId = club.Id, StyleId = style.Id,
                Distance = "50", Gender = "female",
                CompetitionDate = DateTime.SpecifyKind(new DateTime(2025, 10, 1), DateTimeKind.Unspecified),
                Position = 1, TimeMillisecond = 40000, TimeOriginal = "00:40.00",
            });
        await db.SaveChangesAsync();
        return (comp, club, style);
    }

    [Fact]
    public async Task Backfill_LinksAllLegsFromRoster_ByName()
    {
        await using var db = CreateDb(nameof(Backfill_LinksAllLegsFromRoster_ByName));
        var mia = NewSwimmer("חיינובסקי", "מיה");
        var tahel = NewSwimmer("היבל", "תהל");
        var sabina = NewSwimmer("ברנצב", "סבינה");
        var yaela = NewSwimmer("בוצין", "יעלה");
        db.Swimmers.AddRange(mia, tahel, sabina, yaela);
        await db.SaveChangesAsync();
        var (comp, club, style) = await SeedMeetAsync(db, new[] { mia, tahel, sabina, yaela });

        // Эстафета: владелец — Mia (первая нога); текст «Имя Фамилия» через запятую.
        var relay = new Relay { TeamName = "Dolphin", SwimmersName = "מיה חיינובסקי, תהל היבל, סבינה ברנצב, יעלה בוצין" };
        db.Relays.Add(relay);
        await db.SaveChangesAsync();
        db.Results.Add(new ResultRecord
        {
            SwimmerId = mia.Id, CompetitionId = comp.Id, ClubId = club.Id, StyleId = style.Id,
            Distance = "4X50", Gender = "female", RelayId = relay.Id,
            CompetitionDate = DateTime.SpecifyKind(new DateTime(2025, 10, 1), DateTimeKind.Unspecified),
            Position = 1, TimeMillisecond = 215030, TimeOriginal = "03:35.03",
        });
        await db.SaveChangesAsync();

        var report = await new RelayMemberBackfillService(db).BackfillAsync(apply: true);

        Assert.True(report.Applied);
        Assert.Equal(4, report.LegsLinked);
        Assert.Equal(0, report.LegsUnmatched);

        var members = await db.RelayMembers.Where(m => m.RelayId == relay.Id).ToListAsync();
        Assert.Equal(4, members.Count);
        Assert.Contains(members, m => m.SwimmerId == sabina.Id); // нога-не-владелец связана
    }

    [Fact]
    public async Task Backfill_DryRun_DoesNotWrite()
    {
        await using var db = CreateDb(nameof(Backfill_DryRun_DoesNotWrite));
        var mia = NewSwimmer("חיינובסקי", "מיה");
        db.Swimmers.Add(mia);
        await db.SaveChangesAsync();
        var (comp, club, style) = await SeedMeetAsync(db, new[] { mia });
        var relay = new Relay { TeamName = "Dolphin", SwimmersName = "מיה חיינובסקי" };
        db.Relays.Add(relay);
        await db.SaveChangesAsync();
        db.Results.Add(new ResultRecord
        {
            SwimmerId = mia.Id, CompetitionId = comp.Id, ClubId = club.Id, StyleId = style.Id,
            Distance = "4X50", RelayId = relay.Id, Gender = "female",
            CompetitionDate = DateTime.SpecifyKind(new DateTime(2025, 10, 1), DateTimeKind.Unspecified),
            Position = 1, TimeMillisecond = 100000, TimeOriginal = "01:40.00",
        });
        await db.SaveChangesAsync();

        var report = await new RelayMemberBackfillService(db).BackfillAsync(apply: false);

        Assert.False(report.Applied);
        Assert.True(report.LegsLinked > 0);
        Assert.Empty(await db.RelayMembers.ToListAsync()); // dry-run ничего не записал
    }

    [Fact]
    public async Task Backfill_OwnerAnchored_EvenWhenNameUnmatched()
    {
        await using var db = CreateDb(nameof(Backfill_OwnerAnchored_EvenWhenNameUnmatched));
        var owner = NewSwimmer("קפטן", "דנה");
        db.Swimmers.Add(owner);
        await db.SaveChangesAsync();
        var (comp, club, style) = await SeedMeetAsync(db, new[] { owner });
        // Текст состава — мусор (год приклеен), ни одна нога не сматчится по ростеру.
        var relay = new Relay { TeamName = "Team", SwimmersName = "2014 גיא, 2013 נועם" };
        db.Relays.Add(relay);
        await db.SaveChangesAsync();
        db.Results.Add(new ResultRecord
        {
            SwimmerId = owner.Id, CompetitionId = comp.Id, ClubId = club.Id, StyleId = style.Id,
            Distance = "4X50", RelayId = relay.Id, Gender = "female",
            CompetitionDate = DateTime.SpecifyKind(new DateTime(2025, 10, 1), DateTimeKind.Unspecified),
            Position = 1, TimeMillisecond = 100000, TimeOriginal = "01:40.00",
        });
        await db.SaveChangesAsync();

        var report = await new RelayMemberBackfillService(db).BackfillAsync(apply: true);

        // Имена не сматчились, но владелец — гарантированный якорь.
        Assert.Equal(2, report.LegsUnmatched);
        var members = await db.RelayMembers.Where(m => m.RelayId == relay.Id).ToListAsync();
        var single = Assert.Single(members);
        Assert.Equal(owner.Id, single.SwimmerId);
    }

    [Fact]
    public async Task Backfill_SkipsRelaysThatAlreadyHaveMembers()
    {
        await using var db = CreateDb(nameof(Backfill_SkipsRelaysThatAlreadyHaveMembers));
        var mia = NewSwimmer("חיינובסקי", "מיה");
        db.Swimmers.Add(mia);
        await db.SaveChangesAsync();
        var (comp, club, style) = await SeedMeetAsync(db, new[] { mia });
        var relay = new Relay { TeamName = "Dolphin", SwimmersName = "מיה חיינובסקי" };
        db.Relays.Add(relay);
        await db.SaveChangesAsync();
        db.Results.Add(new ResultRecord
        {
            SwimmerId = mia.Id, CompetitionId = comp.Id, ClubId = club.Id, StyleId = style.Id,
            Distance = "4X50", RelayId = relay.Id, Gender = "female",
            CompetitionDate = DateTime.SpecifyKind(new DateTime(2025, 10, 1), DateTimeKind.Unspecified),
            Position = 1, TimeMillisecond = 100000, TimeOriginal = "01:40.00",
        });
        db.RelayMembers.Add(new RelayMember { RelayId = relay.Id, SwimmerId = mia.Id, LegOrder = 1 });
        await db.SaveChangesAsync();

        var report = await new RelayMemberBackfillService(db).BackfillAsync(apply: true);

        Assert.Equal(0, report.RelaysTotal); // уже с составом — вне выборки
        Assert.Equal(1, await db.RelayMembers.CountAsync(m => m.RelayId == relay.Id)); // без дублей
    }
}
