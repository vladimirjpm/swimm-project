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
/// Подробности держателя рекорда (отладочная опция ShowAgeRecordsDetails): год рождения и
/// возраст в год рекорда. В справочнике федерации года рождения НЕТ — он восстанавливается
/// совпадением имени среди наших пловцов, и тесты стерегут границы этого восстановления:
/// где мы имеем право назвать год, а где обязаны промолчать.
/// </summary>
public class RecordHolderDetailsTests
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

    private static Record Rec(string holder, string ageKey = "10", string date = "15/10/2021") => new()
    {
        RegionType = "country", RegionCode = "ISR", Category = "age", AgeKey = ageKey,
        Gender = "male", PoolType = "25m", Style = "breaststroke", Distance = "50m",
        Time = "39.06", HolderName = holder, RecordDate = date
    };

    private static Swimmer Swim(string first, string last, int birthYear, Club club) => new()
    {
        Club = club, FirstName = first, LastName = last,
        FirstNameEn = first, LastNameEn = last, BirthYear = birthYear
    };

    [Fact]
    public async Task NameMatch_ResolvesBirthYearAndAge_EvenWhenWordsAreSwapped()
    {
        using var db = CreateDb(nameof(NameMatch_ResolvesBirthYearAndAge_EvenWhenWordsAreSwapped));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        // В справочнике порядок слов бывает обратный: «טלר מרק» против нашего «מרק טלר».
        db.AddRange(club, Swim("מרק", "טלר", 2008, club));
        db.Add(Rec("טלר מרק", ageKey: "12", date: "17/08/2020"));
        await db.SaveChangesAsync();

        var rows = await new RecordRepository(db, new NoopCacheService())
            .GetRecordsAsync("ISR", "age", withHolderDetails: true);

        var row = Assert.Single(rows);
        Assert.Equal(2008, row.HolderBirthYear);
        Assert.Equal(12, row.HolderAge);          // 2020 − 2008, ось справочника — календарная
        Assert.Equal("name", row.HolderSource);
    }

    [Fact]
    public async Task RecordAge_IsCountedOnTheReferenceAxis()
    {
        using var db = CreateDb(nameof(RecordAge_IsCountedOnTheReferenceAxis));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        db.AddRange(club, Swim("לביא", "גפן משיח", 2011, club));
        db.Add(Rec("לביא גפן משיח"));                      // 15/10/2021, ступень Age 10
        await db.SaveChangesAsync();

        var rows = await new RecordRepository(db, new NoopCacheService())
            .GetRecordsAsync("ISR", "age", withHolderDetails: true);

        var row = Assert.Single(rows);
        Assert.Equal(2011, row.HolderBirthYear);
        // Октябрь 2021: по сезону ему уже 11, но справочник ведёт ступени календарно — 10.
        // Ровно эту пару чисел подпись на витрине и показывает.
        Assert.Equal(10, row.HolderAge);
        Assert.Equal("10", row.AgeKey);
    }

    [Fact]
    public async Task Namesakes_WithDifferentBirthYears_StaySilent()
    {
        using var db = CreateDb(nameof(Namesakes_WithDifferentBirthYears_StaySilent));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        // Два человека с одним именем и разными годами — угадывать не имеем права.
        db.AddRange(club, Swim("דוד", "מנשר", 2002, club), Swim("דוד", "מנשר", 2013, club));
        db.Add(Rec("דוד מנשר", ageKey: "11", date: "28/12/2013"));
        await db.SaveChangesAsync();

        var rows = await new RecordRepository(db, new NoopCacheService())
            .GetRecordsAsync("ISR", "age", withHolderDetails: true);

        var row = Assert.Single(rows);
        Assert.Null(row.HolderBirthYear);
        Assert.Null(row.HolderAge);
        Assert.Null(row.HolderSource);
    }

    [Fact]
    public async Task WithoutTheOption_NoDetailsLeak()
    {
        using var db = CreateDb(nameof(WithoutTheOption_NoDetailsLeak));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        db.AddRange(club, Swim("מרק", "טלר", 2008, club));
        db.Add(Rec("מרק טלר"));
        await db.SaveChangesAsync();

        // Опция выключена — витрина обязана получить справочник ровно таким, как раньше.
        var rows = await new RecordRepository(db, new NoopCacheService())
            .GetRecordsAsync("ISR", "age");

        var row = Assert.Single(rows);
        Assert.Null(row.HolderBirthYear);
        Assert.Null(row.HolderSource);
    }
}
