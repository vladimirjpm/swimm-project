using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services.DataChecks;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Проверка «соревнования без правила клубных очков» (решение Влада 2026-08-10):
/// зовёт чинить ТОЛЬКО чемпионаты, мастерс и Маккабиаду. Лиги и отборочные живут без
/// правила законно — на реальных данных они давали 16 находок из 19, и проверка звала
/// чинить то, что чинить не нужно.
/// </summary>
public class CompetitionClubRuleCheckTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static Competition Comp(string name, bool championship = false, bool masters = false) =>
        new() { Name = name, Date = "16/02/2026", PoolType = "25m", IsChampionship = championship, IsMasters = masters };

    private static ResultRecord Swim(Competition comp, Club club, Swimmer swimmer) => new()
    {
        Competition = comp, Club = club, Swimmer = swimmer, StyleId = 100, Distance = "100",
        Gender = "male", CompetitionDate = new DateTime(2026, 2, 16),
        TimeMillisecond = 60_000, TimeOriginal = "60.00"
    };

    [Fact]
    public async Task ChampionshipMastersAndMaccabiah_AreReported_LeaguesAreNot()
    {
        using var db = CreateDb(nameof(ChampionshipMastersAndMaccabiah_AreReported_LeaguesAreNot));
        var club = new Club { Name = "Club", NameEn = "Club" };
        var swimmer = new Swimmer { LastName = "A", FirstName = "B", BirthYear = 2012 };
        var maccabiahCat = new Category { Key = "result-maccabiah", Name = "Maccabiah", DisplayOrder = 9 };

        var champ = Comp("אליפות ישראל", championship: true);
        var masters = Comp("Masters meet", masters: true);
        var maccabiah = Comp("Maccabiah 2026");
        var league = Comp("ליגה מס 3");
        var qualifier = Comp("מוקדמות אליפות צעירים");

        db.AddRange(club, swimmer, maccabiahCat, champ, masters, maccabiah, league, qualifier);
        await db.SaveChangesAsync();

        db.Add(new CategoryCompetition { Category = maccabiahCat, Competition = maccabiah });
        foreach (var c in new[] { champ, masters, maccabiah, league, qualifier })
            db.Add(Swim(c, club, swimmer));
        await db.SaveChangesAsync();

        var outcome = await new CompetitionWithoutClubPointRuleCheck(db).RunAsync();

        Assert.Equal(3, outcome.Total);
        var ids = outcome.Items.Select(i => i.EntityId).ToList();
        Assert.Contains(champ.Id, ids);
        Assert.Contains(masters.Id, ids);
        Assert.Contains(maccabiah.Id, ids);
        Assert.DoesNotContain(league.Id, ids);
        Assert.DoesNotContain(qualifier.Id, ids);
    }

    [Fact]
    public async Task ClubPointsDisabled_StillSilencesTheFinding()
    {
        // Пометка «клубный зачёт не ведётся» (решение Р19) продолжает работать поверх нового
        // сужения — ею глушат исключения внутри самих чемпионатов.
        using var db = CreateDb(nameof(ClubPointsDisabled_StillSilencesTheFinding));
        var club = new Club { Name = "Club", NameEn = "Club" };
        var swimmer = new Swimmer { LastName = "A", FirstName = "B", BirthYear = 2012 };
        var champ = Comp("אליפות ישראל", championship: true);
        champ.ClubPointsDisabled = true;
        db.AddRange(club, swimmer, champ);
        await db.SaveChangesAsync();
        db.Add(Swim(champ, club, swimmer));
        await db.SaveChangesAsync();

        var outcome = await new CompetitionWithoutClubPointRuleCheck(db).RunAsync();

        Assert.Equal(0, outcome.Total);
    }

    [Fact]
    public async Task Finding_CarriesInlineFix_SoTheRuleCanBePickedRightThere()
    {
        using var db = CreateDb(nameof(Finding_CarriesInlineFix_SoTheRuleCanBePickedRightThere));
        var club = new Club { Name = "Club", NameEn = "Club" };
        var swimmer = new Swimmer { LastName = "A", FirstName = "B", BirthYear = 2012 };
        var champ = Comp("אליפות ישראל", championship: true);
        db.AddRange(club, swimmer, champ);
        await db.SaveChangesAsync();
        db.Add(Swim(champ, club, swimmer));
        await db.SaveChangesAsync();

        var item = Assert.Single((await new CompetitionWithoutClubPointRuleCheck(db).RunAsync()).Items);

        Assert.Equal("competition-club-rule", item.FixKind);
        Assert.Equal(champ.Id, item.FixEntityId);
    }
}
