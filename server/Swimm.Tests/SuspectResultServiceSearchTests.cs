using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Поиск строки для РУЧНОЙ пометки качества.
///
/// Зачем он есть: автоматика ловит только нарушения порогов, а ошибка протокола бывает
/// внутри них. Живой случай — 200 вольным за 1:53.09 у пловца, чей стольник 1:05.05:
/// рекорда это не бьёт, ни одно правило не срабатывает, но так не плавают. Раньше пометить
/// такую строку было нечем: модал показывает только уже помеченные.
/// </summary>
public class SuspectResultServiceSearchTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private sealed class NullCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    /// <summary>Соревнование с двумя заплывами одного пловца — как в исходном случае.</summary>
    private static async Task<SwimmDbContext> SeedAsync(string name)
    {
        var db = CreateDb(name);
        var comp = new Competition { Id = 1527, Name = "מוקדמות אליפות צעירים", Date = "01/02/2025", PoolType = "25m" };
        var club = new Club { Id = 1, Name = "Hapoel" };
        var swimmer = new Swimmer { Id = 60101, LastName = "ורדי איתן", FirstName = "אפרים", BirthYear = 2011 };
        var free = new Style { Id = 1, Name = "freestyle" };
        db.AddRange(comp, club, swimmer, free);
        db.Results.AddRange(
            new ResultRecord
            {
                Id = 6056530, CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = free.Id,
                Distance = "200", Gender = "male", TimeOriginal = "01:53.09", TimeMillisecond = 113090,
                CompetitionDate = new DateTime(2025, 2, 1)
            },
            new ResultRecord
            {
                Id = 6039545, CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = free.Id,
                Distance = "100", Gender = "male", TimeOriginal = "01:05.05", TimeMillisecond = 65050,
                CompetitionDate = new DateTime(2025, 2, 1)
            });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Search_FindsUnflaggedRow_ByTime()
    {
        await using var db = await SeedAsync(nameof(Search_FindsUnflaggedRow_ByTime));
        var service = new SuspectResultService(db, new NullCache());

        var rows = await service.SearchAsync(null, 1527, "1:53.09");

        var row = Assert.Single(rows);
        Assert.Equal(6056530, row.ResultId);
        // Пустая причина = строка ещё не разобрана; именно её и надо уметь пометить.
        Assert.Equal("", row.Reason);
    }

    [Fact]
    public async Task Search_BySwimmerName_IsCaseInsensitive()
    {
        await using var db = await SeedAsync(nameof(Search_BySwimmerName_IsCaseInsensitive));
        var service = new SuspectResultService(db, new NullCache());

        Assert.Equal(2, (await service.SearchAsync(null, 1527, "ורדי")).Count);
        Assert.Equal(2, (await service.SearchAsync(null, 1527, "HAPOEL")).Count);
    }

    [Fact]
    public async Task Search_TooShortQuery_ReturnsNothing()
    {
        // Иначе один символ вывалил бы всё соревнование — список, в котором ничего не найти.
        await using var db = await SeedAsync(nameof(Search_TooShortQuery_ReturnsNothing));
        var service = new SuspectResultService(db, new NullCache());

        Assert.Empty(await service.SearchAsync(null, 1527, "1"));
    }

    [Fact]
    public async Task ManualFlag_ThenSearch_ShowsRowAsAlreadyFlagged()
    {
        await using var db = await SeedAsync(nameof(ManualFlag_ThenSearch_ShowsRowAsAlreadyFlagged));
        var service = new SuspectResultService(db, new NullCache());

        Assert.True(await service.SetManualAsync(6056530, true, "200 за 1:53 при стольнике 1:05"));

        var flagged = Assert.Single(await service.GetFlaggedAsync(null, 1527));
        Assert.Equal(SuspectReasons.Manual, flagged.Reason);
        Assert.True(flagged.IsManual);

        // Повторный поиск должен показать, что строка уже разобрана — чтобы не пометить дважды.
        var found = Assert.Single(await service.SearchAsync(null, 1527, "1:53.09"));
        Assert.Equal(SuspectReasons.Manual, found.Reason);
    }
}
