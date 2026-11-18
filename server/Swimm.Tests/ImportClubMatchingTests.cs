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
/// Матчинг клуба при импорте (docs/data-integrity.md, инцидент И-9).
///
/// Клуб искался по паре Name|NameEn. Ивритский протокол NameEn не приносит, а у канонического
/// клуба он заполнен двуязычным импортом — совпадения нет, и переимпорт плодил клуб-дубль:
/// 2026-08-03 так появилось 59 клубов, забравших 5141 результат.
/// </summary>
public class ImportClubMatchingTests
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

    private static object Item(string club, string? clubEn = null) => new
    {
        country = "ISR",
        competition = "Meet",
        date = "01/06/2026",
        event_style_name = "Freestyle",
        event_style_len = "50",
        event_style_gender = "male",
        pool_type = "25m",
        position = 1,
        heat = 1,
        lane = 1,
        last_name = "Cohen",
        first_name = "Tal",
        birth_year = 2012,
        club,
        club_en = clubEn,
        time = "00:30.00"
    };

    private static Stream ToStream(object payload) =>
        new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

    [Fact]
    public async Task HebrewOnlyProtocol_ReusesClubWithFilledNameEn()
    {
        await using var db = CreateDb(nameof(HebrewOnlyProtocol_ReusesClubWithFilledNameEn));
        db.Clubs.Add(new Club { Name = "בני הרצליה", NameEn = "Bnei Herzeliya" });
        await db.SaveChangesAsync();

        await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(new[] { Item("בני הרצליה") }));

        var club = Assert.Single(await db.Clubs.ToListAsync());
        Assert.Equal("Bnei Herzeliya", club.NameEn);
        Assert.Equal(club.Id, (await db.Results.SingleAsync()).ClubId);
    }

    [Fact]
    public async Task ProtocolWithEnglishName_FillsEmptyNameEn()
    {
        // Зеркальный случай: в БД клуб без английского имени, протокол его принёс.
        await using var db = CreateDb(nameof(ProtocolWithEnglishName_FillsEmptyNameEn));
        db.Clubs.Add(new Club { Name = "בני הרצליה", NameEn = "" });
        await db.SaveChangesAsync();

        await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(new[] { Item("בני הרצליה", "Bnei Herzeliya") }));

        var club = Assert.Single(await db.Clubs.ToListAsync());
        Assert.Equal("Bnei Herzeliya", club.NameEn);
    }

    [Fact]
    public async Task MergedClubName_GoesToCanonical_NoNewClub()
    {
        // Склеенный клуб — надгробие: вешать на него новые результаты нельзя, иначе merge
        // молча откатится. До 18.11.2026 импорт по названию надгробия заводил НОВЫЙ клуб (и
        // склейка всё равно откатывалась — уже дублем); теперь название склеенного дубля ведёт
        // в его канон (план swimmer-club-change §5, Н5).
        await using var db = CreateDb(nameof(MergedClubName_GoesToCanonical_NoNewClub));
        var canon = new Club { Name = "Канон", NameEn = "Canon" };
        db.Clubs.Add(canon);
        await db.SaveChangesAsync();
        db.Clubs.Add(new Club { Name = "בני הרצליה", NameEn = "Bnei Herzeliya", MergedIntoId = canon.Id });
        await db.SaveChangesAsync();

        await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(new[] { Item("בני הרצליה") }));

        var result = await db.Results.SingleAsync();
        Assert.Equal(canon.Id, result.ClubId);
        Assert.Null(result.ClubNameId); // дубль — не переименование: прежнего названия нет
        Assert.Equal(2, await db.Clubs.CountAsync());
    }

    [Fact]
    public async Task FormerName_ProtocolGoesToRenamedClub_RemembersPrintedName()
    {
        // Н5: протокол 2024/25 напечатан под «הפועל עירוני הוד השרון», клуб с тех пор Aluma
        // (склейка-переименование: Hapoel — надгробие, его название в ClubNames). Перезалив
        // попадает в Aluma, новый Hapoel не рождается, строка помнит напечатанное название.
        await using var db = CreateDb(nameof(FormerName_ProtocolGoesToRenamedClub_RemembersPrintedName));
        var aluma = new Club { Name = "אלומה עירוני הוד השרון", NameEn = "" };
        db.Clubs.Add(aluma);
        await db.SaveChangesAsync();
        var hapoel = new Club { Name = "הפועל עירוני הוד השרון", NameEn = "", MergedIntoId = aluma.Id };
        db.Clubs.Add(hapoel);
        await db.SaveChangesAsync();
        var former = new ClubName
        {
            ClubId = aluma.Id, Name = hapoel.Name, Kind = ClubNameKinds.Former, MergedClubId = hapoel.Id,
        };
        db.ClubNames.Add(former);
        await db.SaveChangesAsync();

        await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(new[] { Item("הפועל עירוני הוד השרון") }));

        var result = await db.Results.SingleAsync();
        Assert.Equal(aluma.Id, result.ClubId);
        Assert.Equal(former.Id, result.ClubNameId);
        Assert.Equal(2, await db.Clubs.CountAsync());
    }

    [Fact]
    public async Task FormerName_ThroughChain_GoesToFinalCanonical()
    {
        // Цепочка, которую ещё не догнал --flatten-club-merges: 605 → 1372 → 1454. Название
        // 605 ведёт в живой конец, а не в промежуточное надгробие.
        await using var db = CreateDb(nameof(FormerName_ThroughChain_GoesToFinalCanonical));
        var live = new Club { Name = "אלומה עירוני הוד השרון" };
        db.Clubs.Add(live);
        await db.SaveChangesAsync();
        var middle = new Club { Name = "הפועל עירוני הוד השרון", MergedIntoId = live.Id };
        db.Clubs.Add(middle);
        await db.SaveChangesAsync();
        db.Clubs.Add(new Club { Name = "הפועל הוד השרון", MergedIntoId = middle.Id });
        await db.SaveChangesAsync();

        await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(new[] { Item("הפועל הוד השרון") }));

        Assert.Equal(live.Id, (await db.Results.SingleAsync()).ClubId);
        Assert.Equal(3, await db.Clubs.CountAsync());
    }

    [Fact]
    public async Task LiveClubWithSameName_WinsOverFormerName()
    {
        // Живой клуб с этим названием важнее прежнего названия другого клуба.
        await using var db = CreateDb(nameof(LiveClubWithSameName_WinsOverFormerName));
        var aluma = new Club { Name = "אלומה" };
        var namesake = new Club { Name = "הפועל" };
        db.Clubs.AddRange(aluma, namesake);
        await db.SaveChangesAsync();
        db.ClubNames.Add(new ClubName { ClubId = aluma.Id, Name = "הפועל", Kind = ClubNameKinds.Former });
        await db.SaveChangesAsync();

        await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(new[] { Item("הפועל") }));

        var result = await db.Results.SingleAsync();
        Assert.Equal(namesake.Id, result.ClubId);
        Assert.Null(result.ClubNameId);
    }

    [Fact]
    public async Task MergedClubWithEmptyNameEn_NotReused_EvenOnExactMatch()
    {
        // Инцидент И-13. Надгробие с ПУСТЫМ NameEn + ивритский протокол (NameEn тоже пустой)
        // = точное совпадение пары «Name|''». Фоллбек по имени склеенных отсекал, а точный
        // матч — нет, и merge молча откатывался: 61 надгробие снова набрало 9848 результатов.
        await using var db = CreateDb(nameof(MergedClubWithEmptyNameEn_NotReused_EvenOnExactMatch));
        var canon = new Club { Name = "הפועל דולפין נתניה", NameEn = "Hapoel Dolphine Netanya" };
        db.Clubs.Add(canon);
        await db.SaveChangesAsync();
        db.Clubs.Add(new Club { Name = "הפועל דולפין נתניה", NameEn = "", MergedIntoId = canon.Id });
        await db.SaveChangesAsync();

        await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(new[] { Item("הפועל דולפין נתניה") }));

        // Результат обязан уехать в канон, а не в надгробие, и третий клуб не рождается.
        var result = await db.Results.SingleAsync();
        Assert.Equal(canon.Id, result.ClubId);
        Assert.Equal(2, await db.Clubs.CountAsync());
    }

    [Fact]
    public async Task DifferentName_StillCreatesOwnClub()
    {
        await using var db = CreateDb(nameof(DifferentName_StillCreatesOwnClub));
        db.Clubs.Add(new Club { Name = "בני הרצליה", NameEn = "Bnei Herzeliya" });
        await db.SaveChangesAsync();

        await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(new[] { Item("מכבי חיפה") }));

        Assert.Equal(2, await db.Clubs.CountAsync());
    }
}
