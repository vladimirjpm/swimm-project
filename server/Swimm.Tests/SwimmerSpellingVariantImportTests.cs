using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Импорт узнаёт пловца, записанного другим написанием (כתיב מלא/חסר, дефис в двойной фамилии) —
/// Р79, docs/data-integrity.md. Эталон: «יוקליס עמליה» из протоколов Эйлата — это #61745
/// «יוקלס עמליה» (SWIM TLV, 2011); каждый переимпорт заводил дубль, склеивали 06.10 и 07.10.
/// Условие — тот же год, тот же клуб и единственный кандидат.
/// </summary>
public class SwimmerSpellingVariantImportTests
{
    [Theory]
    [InlineData("יוקליס", "עמליה", "יוקלס", "עמליה")]
    [InlineData("בן-בסט", "יובל", "בן בסט", "יובל")]
    [InlineData("כוהן", "שירה", "כהן", "שירה")]
    [InlineData("קליטצ׳וק", "ליאורה", "קליטצ'וק", "ליאורה")]
    public void SpellingKey_SameForVariants(string lastA, string firstA, string lastB, string firstB) =>
        Assert.Equal(
            ImportSwimmerResolver.SwimmerSpellingKey(lastA, firstA, 2011),
            ImportSwimmerResolver.SwimmerSpellingKey(lastB, firstB, 2011));

    [Fact]
    public void SpellingKey_DiffersByYear_AndIsNullForLatin()
    {
        Assert.NotEqual(
            ImportSwimmerResolver.SwimmerSpellingKey("יוקליס", "עמליה", 2011),
            ImportSwimmerResolver.SwimmerSpellingKey("יוקלס", "עמליה", 2012));
        Assert.Null(ImportSwimmerResolver.SwimmerSpellingKey("Ukeles", "Amalia", 2011));
        Assert.Null(ImportSwimmerResolver.SwimmerSpellingKey("יוקלס", "עמליה", 0));
    }

    // ── Импорт ────────────────────────────────────────────────────────────────

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

    private static object Item(string last, string first, string club, string time = "0:24:00.00") => new
    {
        country = "ISR",
        competition = "Eilat open water",
        date = "06/03/2025",
        event_style_name = "Freestyle",
        event_style_len = "1600",
        event_style_gender = "female",
        pool_type = "25m",
        position = 1,
        heat = 1,
        lane = 1,
        last_name = last,
        first_name = first,
        birth_year = 2011,
        club,
        time
    };

    private static Stream ToStream(object payload) =>
        new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

    /// <summary>Карточка «יוקלס עמליה» 2011 с клубом SWIM TLV — как #61745 в живой базе.</summary>
    private static async Task<(SwimmDbContext Db, Swimmer Ukeles)> SeedAsync(string name)
    {
        var db = CreateDb(name);
        var club = new Club { Name = "SWIM TLV", NameEn = "" };
        db.Add(club);
        await db.SaveChangesAsync();
        var ukeles = new Swimmer { LastName = "יוקלס", FirstName = "עמליה", BirthYear = 2011, Gender = "female", ClubId = club.Id };
        db.Add(ukeles);
        await db.SaveChangesAsync();
        return (db, ukeles);
    }

    [Fact]
    public async Task Variant_SameYearAndClub_GoesToExistingCard()
    {
        var (db, ukeles) = await SeedAsync(nameof(Variant_SameYearAndClub_GoesToExistingCard));
        await using var _ = db;

        var result = await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(new[] { Item("יוקליס", "עמליה", "SWIM TLV") }));

