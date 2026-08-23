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
/// Заплывы без пола в шапке протокола («שומרי שבת» — соблюдающие субботу плывут вместе,
/// мальчики и девочки). Парсер отдаёт event_style_gender = "none": выдумывать пол нельзя.
/// Импорт берёт пол с самого пловца — он известен по остальным его заплывам; если и там
/// пусто, результат остаётся без пола и виден в «Аномалиях» на /Admin/Results.
/// </summary>
public class ImportMixedGenderTests
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

    private static object Item(
        string lastName, string gender, string time = "00:30.00",
        string competition = "Шабат", string date = "01/06/2026") => new
    {
        country = "ISR",
        competition,
        date,
        event_style_name = "Freestyle",
        event_style_len = "200",
        event_style_gender = gender,
        pool_type = "25m",
        position = 1,
        heat = 1,
        lane = 1,
        last_name = lastName,
        first_name = "Tal",
        birth_year = 2012,
        club = "Club",
        time
    };

    private static Stream ToStream(object payload) =>
        new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

    [Fact]
    public async Task NoneGender_TakenFromKnownSwimmer()
    {
        await using var db = CreateDb(nameof(NoneGender_TakenFromKnownSwimmer));
        db.Swimmers.Add(new Swimmer { LastName = "Cohen", FirstName = "Tal", BirthYear = 2012, Gender = "female" });
        await db.SaveChangesAsync();

        var result = await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(new[] { Item("Cohen", "none") }));

        Assert.Empty(result.ErrorMessages);
        Assert.Equal("female", (await db.Results.SingleAsync()).Gender);
    }

    [Fact]
    public async Task NoneGender_UnknownSwimmer_StaysEmpty_AndDoesNotPoisonSwimmer()
    {
        // Пловец встречается только в смешанном заплыве: пол неизвестен. Записать "none"
        // в карточку пловца было бы хуже пустого — это не пол, а отсутствие данных.
        await using var db = CreateDb(nameof(NoneGender_UnknownSwimmer_StaysEmpty_AndDoesNotPoisonSwimmer));

        var result = await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(new[] { Item("Levi", "none") }));

        Assert.Empty(result.ErrorMessages);
        Assert.Equal(string.Empty, (await db.Results.SingleAsync()).Gender);
        Assert.True(string.IsNullOrEmpty((await db.Swimmers.SingleAsync()).Gender));
    }

    [Fact]
    public async Task Relay_KeepsNone_EvenWhenSwimmerGenderKnown()
    {
        // У команды нет одного пола, а «владелец» строки — первая нога. Плюс Gender входит
        // в ключ upsert: подмена сделала бы строки эстафет неузнаваемыми при переимпорте.
        await using var db = CreateDb(nameof(Relay_KeepsNone_EvenWhenSwimmerGenderKnown));
        db.Swimmers.Add(new Swimmer { LastName = "Cohen", FirstName = "Tal", BirthYear = 2012, Gender = "female" });
        await db.SaveChangesAsync();

        var relayItem = new
        {
            country = "ISR",
            competition = "Шабат",
            date = "01/06/2026",
            event_style_name = "Medley",
            event_style_len = "4X50",
            event_style_gender = "none",
            pool_type = "25m",
            position = 1,
            heat = 1,
            lane = 1,
            last_name = "Cohen",
            first_name = "Tal",
            birth_year = 2012,
            club = "Club",
            time = "02:30.00",
            is_relay = true,
            relay_team_name = "Club"
        };

        await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(new[] { relayItem }));

        Assert.Equal("none", (await db.Results.SingleAsync()).Gender);
    }

    [Fact]
    public async Task GenderlessRow_FilledLater_WhenSwimmerGenderBecameKnown()
    {
        // Пловец впервые встретился в смешанном заплыве — пола нет ни в шапке, ни в карточке,
        // строка легально остаётся пустой. На СЛЕДУЮЩЕМ старте пол известен, и старая строка
        // обязана дописаться сама: иначе находка `results.no-gender` висит, хотя ответ уже
        // лежит в базе (4 такие строки на живой базе 2026-08-23).
        await using var db = CreateDb(nameof(GenderlessRow_FilledLater_WhenSwimmerGenderBecameKnown));
        var service = new JsonImportService(db, new NullCache());

        await service.ImportAsync(ToStream(new[] { Item("Levi", "none", "02:40.00") }));
        Assert.Equal(string.Empty, (await db.Results.SingleAsync()).Gender);

        await service.ImportAsync(ToStream(new[]
        {
            Item("Levi", "female", "02:38.00", competition: "Чемпионат", date: "15/06/2026"),
        }));

        Assert.Equal("female", (await db.Swimmers.SingleAsync()).Gender);
        Assert.All(await db.Results.ToListAsync(), r => Assert.Equal("female", r.Gender));
    }

    [Fact]
    public async Task ImportFixesCardByMajority_ButNeverOverwritesPrintedGender()
    {
        // Карточка приводится к большинству заплывов (одна ошибка в первом протоколе больше
        // не делает её неверной навсегда), а вот НАПЕЧАТАННЫЙ в протоколе пол строки импорт
        // не трогает: перезапись — осознанное решение человека, кнопка «выровнять» в реестре.
        await using var db = CreateDb(nameof(ImportFixesCardByMajority_ButNeverOverwritesPrintedGender));
        db.Swimmers.Add(new Swimmer { LastName = "Cohen", FirstName = "Tal", BirthYear = 2012, Gender = "male" });
        await db.SaveChangesAsync();

        await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(new[]
        {
            Item("Cohen", "female", "02:31.00"),
            Item("Cohen", "female", "02:32.00"),
            Item("Cohen", "female", "02:33.00"),
        }));

        Assert.Equal("female", (await db.Swimmers.SingleAsync()).Gender);
        Assert.All(await db.Results.ToListAsync(), r => Assert.Equal("female", r.Gender));
    }

    [Fact]
    public async Task OrdinaryGender_UsedAsIs()
    {
        await using var db = CreateDb(nameof(OrdinaryGender_UsedAsIs));

        await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(new[] { Item("Dan", "male") }));

        Assert.Equal("male", (await db.Results.SingleAsync()).Gender);
        Assert.Equal("male", (await db.Swimmers.SingleAsync()).Gender);
    }
}
