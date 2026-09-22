using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;
using Record = Swimm.Domain.Entities.Record;

namespace Swimm.Tests;

/// <summary>
/// Числа на табах <c>/records</c> (<c>GET /api/records/world-counts</c>, 22.09.2026): всего
/// мировых рекордов по категориям, по обоим бассейнам и всем полам. Национальные рекорды
/// стран и Израиля в счёт не идут.
/// </summary>
public class RecordWorldCountsTests
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

    private static Record Rec(string regionType, string regionCode, string category, string pool, string gender) => new()
    {
        RegionType = regionType, RegionCode = regionCode, Category = category, AgeKey = "",
        Gender = gender, PoolType = pool, Style = "freestyle", Distance = "50m", Time = "21.00",
    };

    [Fact]
    public async Task CountsWorldRecordsPerCategory_AcrossPoolsAndGenders()
    {
        using var db = CreateDb(nameof(CountsWorldRecordsPerCategory_AcrossPoolsAndGenders));
        db.Records.AddRange(
            Rec("world", "WORLD", "open", "50m", "male"),
            Rec("world", "WORLD", "open", "25m", "female"),
            Rec("world", "WORLD", "junior", "50m", "mixed"),
            Rec("world", "WORLD", "masters", "25m", "male"),
            Rec("world", "WORLD", "masters", "50m", "female"),
            Rec("world", "WORLD", "masters", "50m", "male"),
            // Не мировые — в счёт не идут.
            Rec("country", "ISR", "open", "50m", "male"),
            Rec("country", "ISR", "masters", "50m", "male"),
            Rec("country", "USA", "open", "50m", "female"));
        await db.SaveChangesAsync();

        var counts = await new RecordRepository(db, new NoopCacheService()).GetWorldCountsAsync();

        Assert.Equal((2, 1, 3), (counts.Open, counts.Junior, counts.Masters));
    }
}
