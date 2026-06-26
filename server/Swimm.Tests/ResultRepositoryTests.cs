using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

public class ResultRepositoryTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static DbContextOptions<SwimmReadDbContext> BuildOptions(string name) =>
        new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static SwimmReadDbContext CreateDb(string name) =>
        new SwimmReadDbContext(BuildOptions(name));

    /// <summary>ICacheService, который всегда возвращает miss — репозиторий идёт в БД.</summary>
    private sealed class NullCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static ICacheService NoCache() => new NullCacheService();

    /// <summary>
    /// Сеет полный граф: Style + Club + Competition + Swimmer + ResultRecord.
    /// Каждый вызов создаёт независимые сущности с новыми PK.
    /// </summary>
    private static async Task<ResultRecord> SeedResultAsync(
        SwimmReadDbContext db,
        string styleName = "freestyle",
        string distance  = "100",
        string gender    = "male",
        string poolType  = "50m",
        DateTime? date   = null)
    {
        var style   = new Style { Name = styleName };
        var club    = new Club { Name = "TestClub", NameEn = "TestClub" };
        var comp    = new Competition
        {
            Name = "TestComp", Country = "ISR",
            Date = "01/01/2024", PoolType = poolType
        };
        var swimmer = new Swimmer
        {
            LastName = "Иванов", FirstName = "Иван",
            LastNameEn = "Ivanov", FirstNameEn = "Ivan",
            BirthYear = 2000
        };
        db.Styles.Add(style);
        db.Clubs.Add(club);
        db.Competitions.Add(comp);
        db.Swimmers.Add(swimmer);
        await db.SaveChangesAsync();

        var result = new ResultRecord
        {
            CompetitionId   = comp.Id,
            SwimmerId       = swimmer.Id,
            ClubId          = club.Id,
            StyleId         = style.Id,
            Distance        = distance,
            Gender          = gender,
            CompetitionDate = date ?? new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeOriginal    = "1:00.00",
            AgeGroup        = "Open",
            EventStyleAge   = $"{distance} {styleName} Open"
        };
        db.Results.Add(result);
        await db.SaveChangesAsync();
        return result;
    }

    // ── GetPagedAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPaged_EmptyDb_ReturnsEmptyList()
    {
        await using var db = CreateDb(nameof(GetPaged_EmptyDb_ReturnsEmptyList));
        var repo = new ResultRepository(db, NoCache());

        var (items, hasMore) = await repo.GetPagedAsync(new ResultFilter(), 1, 10);

        Assert.Empty(items);
        Assert.False(hasMore);
    }

    [Fact]
    public async Task GetPaged_HasMore_TrueWhenExceedingPageSize()
    {
        await using var db = CreateDb(nameof(GetPaged_HasMore_TrueWhenExceedingPageSize));
        await SeedResultAsync(db, "freestyle");
        await SeedResultAsync(db, "backstroke");
        await SeedResultAsync(db, "butterfly");
        var repo = new ResultRepository(db, NoCache());

        var (items, hasMore) = await repo.GetPagedAsync(new ResultFilter(), 1, 2);

        Assert.Equal(2, items.Count);
        Assert.True(hasMore);
    }

    [Fact]
    public async Task GetPaged_SecondPage_HasNoMore()
    {
        await using var db = CreateDb(nameof(GetPaged_SecondPage_HasNoMore));
        await SeedResultAsync(db, "freestyle");
        await SeedResultAsync(db, "backstroke");
        var repo = new ResultRepository(db, NoCache());

        var (items, hasMore) = await repo.GetPagedAsync(new ResultFilter(), 2, 10);

        Assert.Empty(items);
        Assert.False(hasMore);
    }

    [Fact]
    public async Task GetPaged_FilterByStyle_ReturnsOnlyMatching()
    {
        await using var db = CreateDb(nameof(GetPaged_FilterByStyle_ReturnsOnlyMatching));
        await SeedResultAsync(db, "freestyle");
        await SeedResultAsync(db, "backstroke");
        var repo = new ResultRepository(db, NoCache());

        var (items, _) = await repo.GetPagedAsync(
            new ResultFilter { StyleName = "freestyle" }, 1, 10);

        Assert.Single(items);
        Assert.Equal("freestyle", items[0].StyleName);
    }

    [Fact]
    public async Task GetPaged_FilterByDateRange_ExcludesOutOfRange()
    {
        await using var db = CreateDb(nameof(GetPaged_FilterByDateRange_ExcludesOutOfRange));
        await SeedResultAsync(db, "freestyle", date: new DateTime(2023, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        await SeedResultAsync(db, "backstroke", date: new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var repo = new ResultRepository(db, NoCache());

        var (items, _) = await repo.GetPagedAsync(new ResultFilter
        {
            DateFrom = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DateTo   = new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc)
        }, 1, 10);

        Assert.Single(items);
        Assert.Equal("backstroke", items[0].StyleName);
    }

    // ── GetByIdAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_ExistingId_ReturnsDto()
    {
        await using var db = CreateDb(nameof(GetById_ExistingId_ReturnsDto));
        var record = await SeedResultAsync(db);
        var repo = new ResultRepository(db, NoCache());

        var dto = await repo.GetByIdAsync(record.Id);

        Assert.NotNull(dto);
        Assert.Equal(record.Id, dto!.Id);
        Assert.Equal("freestyle", dto.StyleName);
    }

    [Fact]
    public async Task GetById_NonExistingId_ReturnsNull()
    {
        await using var db = CreateDb(nameof(GetById_NonExistingId_ReturnsNull));
        var repo = new ResultRepository(db, NoCache());

        var dto = await repo.GetByIdAsync(99999L);

        Assert.Null(dto);
    }

    // ── GetFilterHintsAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetFilterHints_StyleField_ReturnsAllStyleNames()
    {
        await using var db = CreateDb(nameof(GetFilterHints_StyleField_ReturnsAllStyleNames));
        await SeedResultAsync(db, "freestyle");
        await SeedResultAsync(db, "butterfly");
        var repo = new ResultRepository(db, NoCache());

        var hints = await repo.GetFilterHintsAsync("style", null, 10);

        Assert.Contains("freestyle", hints);
        Assert.Contains("butterfly", hints);
    }

    [Fact]
    public async Task GetFilterHints_UnknownField_ReturnsEmpty()
    {
        await using var db = CreateDb(nameof(GetFilterHints_UnknownField_ReturnsEmpty));
        var repo = new ResultRepository(db, NoCache());

        var hints = await repo.GetFilterHintsAsync("unknown_field", null, 10);

        Assert.Empty(hints);
    }

    [Fact]
    public async Task GetFilterHints_CacheHit_ReturnsCachedArrayWithoutDbQuery()
    {
        await using var db = CreateDb(nameof(GetFilterHints_CacheHit_ReturnsCachedArrayWithoutDbQuery));
        // DB пустая — если кеш сработает, результат придёт из кеша, а не из DB
        var expected = new[] { "freestyle", "backstroke" };
        var cacheMock = new Mock<ICacheService>();
        cacheMock.Setup(c => c.GetAsync<string[]>(It.IsAny<string>()))
                 .ReturnsAsync(expected);
        var repo = new ResultRepository(db, cacheMock.Object);

        var hints = await repo.GetFilterHintsAsync("style", null, 10);

        Assert.Equal(expected, hints);
    }
}
