using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Правая сторона карточки «рекорд пловца против мирового» (план records-world-compare, Р1):
/// к каждому официальному рекорду пловца подбирается мировой рекорд мастерс ТОЙ ЖЕ ступени.
///
/// Матч точный — <c>AgeKey + Gender + PoolType + Style + Distance</c>: ступени мировые и
/// израильские совпадают один в один (25-29 … 90-94), эвристики тут быть не должно. Держим
/// три развилки, которые легко потерять правкой: совпало; мирового нет; мировой есть, но
/// для ДРУГОГО бассейна — 25м и 50м несравнимы, и подставлять один вместо другого нельзя.
/// </summary>
public class HeldRecordWorldMatchTests
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

    private static ISwimmerPageRepository Repo(SwimmReadDbContext db) =>
        new SwimmerPageRepository(db, new NullCacheService());

    private static Swimmer Holder() => new()
    {
        Id = 1, FirstName = "איגור", LastName = "קרבקוב",
        FirstNameEn = "Igor", LastNameEn = "Kravkov",
    };

    private static Swimm.Domain.Entities.Record Rec(
        string regionType, string category, string ageKey, string poolType,
        string style, string distance, string time, string? holder, string? country = null) => new()
        {
            RegionType = regionType,
            RegionCode = regionType == "country" ? "ISR" : string.Empty,
            Category = category,
            AgeKey = ageKey,
            Gender = "male",
            PoolType = poolType,
            Style = style,
            Distance = distance,
            Time = time,
            HolderName = holder,
            HolderCountry = country,
            RecordDate = "01/01/2025",
        };

    /// <summary>Рекорд пловца — израильский мастерский; мировой той же ступени существует.</summary>
    [Fact]
    public async Task Held_record_gets_world_record_of_the_same_step()
    {
        await using var db = CreateDb(nameof(Held_record_gets_world_record_of_the_same_step));
        db.Swimmers.Add(Holder());
        db.Records.AddRange(
            Rec("country", "masters", "55-59", "50m", "backstroke", "50m", "31.11", "איגור קרבקוב"),
            Rec("world", "masters", "55-59", "50m", "backstroke", "50m", "28.54", "ATASAY Serkan", "TUR"));
        await db.SaveChangesAsync();

        var rows = await Repo(db).GetRecordsHeldAsync(1);

        var row = Assert.Single(rows);
        Assert.NotNull(row.WorldRecord);
        Assert.Equal("28.54", row.WorldRecord!.Time);
        Assert.Equal("ATASAY Serkan", row.WorldRecord.Holder);
        Assert.Equal("TUR", row.WorldRecord.HolderCountry);
    }

    /// <summary>
    /// Немастерский рекорд (возрастная ось): мировых рекордов по юношеским возрастам у нас
    /// нет — правая сторона карточки остаётся пустой, и это норма, а не потеря данных.
    /// </summary>
    [Fact]
    public async Task Record_without_world_counterpart_stays_null()
    {
        await using var db = CreateDb(nameof(Record_without_world_counterpart_stays_null));
        db.Swimmers.Add(Holder());
        db.Records.Add(
            Rec("country", "age", "16", "50m", "freestyle", "100m", "50.95", "איגור קרבקוב"));
        await db.SaveChangesAsync();

        var rows = await Repo(db).GetRecordsHeldAsync(1);

        Assert.Null(Assert.Single(rows).WorldRecord);
    }

    /// <summary>
    /// Мировой есть, но для ДРУГОГО бассейна. Время короткой воды быстрее по устройству
    /// бассейна, и подставить его к 50-метровому рекорду значило бы напечатать выдуманный
    /// разрыв. Ступень, пол, стиль и дистанция совпадают — совпасть не должен только бассейн.
    /// </summary>
    [Fact]
    public async Task World_record_of_another_pool_does_not_match()
    {
        await using var db = CreateDb(nameof(World_record_of_another_pool_does_not_match));
        db.Swimmers.Add(Holder());
        db.Records.AddRange(
            Rec("country", "masters", "60-64", "50m", "freestyle", "50m", "27.28", "איגור קרבקוב"),
            Rec("world", "masters", "60-64", "25m", "freestyle", "50m", "24.01", "HOCHSTEIN Erik", "USA"));
        await db.SaveChangesAsync();

        var rows = await Repo(db).GetRecordsHeldAsync(1);

        Assert.Null(Assert.Single(rows).WorldRecord);
    }

    /// <summary>
    /// Справочник печатает дистанцию «50m», протокол — «50». Нормализация обеих сторон ключа
    /// живёт в одном месте (<c>WorldRecordKey</c>), и разнобой записи в справочнике матч не рвёт.
    /// </summary>
    [Fact]
    public async Task Distance_suffix_does_not_break_the_match()
    {
        await using var db = CreateDb(nameof(Distance_suffix_does_not_break_the_match));
        db.Swimmers.Add(Holder());
        db.Records.AddRange(
            Rec("country", "masters", "65-69", "25m", "butterfly", "50", "27.41", "איגור קרבקוב"),
            Rec("world", "masters", "65-69", "25m", "butterfly", "50m", "24.45", "BARNES Brent", "JPN"));
        await db.SaveChangesAsync();

        var rows = await Repo(db).GetRecordsHeldAsync(1);

        Assert.Equal("24.45", Assert.Single(rows).WorldRecord?.Time);
    }
}
