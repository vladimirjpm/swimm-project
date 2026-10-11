using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services.DataChecks;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// `swimmers.two-clubs-in-competition`: два настоящих клуба в одном старте — находка (тёзки,
/// склеенные импортом, И-11). Псевдоклуб «No club» (клуба в строке протокола нет) вторым
/// клубом НЕ считается: после склейки дублей 06.10.2026 он поднял две ложные ошибки
/// (מירוצ׳ניק דוד и זורבלוב משה — «No club» на 100, «הפועל ירושלים» на 200 одного старта).
/// </summary>
public class SwimmerTwoClubsInCompetitionCheckTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static async Task<SwimmDbContext> SeedAsync(string name, string secondClub)
    {
        var db = CreateDb(name);
        var swimmer = new Swimmer { LastName = "מירוצ׳ניק", FirstName = "דוד", BirthYear = 2008, Gender = "male" };
        var club = new Club { Name = "הפועל ירושלים" };
        var other = new Club { Name = secondClub };
        var style = new Style { Name = "freestyle" };
        var comp = new Competition { Name = "ליגה 5 הפועל ירושלים", Date = "21/03/2025", PoolType = "25m" };
        db.AddRange(swimmer, club, other, style, comp);
        await db.SaveChangesAsync();

        db.Results.AddRange(
            new ResultRecord
            {
                CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = other.Id, StyleId = style.Id,
                Distance = "100", Gender = "male", TimeOriginal = "00:59.45", CompetitionDate = new DateTime(2025, 3, 21)
            },
            new ResultRecord
            {
                CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
                Distance = "200", Gender = "male", TimeOriginal = "02:09.39", CompetitionDate = new DateTime(2025, 3, 21)
            });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task NoClubRow_IsNotASecondClub()
    {
        await using var db = await SeedAsync(nameof(NoClubRow_IsNotASecondClub), SwimmerTwoClubsInCompetitionCheck.NoClubName);

        var outcome = await new SwimmerTwoClubsInCompetitionCheck(db).RunAsync();

        Assert.Equal(0, outcome.Total);
    }

    [Fact]
    public async Task TwoRealClubs_AreAFinding()
    {
        await using var db = await SeedAsync(nameof(TwoRealClubs_AreAFinding), "מכבי חיפה");

        var outcome = await new SwimmerTwoClubsInCompetitionCheck(db).RunAsync();

        Assert.Equal(1, outcome.Total);
    }
}
