using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Страна группы (<see cref="HubGroupCrudCore.ApplyCountryAsync"/>): alpha-3 код из ввода →
/// FK на Countries с find-or-create (как в импорте). Семантика ввода: null — поле не
/// прислано, страну не трогаем (старый клиент не сотрёт выставленное админом);
/// "" — явное «снять страну».
/// </summary>
public class HubGroupCountryTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private sealed class NoopCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult(default(T));
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static HubGroupCrudCore Core(SwimmDbContext db) => new(db, new NoopCacheService());

    [Fact]
    public async Task ExistingCountry_ResolvedToItsId_NoDuplicateCreated()
    {
        await using var db = CreateDb(nameof(ExistingCountry_ResolvedToItsId_NoDuplicateCreated));
        db.Countries.Add(new Country { CountryCode = "ISR", CountryName = "ISR" });
        await db.SaveChangesAsync();
        var existingId = (await db.Countries.SingleAsync()).Id;

        var group = new HubGroup();
        await Core(db).ApplyCountryAsync(group, "ISR");

        Assert.Equal(existingId, group.CountryId);
        Assert.Equal(1, await db.Countries.CountAsync());
    }

    [Fact]
    public async Task UnknownCountry_CreatedOnTheFly_LikeImport()
    {
        await using var db = CreateDb(nameof(UnknownCountry_CreatedOnTheFly_LikeImport));

        var group = new HubGroup();
        await Core(db).ApplyCountryAsync(group, "HUN");

        var created = await db.Countries.SingleAsync();
        Assert.Equal("HUN", created.CountryCode);
        Assert.Equal(created.Id, group.CountryId);
    }

    [Fact]
    public async Task Code_IsNormalized_TrimmedAndUppercased()
    {
        await using var db = CreateDb(nameof(Code_IsNormalized_TrimmedAndUppercased));
        db.Countries.Add(new Country { CountryCode = "ISR", CountryName = "ISR" });
        await db.SaveChangesAsync();

        var group = new HubGroup();
        await Core(db).ApplyCountryAsync(group, " isr ");

        Assert.NotNull(group.CountryId);
        Assert.Equal(1, await db.Countries.CountAsync()); // дубль "isr" не создан
    }

    [Fact]
    public async Task NullInput_FieldNotSent_CountryUntouched()
    {
        await using var db = CreateDb(nameof(NullInput_FieldNotSent_CountryUntouched));

        var group = new HubGroup { CountryId = 42 };
        await Core(db).ApplyCountryAsync(group, null);

        Assert.Equal(42, group.CountryId); // старый клиент без поля не стирает страну
    }

    [Fact]
    public async Task EmptyString_ExplicitClear_CountryRemoved()
    {
        await using var db = CreateDb(nameof(EmptyString_ExplicitClear_CountryRemoved));

        var group = new HubGroup { CountryId = 42 };
        await Core(db).ApplyCountryAsync(group, "");

        Assert.Null(group.CountryId);
    }
}
