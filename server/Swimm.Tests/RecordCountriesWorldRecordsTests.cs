using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;
using Record = Swimm.Domain.Entities.Record;

namespace Swimm.Tests;

/// <summary>
/// Список стран для выбора региона на табе WR /records (22.09.2026): в скобках — сколько
/// мировых рекордов OPEN держат пловцы страны. Юниорские и мастерские мировые не считаются.
/// </summary>
public class RecordCountriesWorldRecordsTests
{
    private static SwimmReadDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NoopCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult(default(T));
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static Record Rec(string regionType, string regionCode, string category, string? holderCountry,
        string distance = "50m") => new()
    {
        RegionType = regionType, RegionCode = regionCode, Category = category, AgeKey = "",
        Gender = "male", PoolType = "50m", Style = "freestyle", Distance = distance, Time = "21.00",
        HolderCountry = holderCountry,
    };

    [Fact]
    public async Task WorldRecords_CountOnlyOpenWorldRecordsByHolderCountry()
    {
        using var db = CreateDb(nameof(WorldRecords_CountOnlyOpenWorldRecordsByHolderCountry));
        db.Records.AddRange(
            Rec("country", "USA", "open", "USA"),
            Rec("country", "ISR", "open", "ISR"),
            Rec("world", "WORLD", "open", "USA", "50m"),
            Rec("world", "WORLD", "open", "USA", "100m"),
            Rec("world", "WORLD", "junior", "ISR"),   // юниорский — не в счёт
            Rec("world", "WORLD", "masters", "ISR")); // мастерский — не в счёт
        await db.SaveChangesAsync();

        var rows = await new RecordRepository(db, new NoopCacheService()).GetRecordCountriesAsync();

        Assert.Equal(2, rows.Single(r => r.Code == "USA").WorldRecords);
        Assert.Equal(0, rows.Single(r => r.Code == "ISR").WorldRecords);
    }
}
