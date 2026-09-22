using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;
using Record = Swimm.Domain.Entities.Record;

namespace Swimm.Tests;

/// <summary>
/// Э5 плана records-relays-plan (решение Влада 22.09.2026): пловцу на его странице видна
/// эстафета, рекорд которой держит его команда. Держатель эстафеты — четыре имени через
/// запятую; имя пловца обязано совпасть с ОДНОЙ ЧАСТЬЮ целиком, не подстрокой.
/// </summary>
public class HeldRelayRecordTests
{
    private static SwimmReadDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NullCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static Record Relay(string holder, string time = "04:06.52") => new()
    {
        RegionType = "country", RegionCode = "ISR", Category = "age", AgeKey = "17",
        Gender = "mixed", PoolType = "50m", Style = "individual_medley", Distance = "4X100m",
        Time = time, HolderName = holder, RecordDate = "27/07/2025",
    };

    [Fact]
    public async Task RelayRecord_FoundByWholeHolderPart_NotBySubstring()
    {
        await using var db = CreateDb(nameof(RelayRecord_FoundByWholeHolderPart_NotBySubstring));
        var swimmer = new Swimmer { FirstName = "מרק", LastName = "טלר", BirthYear = 2008 };
        db.Add(swimmer);
        db.Records.AddRange(
            Relay("דניאל נג׳אר, מרק טלר, טומי ליטאי, אוה גולצוב"),
            // «מרק טלרמן» содержит «מרק טלר» подстрокой — но это другой человек.
            Relay("דניאל נג׳אר, מרק טלרמן, טומי ליטאי, אוה גולצוב", time: "04:00.00"));
        await db.SaveChangesAsync();

        var held = await new SwimmerPageRepository(db, new NullCacheService()).GetRecordsHeldAsync(swimmer.Id);

        var rec = Assert.Single(held);
        Assert.Equal(("mixed", "4X100m", "04:06.52"), (rec.Gender, rec.Distance, rec.Time));
    }
}
