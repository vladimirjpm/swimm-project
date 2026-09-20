using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Пловцы-тени из-за РАЗНОЙ ГРАНИЦЫ между именем и фамилией (И-28, 20.09.2026).
///
/// Основной протокол даёт ногу эстафеты плоской строкой «Фамилия… Имя Год» и вынужден гадать,
/// где кончается фамилия: <c>ParseRelaySwimmerLine</c> отдаёт имени ровно одно слово, поэтому
/// «בניסים שרה רוז 2015» становится фамилией «בניסים שרה» и именем «רוז». У PDF промежуточных
/// loglig имя и фамилия лежат в РАЗНЫХ КОЛОНКАХ, и тот же человек разрезан правильно.
/// Ключ «фамилия|имя|год» на этих двух написаниях разный — и за один прогон 19.09.2026
/// импорт завёл 50 теней рядом с настоящими пловцами.
///
/// Лечение — фоллбек по СКЛЕЙКЕ «фамилия+имя» без пробелов (<c>SwimmerFullNameKey</c>), только
/// когда точного совпадения нет. Порядок значим: перестановка имени и фамилии по-прежнему даёт
/// разных людей (Import_NameSurnameSwap_NotMerged).
/// </summary>
public class ImportNameBoundaryTests
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

    private static object Swim(string last, string first, string time, string competition = "Meet") => new
    {
        country = "ISR",
        competition,
        date = "19/02/2026",
        event_style_name = "freestyle",
        event_style_len = "50",
        event_style_gender = "female",
        pool_type = "25m",
        position = 1,
        heat = 1,
        lane = 4,
        last_name = last,
        first_name = first,
        birth_year = 2015,
        club = "מכבי",
        time,
    };

    /// <summary>Эстафета, у которой нога записана со СВОИМ разрезом имени.</summary>
    private static object Relay(string legLast, string legFirst, string competition = "Meet") => new
    {
        country = "ISR",
        competition,
        date = "20/02/2026",
        event_style_name = "freestyle",
        event_style_len = "4X50",
        event_style_gender = "female",
        pool_type = "25m",
        position = 1,
        heat = 1,
        lane = 5,
        last_name = legLast,
        first_name = legFirst,
        birth_year = 2015,
        club = "מכבי",
        time = "02:10.00",
        is_relay = true,
        relay_team_name = "מכבי",
        relay_swimmers = new[]
        {
            new { order = 1, last_name = legLast, first_name = legFirst, birth_year = 2015, club = (string?)null, split_time = (string?)null },
        },
    };

    private static Stream ToStream(object payload) =>
        new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

    [Fact]
    public async Task LegWithOtherNameBoundary_ReusesExistingSwimmer()
    {
        await using var db = CreateDb(nameof(LegWithOtherNameBoundary_ReusesExistingSwimmer));
        var import = new JsonImportService(db, new NullCache());

        // Личный заплыв: протокол отдал второе имя фамилии.
        await import.ImportAsync(ToStream(new[] { Swim("בניסים שרה", "רוז", "00:35.00") }), "meet.json");
        // Эстафета: тот же человек, но имя и фамилия разрезаны как в колонках loglig.
        await import.ImportAsync(ToStream(new[] { Relay("בניסים", "שרה רוז") }), "relay.json");

        Assert.Equal(1, await db.Swimmers.CountAsync());
        var member = await db.RelayMembers.SingleAsync();
        Assert.Equal((await db.Swimmers.SingleAsync()).Id, member.SwimmerId);
    }

    [Fact]
    public async Task SwimWithOtherNameBoundary_ReusesExistingSwimmer()
    {
        await using var db = CreateDb(nameof(SwimWithOtherNameBoundary_ReusesExistingSwimmer));
        var import = new JsonImportService(db, new NullCache());

        await import.ImportAsync(ToStream(new[] { Swim("בניסים", "שרה רוז", "00:35.00") }), "a.json");
        await import.ImportAsync(ToStream(new[] { Swim("בניסים שרה", "רוז", "00:34.00", "Meet 2") }), "b.json");

        Assert.Equal(1, await db.Swimmers.CountAsync());
        Assert.Equal(2, await db.Results.CountAsync());
    }

    [Fact]
    public async Task SwappedOrder_StaysDifferentPeople()
    {
        // Порядок значим: «שני עידו» и «עידו שני» — разные люди (решение зафиксировано в
        // Import_NameSurnameSwap_NotMerged: в иврите обе половины бывают и именем, и фамилией).
        // Фоллбек по границе не должен это ломать.
        await using var db = CreateDb(nameof(SwappedOrder_StaysDifferentPeople));
        var import = new JsonImportService(db, new NullCache());

        await import.ImportAsync(ToStream(new[] { Swim("שני", "עידו", "00:35.00") }), "a.json");
        await import.ImportAsync(ToStream(new[] { Swim("עידו", "שני", "00:34.00", "Meet 2") }), "b.json");

        Assert.Equal(2, await db.Swimmers.CountAsync());
    }

    [Fact]
    public async Task DifferentBirthYear_StaysDifferentPerson()
    {
        // Год рождения в ключе остаётся: одинаковое имя разных лет — разные люди.
        await using var db = CreateDb(nameof(DifferentBirthYear_StaysDifferentPerson));
        var import = new JsonImportService(db, new NullCache());

        await import.ImportAsync(ToStream(new[] { Swim("בניסים", "שרה רוז", "00:35.00") }), "a.json");
        await import.ImportAsync(ToStream(new object[]
        {
            new
            {
                country = "ISR", competition = "Meet 2", date = "19/02/2026",
                event_style_name = "freestyle", event_style_len = "50", event_style_gender = "female",
                pool_type = "25m", position = 1, heat = 1, lane = 4,
                last_name = "בניסים שרה", first_name = "רוז", birth_year = 2013,
                club = "מכבי", time = "00:34.00",
            },
        }), "b.json");

        Assert.Equal(2, await db.Swimmers.CountAsync());
    }

    [Fact]
    public async Task ExactMatchStillWins_NamesakeGuardUntouched()
    {
        // Точное совпадение — прежний путь; фоллбек включается только при промахе.
        await using var db = CreateDb(nameof(ExactMatchStillWins_NamesakeGuardUntouched));
        var import = new JsonImportService(db, new NullCache());

        await import.ImportAsync(ToStream(new[] { Swim("בניסים", "שרה רוז", "00:35.00") }), "a.json");
        await import.ImportAsync(ToStream(new[] { Swim("בניסים", "שרה רוז", "00:34.00", "Meet 2") }), "b.json");

        Assert.Equal(1, await db.Swimmers.CountAsync());
        Assert.Equal(2, await db.Results.CountAsync());
    }
}
