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
    public async Task StartListBoundary_SameSwimmer()
    {
        // И-56: стартовый протокол завёл «עליא אבו | עסב» (последнее слово — фамилия), протокол
        // результатов режет верно — «עליא | אבו עסב». Живой случай 16858, 30.10.2026.
        var (db, club, _) = await SeedClubsAsync();
        var card = new Swimmer { LastName = "עסב", FirstName = "עליא אבו", BirthYear = 2018 };
        db.Add(card);
        await db.SaveChangesAsync();

        var log = new List<string>();
        var resolver = await ImportSwimmerResolver.LoadAsync(db, log);
        var (found, _) = await resolver.ResolveAsync(Row("אבו עסב", "עליא", 2018), club, 1);

        Assert.Same(card, found);
        Assert.Equal(1, await db.Swimmers.CountAsync());
        Assert.Contains(log, l => l.StartsWith("Другая граница имени:"));
    }

    [Fact]
    public async Task StartListBoundary_TwoCandidates_NewCard()
    {
        // Двое под одним ключом «имя+фамилия» — гадать нельзя, лучше тень, чем чужие результаты.
        var (db, club, _) = await SeedClubsAsync();
        db.AddRange(
            new Swimmer { LastName = "ד", FirstName = "א ב ג", BirthYear = 2015 },
            new Swimmer { LastName = "ב ג ד", FirstName = "א", BirthYear = 2015 });
        await db.SaveChangesAsync();

        var resolver = await ImportSwimmerResolver.LoadAsync(db, []);
        var (found, _) = await resolver.ResolveAsync(Row("ג ד", "א ב", 2015), club, 1);

        Assert.Equal(("ג ד", "א ב"), (found.LastName, found.FirstName));
        Assert.Equal(3, await db.Swimmers.CountAsync());
    }

    [Fact]
    public async Task StartListBoundary_GroupSwimmer_NotMatched()
    {
        // Пловец группы (Р71) — его заводил тренер, чужой протокол в его карточку не идёт.
        var (db, club, _) = await SeedClubsAsync();
        var group = new HubGroup { Name = "Test", Slug = "test" };
        db.Add(group);
        await db.SaveChangesAsync();
        db.Add(new Swimmer { LastName = "עסב", FirstName = "עליא אבו", BirthYear = 2018, PrivateHubGroupId = group.Id });
        await db.SaveChangesAsync();

        var resolver = await ImportSwimmerResolver.LoadAsync(db, []);
        var (found, _) = await resolver.ResolveAsync(Row("אבו עסב", "עליא", 2018), club, 1);

        Assert.Null(found.PrivateHubGroupId);
        Assert.Equal(2, await db.Swimmers.CountAsync());
    }

    [Fact]
    public async Task StartListBoundary_RelayLeg_SameSwimmer()
    {
        // Нога эстафеты из PDF — «אריאלי מזוז | אופיר», стартовый протокол завёл «אופיר אריאלי | מזוז».
        var (db, club, _) = await SeedClubsAsync();
        var card = new Swimmer { LastName = "מזוז", FirstName = "אופיר אריאלי", BirthYear = 2019 };
        db.Add(card);
        await db.SaveChangesAsync();

        var resolver = await ImportSwimmerResolver.LoadAsync(db, []);
        var members = await resolver.ResolveRelayMembersAsync(
            [new RelaySwimmerJson { Order = 1, LastName = "אריאלי מזוז", FirstName = "אופיר", BirthYear = 2019 }], club);

        Assert.Equal(card.Id, Assert.Single(members).SwimmerId);
        Assert.Equal(1, await db.Swimmers.CountAsync());
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

    // ── Сторож EN-имени (10.10.2026, comp 1486): чужое или ивритское в EN-поля не пишем ──

    [Fact]
    public async Task NewCard_HebrewInEnglishFields_LeftEmpty()
    {
        // EN-строка потеряла фамилию — пара HE+EN подставила ивритскую.
        var (db, club, _) = await SeedClubsAsync();
        var log = new List<string>();
        var resolver = await ImportSwimmerResolver.LoadAsync(db, log);

        var (s, _) = await resolver.ResolveAsync(Row("ברוטמן", "מיכאל", 2016, "ברוטמן", "BRUTMAN"), club, 1);

        Assert.Equal(("", ""), (s.LastNameEn, s.FirstNameEn));
        Assert.Contains(log, l => l.Contains("иврит в латинском поле"));
    }

    [Fact]
    public async Task FillEnglishName_NeighboursName_NotWritten()
    {
        // EN-строка съехала на соседа по блоку неявок: у «פוגל דודוביץ איתן» — «ELKIN DANIEL».
        var (db, club, _) = await SeedClubsAsync();
        var card = new Swimmer { LastName = "פוגל דודוביץ", FirstName = "איתן", BirthYear = 2016 };
        db.Add(card);
        await db.SaveChangesAsync();
        var log = new List<string>();
        var resolver = await ImportSwimmerResolver.LoadAsync(db, log);

        var row = Row("פוגל דודוביץ", "איתן", 2016, "ELKIN", "DANIEL");
        var (s, key) = await resolver.ResolveAsync(row, club, 1);
        resolver.FillEnglishName(s, row, key);

        Assert.Same(card, s);
        Assert.Equal(("", ""), (s.LastNameEn, s.FirstNameEn));
        Assert.Contains(log, l => l.Contains("«ELKIN» не похоже"));
    }

    [Fact]
    public async Task NewCard_ItsOwnEnglishName_Written()
    {
        var (db, club, _) = await SeedClubsAsync();
        var resolver = await ImportSwimmerResolver.LoadAsync(db, []);

        var (s, _) = await resolver.ResolveAsync(Row("אבן חן נויבואר", "איתן", 2016, "EVEN CHEN NEUBAUER", "EYTAN"), club, 1);

        Assert.Equal(("EVEN CHEN NEUBAUER", "EYTAN"), (s.LastNameEn, s.FirstNameEn));
    }
}