        Assert.Empty(result.ErrorMessages);
        Assert.Single(await db.Swimmers.ToListAsync());
        Assert.Equal(ukeles.Id, (await db.Results.SingleAsync()).SwimmerId);
        Assert.Contains(result.DiagnosticLog, l => l.StartsWith("Другое написание"));
    }

    [Fact]
    public async Task TypographicGeresh_GoesToExistingCard()
    {
        // 18.11.2026: протокол Эйлата писал гереш левой типографской кавычкой «‘» (U+2018),
        // карточки — «׳» (U+05F3); импорт не узнавал пловца и заводил двойника (8 из 16
        // «уверенных дублей» на проде). Это то же имя, а не другое написание: клуб не важен.
        var (db, _) = await SeedAsync(nameof(TypographicGeresh_GoesToExistingCard));
        await using var __ = db;
        var card = new Swimmer { LastName = "ברדיצ׳בסקי", FirstName = "דניאלה", BirthYear = 2011, Gender = "female" };
        db.Add(card);
        await db.SaveChangesAsync();

        var result = await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(new[] { Item("ברדיצ‘בסקי", "דניאלה", "מכבי חיפה") }));

        Assert.Empty(result.ErrorMessages);
        Assert.Equal(2, await db.Swimmers.CountAsync());
        Assert.Equal(card.Id, (await db.Results.SingleAsync()).SwimmerId);
    }

    [Fact]
    public async Task Variant_OtherClub_IsNewCard()
    {
        // Другой клуб — уже не уверенность: возможно, другой человек. Решает человек в Health.
        var (db, ukeles) = await SeedAsync(nameof(Variant_OtherClub_IsNewCard));
        await using var _ = db;

        await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(new[] { Item("יוקליס", "עמליה", "מכבי חיפה") }));

        Assert.Equal(2, await db.Swimmers.CountAsync());
        Assert.NotEqual(ukeles.Id, (await db.Results.SingleAsync()).SwimmerId);
    }

    [Fact]
    public async Task Variant_TwoCandidates_IsNewCard()
    {
        // Двое подходят — гадать нельзя.
        var (db, ukeles) = await SeedAsync(nameof(Variant_TwoCandidates_IsNewCard));
        await using var _ = db;
        db.Add(new Swimmer { LastName = "יוקלס", FirstName = "עמלייה", BirthYear = 2011, ClubId = ukeles.ClubId });
        await db.SaveChangesAsync();

        await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(new[] { Item("יוקליס", "עמליה", "SWIM TLV") }));

        Assert.Equal(3, await db.Swimmers.CountAsync());
    }

    // ── Р121 (И-69): два разных имени с одним скелетом в ОДНОМ файле — два человека ──

    [Theory]
    [InlineData("בדארנה", "מחמוד", "בדארנה", "מוחמד", false)]   // разные «ו/י» — два точных имени
    [InlineData("יוקליס", "עמליה", "יוקלס", "עמליה", false)]
    [InlineData("בן-בסט", "יובל", "בן בסט", "יובל", true)]       // пунктуация — одно
    [InlineData("קליטצ׳וק", "ליאורה", "קליטצ'וק", "ליאורה", true)]
    [InlineData("בן־בסט", "יובל", "בןבסט", "יובל", true)]
    public void SpellingForms_ExactNameIgnoresOnlyPunctuation(
        string lastA, string firstA, string lastB, string firstB, bool sameExact)
    {
        var a = ImportSwimmerResolver.SpellingForms(lastA, firstA, 2008)!.Value;
        var b = ImportSwimmerResolver.SpellingForms(lastB, firstB, 2008)!.Value;
        Assert.Equal(a.Key, b.Key);
        Assert.Equal(sameExact, a.Exact == b.Exact);
    }

    private static object Brother(string first, int lane, string time, string club = "הפועל לב הגליל סחנין") => new
    {
        country = "ISR",
        competition = "גביע ארזי הלבנון",
        date = "06/10/2023",
        event_style_name = "Freestyle",
        event_style_len = "100",
        event_style_gender = "male",
        pool_type = "25m",
        position = lane,
        heat = 1,
        lane,
        last_name = "בדארנה",
        first_name = first,
        birth_year = 2008,
        club,
        time
    };

    /// <summary>Строки заплыва по карточкам: «имя → время».</summary>
    private static async Task<Dictionary<string, string>> TimesByFirstNameAsync(SwimmDbContext db) =>
        await db.Results.Include(r => r.Swimmer)
            .ToDictionaryAsync(r => r.Swimmer!.FirstName, r => r.TimeOriginal ?? "");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Brothers_InOneFile_AreTwoCards_AnyOrder(bool reversed)
    {
        // И-69: «מחמוד» и «מוחמד» בדארנה, оба 2008, один клуб; кандидат появляется в этом же импорте.
        await using var db = CreateDb($"{nameof(Brothers_InOneFile_AreTwoCards_AnyOrder)}-{reversed}");
        var rows = new[] { Brother("מחמוד", 8, "1:15.56"), Brother("מוחמד", 1, "1:14.65") };
        if (reversed) Array.Reverse(rows);

        var result = await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(rows));

        Assert.Empty(result.ErrorMessages);
        Assert.Equal(2, await db.Swimmers.CountAsync());
        var times = await TimesByFirstNameAsync(db);
        Assert.Equal(2, times.Count);
        Assert.Contains("1:15.56", times["מחמוד"]);
        Assert.Contains("1:14.65", times["מוחמד"]);
        Assert.DoesNotContain(result.DiagnosticLog, l => l.StartsWith("Другое написание:"));
        Assert.Contains(result.DiagnosticLog, l => l.StartsWith("Написание не применено:") && l.Contains("Р121"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Brothers_OneAlreadyInDb_SecondGetsOwnCard_AnyOrder(bool reversed)
    {
        // Карточка одного брата уже в базе с тем же клубом — прежде все три защиты Р79 были пройдены.
        await using var db = CreateDb($"{nameof(Brothers_OneAlreadyInDb_SecondGetsOwnCard_AnyOrder)}-{reversed}");
        var club = new Club { Name = "הפועל לב הגליל סחנין", NameEn = "" };
        db.Add(club);
        await db.SaveChangesAsync();
        var mahmud = new Swimmer { LastName = "בדארנה", FirstName = "מחמוד", BirthYear = 2008, Gender = "male", ClubId = club.Id };
        db.Add(mahmud);
        await db.SaveChangesAsync();
        var rows = new[] { Brother("מחמוד", 8, "1:15.56"), Brother("מוחמד", 1, "1:14.65") };
        if (reversed) Array.Reverse(rows);

        await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(rows));

        Assert.Equal(2, await db.Swimmers.CountAsync());
        var mine = await db.Results.Where(r => r.SwimmerId == mahmud.Id).ToListAsync();
        Assert.Contains("1:15.56", Assert.Single(mine).TimeOriginal);
    }

    [Fact]
    public async Task OneBrotherInFile_StillRecognisedBySpelling()
    {
        // Граница Р121: в файле ОДНО имя — правило Р79 действует как раньше (и по-прежнему может
        // ошибиться; это ловит только сверка с игроками loglig, см. docs/data-integrity.md, И-69).
        var (db, ukeles) = await SeedAsync(nameof(OneBrotherInFile_StillRecognisedBySpelling));
        await using var _ = db;

        await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(new[] { Item("יוקליס", "עמליה", "SWIM TLV") }));

        Assert.Single(await db.Swimmers.ToListAsync());
        Assert.Equal(ukeles.Id, (await db.Results.SingleAsync()).SwimmerId);
    }

    [Fact]
    public async Task PunctuationOnly_InOneFile_IsStillOneCard()
    {
        // «בן-בסט» и «בן בסט» в одном файле — одно точное имя, страховка Р121 не мешает Р79.
        await using var db = CreateDb(nameof(PunctuationOnly_InOneFile_IsStillOneCard));
        var club = new Club { Name = "SWIM TLV", NameEn = "" };
        db.Add(club);
        await db.SaveChangesAsync();
        var card = new Swimmer { LastName = "בן בסט", FirstName = "יובל", BirthYear = 2011, Gender = "female", ClubId = club.Id };
        db.Add(card);
        await db.SaveChangesAsync();

        var result = await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(new[]
        {
            Item("בן-בסט", "יובל", "SWIM TLV", "0:24:00.00"),
            Item("בן בסט", "יובל", "SWIM TLV", "0:25:00.00"),
        }));

        Assert.Single(await db.Swimmers.ToListAsync());
        Assert.DoesNotContain(result.DiagnosticLog, l => l.StartsWith("Написание не применено:"));
    }

    [Fact]
    public async Task PunctuationVariant_NextToBrotherInFile_StillGoesToOwnCard()
    {
        // В файле «מחמוד» (с дефисом в фамилии) и «מוחמד»; карточка «אבו עסב מחמוד» в базе. Страховка
        // запрещает приравнять братьев, но не мешает узнать карточку с тем же точным именем.
        await using var db = CreateDb(nameof(PunctuationVariant_NextToBrotherInFile_StillGoesToOwnCard));
        var club = new Club { Name = "SWIM TLV", NameEn = "" };
        db.Add(club);
        await db.SaveChangesAsync();
        var card = new Swimmer { LastName = "אבו עסב", FirstName = "מחמוד", BirthYear = 2011, Gender = "male", ClubId = club.Id };
        db.Add(card);
        await db.SaveChangesAsync();

        await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(new[]
        {
            Item("אבו-עסב", "מוחמד", "SWIM TLV", "0:24:00.00"),
            Item("אבו-עסב", "מחמוד", "SWIM TLV", "0:25:00.00"),
        }));

        Assert.Equal(2, await db.Swimmers.CountAsync());
        var mine = await db.Results.Where(r => r.SwimmerId == card.Id).ToListAsync();
        Assert.Contains("25:00", Assert.Single(mine).TimeOriginal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Brothers_OneOnlyAsRelayLeg_AreTwoCards(bool relayFirst)
    {
        // Один брат плывёт лично, второй есть только в составе эстафеты — имена ног тоже в проходе.
        await using var db = CreateDb($"{nameof(Brothers_OneOnlyAsRelayLeg_AreTwoCards)}-{relayFirst}");
        const string club = "הפועל לב הגליל סחנין";
        object relay = new
        {
            country = "ISR",
            competition = "גביע ארזי הלבנון",
            date = "06/10/2023",
            event_style_name = "Freestyle",
            event_style_len = "4X50",
            event_style_gender = "male",
            pool_type = "25m",
            position = 1,
            heat = 1,
            lane = 4,
            last_name = "לוי",
            first_name = "דן",
            birth_year = 2008,
            club,
            time = "2:00.00",
            is_relay = true,
            relay_team_name = club,
            relay_swimmers = new[]
            {
                new { order = 1, last_name = "לוי", first_name = "דן", birth_year = 2008 },
                new { order = 2, last_name = "בדארנה", first_name = "מוחמד", birth_year = 2008 },
            },
        };
        var rows = relayFirst
            ? new[] { relay, Brother("מחמוד", 8, "1:15.56") }
            : new[] { Brother("מחמוד", 8, "1:15.56"), relay };

        await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(rows));

        var badarna = await db.Swimmers.Where(s => s.LastName == "בדארנה").ToListAsync();
        Assert.Equal(2, badarna.Count);
        var leg = badarna.Single(s => s.FirstName == "מוחמד");
        Assert.Contains(await db.RelayMembers.ToListAsync(), m => m.SwimmerId == leg.Id);
        Assert.Equal(badarna.Single(s => s.FirstName == "מחמוד").Id,
            (await db.Results.SingleAsync(r => r.RelayId == null)).SwimmerId);
    }

    [Fact]
    public async Task RelayLeg_Variant_GoesToExistingCard()
    {
        var (db, ukeles) = await SeedAsync(nameof(RelayLeg_Variant_GoesToExistingCard));
        await using var _ = db;

        var relay = new
        {
            country = "ISR",
            competition = "Eilat open water",
            date = "07/03/2025",
            event_style_name = "Freestyle",
            event_style_len = "4X1500",
            event_style_gender = "none",
            pool_type = "25m",
            position = 1,
            heat = 1,
            lane = 4,
            last_name = "יוקליס",
            first_name = "עמליה",
            birth_year = 2011,
            club = "SWIM TLV",
            time = "1:30:00.00",
            is_relay = true,
            relay_team_name = "SWIM TLV",
            relay_swimmers = new[]
            {
                new { order = 1, last_name = "יוקליס", first_name = "עמליה", birth_year = 2011 },
                new { order = 2, last_name = "לוי", first_name = "דנה", birth_year = 2010 },
            },
        };

        await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(new[] { relay }));

        Assert.Contains(await db.RelayMembers.ToListAsync(), m => m.SwimmerId == ukeles.Id);
        Assert.False(await db.Swimmers.AnyAsync(s => s.LastName == "יוקליס"));
    }
}
