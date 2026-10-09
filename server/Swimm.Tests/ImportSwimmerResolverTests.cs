using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Поиск карточки пловца по строке протокола — <see cref="ImportSwimmerResolver"/>, вынесенный
/// из <see cref="JsonImportService"/> 08.10.2026. Раньше эти правила проверялись только целым
/// импортом JSON; здесь — каждое по отдельности, на InMemory-базе.
/// </summary>
public class ImportSwimmerResolverTests
{
    private static SwimmDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static ResultJsonItem Row(string last, string first, int year = 2012, string? lastEn = null, string? firstEn = null) =>
        new() { LastName = last, FirstName = first, BirthYear = year, LastNameEn = lastEn, FirstNameEn = firstEn };

    private static async Task<(SwimmDbContext Db, Club A, Club B)> SeedClubsAsync()
    {
        var db = CreateDb();
        var a = new Club { Name = "מכבי חיפה", NameEn = "" };
        var b = new Club { Name = "הפועל ירושלים", NameEn = "" };
        db.AddRange(a, b);
        await db.SaveChangesAsync();
        return (db, a, b);
    }

    [Fact]
    public async Task ExactKey_FindsExistingCard()
    {
        var (db, club, _) = await SeedClubsAsync();
        var card = new Swimmer { LastName = "כהן", FirstName = "דנה", BirthYear = 2012 };
        db.Add(card);
        await db.SaveChangesAsync();

        var resolver = await ImportSwimmerResolver.LoadAsync(db, []);
        var (found, _) = await resolver.ResolveAsync(Row("כהן", "דנה"), club, competitionId: 1);

        Assert.Same(card, found);
        Assert.Equal(1, await db.Swimmers.CountAsync());
    }

    [Fact]
    public async Task NewCard_IsReusedByTheSameFile()
    {
        var (db, club, _) = await SeedClubsAsync();
        var resolver = await ImportSwimmerResolver.LoadAsync(db, []);

        var (first, _) = await resolver.ResolveAsync(Row("לוי", "נועה"), club, 1);
        var (second, _) = await resolver.ResolveAsync(Row("לוי", "נועה"), club, 1);

        Assert.Same(first, second);
        Assert.Equal(1, await db.Swimmers.CountAsync());
    }

    [Fact]
    public async Task EnglishName_FindsCardCreatedInLatin_AndCanonicalizesIt()
    {
        var (db, club, _) = await SeedClubsAsync();
        var latin = new Swimmer { LastName = "FRIDLAND", FirstName = "YAEL", BirthYear = 1980 };
        db.Add(latin);
        await db.SaveChangesAsync();

        var resolver = await ImportSwimmerResolver.LoadAsync(db, []);
        var (found, _) = await resolver.ResolveAsync(Row("פרידלנד", "יעל", 1980, "FRIDLAND", "YAEL"), club, 1);

        Assert.Same(latin, found);
        Assert.Equal(("פרידלנד", "יעל"), (found.LastName, found.FirstName));
        Assert.Equal(("FRIDLAND", "YAEL"), (found.LastNameEn, found.FirstNameEn));
    }

    [Fact]
    public async Task OtherSurnameBoundary_SameSwimmer()
    {
        // И-28: «בניסים שרה | רוז» и «בניסים | שרה רוז» — один человек.
        var (db, club, _) = await SeedClubsAsync();
        var card = new Swimmer { LastName = "בניסים שרה", FirstName = "רוז", BirthYear = 2015 };
        db.Add(card);
        await db.SaveChangesAsync();

        var resolver = await ImportSwimmerResolver.LoadAsync(db, []);
        var (found, _) = await resolver.ResolveAsync(Row("בניסים", "שרה רוז", 2015), club, 1);

        Assert.Same(card, found);
    }

    [Fact]
    public async Task SwappedNameAndSurname_IsAnotherPerson()
    {
        // «שני עידו» и «עידו שני» — разные дети (Import_NameSurnameSwap_NotMerged).
        var (db, club, _) = await SeedClubsAsync();
        db.Add(new Swimmer { LastName = "שני", FirstName = "עידו", BirthYear = 2012 });
        await db.SaveChangesAsync();

        var resolver = await ImportSwimmerResolver.LoadAsync(db, []);
        var (found, _) = await resolver.ResolveAsync(Row("עידו", "שני"), club, 1);

        Assert.Equal(("עידו", "שני"), (found.LastName, found.FirstName));
        Assert.Equal(2, await db.Swimmers.CountAsync());
    }

    [Fact]
    public async Task Namesakes_TwoClubsInOneCompetition_GetTwoCards()
    {
        // И-11: в одном соревновании пловец не плывёт за два клуба — это тёзки.
        var (db, clubA, clubB) = await SeedClubsAsync();
        var log = new List<string>();
        var resolver = await ImportSwimmerResolver.LoadAsync(db, log);

        var (a1, _) = await resolver.ResolveAsync(Row("כהן", "יעל"), clubA, 7);
        var (b1, _) = await resolver.ResolveAsync(Row("כהן", "יעל"), clubB, 7);
        var (b2, _) = await resolver.ResolveAsync(Row("כהן", "יעל"), clubB, 7);
        var (a2, _) = await resolver.ResolveAsync(Row("כהן", "יעל"), clubA, 7);

        Assert.NotSame(a1, b1);
        Assert.Same(b1, b2);
        Assert.Same(a1, a2);
        Assert.Contains(log, l => l.StartsWith("Тёзки:"));
    }

    [Fact]
    public async Task FillEnglishName_OnlyWhenEmpty()
    {
        var (db, club, _) = await SeedClubsAsync();
        var named = new Swimmer { LastName = "אשל", FirstName = "יהונתן", BirthYear = 2012, LastNameEn = "ESHEL", FirstNameEn = "YEHONATAN" };
        var bare = new Swimmer { LastName = "לסרי", FirstName = "אורי", BirthYear = 2012 };
        db.AddRange(named, bare);
        await db.SaveChangesAsync();
        var resolver = await ImportSwimmerResolver.LoadAsync(db, []);

        var rowNamed = Row("אשל", "יהונתן", 2012, "ESHEL", "YONATAN");
        var (s1, key1) = await resolver.ResolveAsync(rowNamed, club, 1);
        resolver.FillEnglishName(s1, rowNamed, key1);

        var rowBare = Row("לסרי", "אורי", 2012, "LASRI", "ORI");
        var (s2, key2) = await resolver.ResolveAsync(rowBare, club, 1);
        resolver.FillEnglishName(s2, rowBare, key2);

        Assert.Equal("YEHONATAN", s1.FirstNameEn);
        Assert.Equal(("LASRI", "ORI"), (s2.LastNameEn, s2.FirstNameEn));
    }
}
