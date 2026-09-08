using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Swimm.Infrastructure.Services.DataChecks;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Правило «alpha-3 в данных, alpha-2 только флагам» на ВХОДЕ в справочник стран.
///
/// Инцидент И-14 (docs/data-integrity.md §14): импорт заводил страну по сырому коду из файла,
/// и рядом с «ISR» появился второй Израиль «IL» — на него смотрели 791 пловец и 3466
/// результатов, и рекорды им не находились вовсе (поиск идёт по коду страны). Однократной
/// склейки не хватило: до неё уже была миграция MergeCountryIlIntoIsr, после которой код
/// вернулся тем же путём. Поэтому нормализация стоит во всех трёх find-or-create.
/// </summary>
public class CountryCodeNormalizationTests
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

    // ── Шов ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("IL", "ISR")]
    [InlineData("il", "ISR")]
    [InlineData(" Il ", "ISR")]
    [InlineData("ISR", "ISR")]
    [InlineData(" isr ", "ISR")]
    [InlineData("usa", "USA")]
    public void Normalize_MapsAliasesAndCase(string input, string expected) =>
        Assert.Equal(expected, CountryCodes.Normalize(input));

    [Fact]
    public void Normalize_EmptyStaysEmpty()
    {
        // «Страна не указана» — это не Израиль: подстановка своей страны на пустом вводе
        // приписала бы гражданство всем строкам без графы country.
        Assert.Equal(string.Empty, CountryCodes.Normalize(null));
        Assert.Equal(string.Empty, CountryCodes.Normalize("   "));
    }

    [Theory]
    [InlineData("ISR", true)]
    [InlineData("IL", false)]
    [InlineData("ISRA", false)]
    [InlineData("I1L", false)]
    public void LooksAlpha3_ChecksShapeOnly(string code, bool expected) =>
        Assert.Equal(expected, CountryCodes.LooksAlpha3(code));

    // ── Импорт ───────────────────────────────────────────────────────────────

    private static Stream ToStream(object payload) =>
        new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

    private static object Item(string country) => new
    {
        country,
        competition = "Тест",
        date = "01/06/2026",
        event_style_name = "Freestyle",
        event_style_len = "200",
        event_style_gender = "male",
        pool_type = "25m",
        position = 1,
        heat = 1,
        lane = 1,
        last_name = "Cohen",
        first_name = "Tal",
        birth_year = 2012,
        club = "Club",
        time = "00:30.00"
    };

    [Fact]
    public async Task Import_Alpha2Country_ReusesAlpha3Row_NoSecondIsrael()
    {
        await using var db = CreateDb(nameof(Import_Alpha2Country_ReusesAlpha3Row_NoSecondIsrael));
        db.Countries.Add(new Country { CountryCode = "ISR", CountryName = "Israel" });
        await db.SaveChangesAsync();
        var isrId = (await db.Countries.SingleAsync()).Id;

        var result = await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(new[] { Item("IL") }));

        Assert.Empty(result.ErrorMessages);
        Assert.Equal(1, await db.Countries.CountAsync());
        Assert.Equal(isrId, (await db.Results.SingleAsync()).CountryId);
    }

    [Fact]
    public async Task Import_Alpha2Country_WithoutAlpha3Row_CreatesCanonicalCode()
    {
        // Справочник пуст — создаём страну сразу под каноническим кодом, иначе следующий
        // импорт с «ISR» завёл бы вторую запись, и мы получили бы дубль с другой стороны.
        await using var db = CreateDb(nameof(Import_Alpha2Country_WithoutAlpha3Row_CreatesCanonicalCode));

        await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(new[] { Item("IL") }));

        Assert.Equal("ISR", (await db.Countries.SingleAsync()).CountryCode);
    }

    [Fact]
    public async Task Import_UnknownNonAlpha3Code_IsCreatedButAnnouncedInLog()
    {
        // Данные не подменяем — говорим вслух (docs/data-integrity.md §9): незнакомый
        // двухбуквенный код это либо новый синоним, либо опечатка в протоколе.
        await using var db = CreateDb(nameof(Import_UnknownNonAlpha3Code_IsCreatedButAnnouncedInLog));

        var result = await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(new[] { Item("DE") }));

        Assert.Equal("DE", (await db.Countries.SingleAsync()).CountryCode);
        Assert.Contains(result.DiagnosticLog, l => l.Contains("не alpha-3"));
    }

    // ── Сторож реестра ───────────────────────────────────────────────────────

    [Fact]
    public async Task DuplicateCheck_FindsSecondIsrael_WithReferenceCounts()
    {
        await using var db = CreateDb(nameof(DuplicateCheck_FindsSecondIsrael_WithReferenceCounts));
        var isr = new Country { CountryCode = "ISR", CountryName = "Israel" };
        var il = new Country { CountryCode = "IL", CountryName = "IL" };
        db.Countries.AddRange(isr, il);
        await db.SaveChangesAsync();
        db.Swimmers.Add(new Swimmer { LastName = "Cohen", FirstName = "Tal", CountryId = il.Id });
        await db.SaveChangesAsync();

        var outcome = await new CountryDuplicateCheck(db).RunAsync();

        var item = Assert.Single(outcome.Items);
        Assert.Equal(il.Id, item.EntityId);
        Assert.Contains($"#{isr.Id}", item.Message);
        Assert.Contains("пловцов 1", item.Details);
    }

    [Fact]
    public async Task DuplicateCheck_CleanCatalog_IsSilent()
    {
        await using var db = CreateDb(nameof(DuplicateCheck_CleanCatalog_IsSilent));
        db.Countries.AddRange(
            new Country { CountryCode = "ISR", CountryName = "Israel" },
            new Country { CountryCode = "USA", CountryName = "USA" });
        await db.SaveChangesAsync();

        var outcome = await new CountryDuplicateCheck(db).RunAsync();

        Assert.Equal(0, outcome.Total);
    }
}
