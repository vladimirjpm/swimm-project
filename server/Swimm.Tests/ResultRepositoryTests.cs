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
        DateTime? date   = null,
        int birthYear    = 2000,
        string ageGroup  = "Open",
        int? position    = null)
    {
        var style   = new Style { Name = styleName };
        var club    = new Club { Name = "TestClub", NameEn = "TestClub" };
        var comp    = new Competition
        {
            Name = "TestComp", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "01/01/2024", PoolType = poolType
        };
        var swimmer = new Swimmer
        {
            LastName = "Иванов", FirstName = "Иван",
            LastNameEn = "Ivanov", FirstNameEn = "Ivan",
            BirthYear = birthYear
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
            AgeGroup        = ageGroup,
            EventStyleAge   = $"{distance} {styleName} Open",
            Position        = position
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

        var (items, hasMore, total) = await repo.GetPagedAsync(new ResultFilter(), 1, 10);

        Assert.Empty(items);
        Assert.False(hasMore);
        Assert.Equal(0, total);
    }

    [Fact]
    public async Task GetPaged_HasMore_TrueWhenExceedingPageSize()
    {
        await using var db = CreateDb(nameof(GetPaged_HasMore_TrueWhenExceedingPageSize));
        await SeedResultAsync(db, "freestyle");
        await SeedResultAsync(db, "backstroke");
        await SeedResultAsync(db, "butterfly");
        var repo = new ResultRepository(db, NoCache());

        var (items, hasMore, total) = await repo.GetPagedAsync(new ResultFilter(), 1, 2);

        Assert.Equal(2, items.Count);
        Assert.True(hasMore);
        Assert.Equal(3, total);
    }

    [Fact]
    public async Task GetPaged_SecondPage_HasNoMore()
    {
        await using var db = CreateDb(nameof(GetPaged_SecondPage_HasNoMore));
        await SeedResultAsync(db, "freestyle");
        await SeedResultAsync(db, "backstroke");
        var repo = new ResultRepository(db, NoCache());

        var (items, hasMore, total) = await repo.GetPagedAsync(new ResultFilter(), 2, 10);

        Assert.Empty(items);
        Assert.False(hasMore);
        Assert.Equal(2, total);
    }

    [Fact]
    public async Task GetPaged_FilterByStyle_ReturnsOnlyMatching()
    {
        await using var db = CreateDb(nameof(GetPaged_FilterByStyle_ReturnsOnlyMatching));
        await SeedResultAsync(db, "freestyle");
        await SeedResultAsync(db, "backstroke");
        var repo = new ResultRepository(db, NoCache());

        var (items, _, _) = await repo.GetPagedAsync(
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

        var (items, _, _) = await repo.GetPagedAsync(new ResultFilter
        {
            DateFrom = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DateTo   = new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc)
        }, 1, 10);

        Assert.Single(items);
        Assert.Equal("backstroke", items[0].StyleName);
    }

    // ── GetPagedAsync: параметры paged-режима (фаза 3.2) ─────────────────────

    [Fact]
    public async Task GetPaged_FilterByBirthYearRange_ExcludesOutOfRange()
    {
        await using var db = CreateDb(nameof(GetPaged_FilterByBirthYearRange_ExcludesOutOfRange));
        await SeedResultAsync(db, "freestyle", birthYear: 2005);
        await SeedResultAsync(db, "backstroke", birthYear: 2010);
        await SeedResultAsync(db, "butterfly", birthYear: 2015);
        var repo = new ResultRepository(db, NoCache());

        var (items, _, total) = await repo.GetPagedAsync(
            new ResultFilter { BirthYearFrom = 2008, BirthYearTo = 2012 }, 1, 10);

        Assert.Single(items);
        Assert.Equal("backstroke", items[0].StyleName);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetPaged_FilterByAgeGroup_ExactMatch()
    {
        await using var db = CreateDb(nameof(GetPaged_FilterByAgeGroup_ExactMatch));
        await SeedResultAsync(db, "freestyle", ageGroup: "25-29");
        await SeedResultAsync(db, "backstroke", ageGroup: "30-34");
        var repo = new ResultRepository(db, NoCache());

        var (items, _, _) = await repo.GetPagedAsync(
            new ResultFilter { AgeGroup = "25-29" }, 1, 10);

        Assert.Single(items);
        Assert.Equal("freestyle", items[0].StyleName);
    }

    [Fact]
    public async Task GetPaged_FilterByPositionMax_MirrorsClientSemantics()
    {
        await using var db = CreateDb(nameof(GetPaged_FilterByPositionMax_MirrorsClientSemantics));
        await SeedResultAsync(db, "freestyle", position: 2);
        await SeedResultAsync(db, "backstroke", position: 7);
        await SeedResultAsync(db, "butterfly", position: null); // DSQ/DNS — без места
        var repo = new ResultRepository(db, NoCache());

        // podium (KeepUnranked=false): только места 1–3, без DSQ/DNS.
        var (podium, _, _) = await repo.GetPagedAsync(
            new ResultFilter { PositionMax = 3 }, 1, 10);
        // top (KeepUnranked=true): места 1–10 ПЛЮС строки без места — зеркало клиентского фильтра.
        var (top, _, _) = await repo.GetPagedAsync(
            new ResultFilter { PositionMax = 10, PositionKeepUnranked = true }, 1, 10);

        Assert.Single(podium);
        Assert.Equal("freestyle", podium[0].StyleName);
        Assert.Equal(3, top.Count);
    }

    [Fact]
    public async Task GetPaged_FilterByEventDate_ReturnsOnlyThatDay()
    {
        await using var db = CreateDb(nameof(GetPaged_FilterByEventDate_ReturnsOnlyThatDay));
        var day1 = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2024, 6, 2, 0, 0, 0, DateTimeKind.Utc);
        await SeedResultAsync(db, "freestyle", date: day1);
        await SeedResultAsync(db, "backstroke", date: day2);
        var repo = new ResultRepository(db, NoCache());

        var (items, _, _) = await repo.GetPagedAsync(
            new ResultFilter { EventDate = day2 }, 1, 10);

        Assert.Single(items);
        Assert.Equal("backstroke", items[0].StyleName);
    }

    [Fact]
    public async Task GetPaged_Total_IsFilteredCountNotPageCount()
    {
        await using var db = CreateDb(nameof(GetPaged_Total_IsFilteredCountNotPageCount));
        await SeedResultAsync(db, "freestyle");
        await SeedResultAsync(db, "freestyle");
        await SeedResultAsync(db, "freestyle");
        await SeedResultAsync(db, "backstroke");
        var repo = new ResultRepository(db, NoCache());

        var (items, hasMore, total) = await repo.GetPagedAsync(
            new ResultFilter { StyleName = "freestyle" }, 1, 2);

        Assert.Equal(2, items.Count);
        Assert.True(hasMore);
        Assert.Equal(3, total); // все freestyle, а не размер страницы и не все 4 строки
    }

    // ── GetPagedAsync: EventId → CompetitionId(ы) резолв (perf-фикс) ─────────

    [Fact]
    public async Task GetPaged_FilterByEventId_ReturnsOnlyResultsFromEventCompetitions()
    {
        await using var db = CreateDb(nameof(GetPaged_FilterByEventId_ReturnsOnlyResultsFromEventCompetitions));

        // Два дня одного события (EventId=42) + одно "чужое" соревнование без EventId.
        var style   = new Style { Name = "freestyle" };
        var club    = new Club { Name = "TestClub", NameEn = "TestClub" };
        var swimmer = new Swimmer
        {
            LastName = "Иванов", FirstName = "Иван",
            LastNameEn = "Ivanov", FirstNameEn = "Ivan", BirthYear = 2000
        };
        var day1 = new Competition
        {
            Name = "Event Day 1", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "01/01/2024", PoolType = "50m", EventId = 42
        };
        var day2 = new Competition
        {
            Name = "Event Day 2", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "02/01/2024", PoolType = "50m", EventId = 42
        };
        var other = new Competition
        {
            Name = "Other Comp", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "03/01/2024", PoolType = "50m", EventId = null
        };
        db.AddRange(style, club, swimmer, day1, day2, other);
        await db.SaveChangesAsync();

        var date = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        ResultRecord Row(int compId) => new()
        {
            CompetitionId = compId, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            Distance = "100", Gender = "male", CompetitionDate = date, TimeOriginal = "1:00.00",
            AgeGroup = "Open", EventStyleAge = "100 freestyle Open"
        };
        db.Results.AddRange(Row(day1.Id), Row(day2.Id), Row(other.Id));
        await db.SaveChangesAsync();

        var repo = new ResultRepository(db, NoCache());

        var (items, _, total) = await repo.GetPagedAsync(new ResultFilter { EventId = 42 }, 1, 100);

        Assert.Equal(2, total);
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Contains(i.CompetitionName, new[] { "Event Day 1", "Event Day 2" }));
        Assert.DoesNotContain(items, i => i.CompetitionName == "Other Comp");
    }

    [Fact]
    public async Task GetPaged_FilterByEventId_UnknownEvent_ReturnsEmpty()
    {
        await using var db = CreateDb(nameof(GetPaged_FilterByEventId_UnknownEvent_ReturnsEmpty));
        await SeedResultAsync(db, "freestyle");
        var repo = new ResultRepository(db, NoCache());

        var (items, hasMore, total) = await repo.GetPagedAsync(new ResultFilter { EventId = 999 }, 1, 100);

        Assert.Empty(items);
        Assert.False(hasMore);
        Assert.Equal(0, total);
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

    // ── GetClubSummaryAsync (фаза 3.4) ────────────────────────────────────────

    /// <summary>Сеет одно соревнование + правило очков (1→30,2→28,3→26, иначе 0) и набор заплывов.</summary>
    private static async Task<int> SeedClubSummaryFixtureAsync(SwimmReadDbContext db)
    {
        var style = new Style { Name = "freestyle" };
        var clubA = new Club { Name = "Alpha", NameEn = "Alpha" };
        var clubB = new Club { Name = "Beta", NameEn = "Beta" };
        var comp = new Competition
        {
            Name = "Meet", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "01/01/2024", PoolType = "50m", IsMasters = false
        };
        var s1 = new Swimmer { LastName = "Aaa", FirstName = "A", LastNameEn = "Aaa", FirstNameEn = "A", BirthYear = 2000 };
        var s2 = new Swimmer { LastName = "Bbb", FirstName = "B", LastNameEn = "Bbb", FirstNameEn = "B", BirthYear = 2000 };
        db.AddRange(style, clubA, clubB, comp, s1, s2);
        db.ClubPointsRules.Add(new ClubPointsRule
        {
            Version = "test", Scope = "all", EffectiveFrom = new DateOnly(2000, 1, 1), DefaultPoints = 0,
            Entries =
            [
                new ClubPointsRuleEntry { Place = 1, Points = 30 },
                new ClubPointsRuleEntry { Place = 2, Points = 28 },
                new ClubPointsRuleEntry { Place = 3, Points = 26 },
            ]
        });
        await db.SaveChangesAsync();

        var date = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        ResultRecord Row(int club, int swimmer, int? pos) => new()
        {
            CompetitionId = comp.Id, SwimmerId = swimmer, ClubId = club, StyleId = style.Id,
            Distance = "100", Gender = "male", CompetitionDate = date, TimeOriginal = "1:00.00",
            AgeGroup = "Open", EventStyleAge = "100 freestyle Open", Position = pos
        };
        // Alpha: пловец s1 — золото (30) и серебро (28); пловец s2 — 5-е место (0 очков).
        db.Results.AddRange(Row(clubA.Id, s1.Id, 1), Row(clubA.Id, s1.Id, 2), Row(clubA.Id, s2.Id, 5));
        // Beta: бронза (26).
        db.Results.Add(Row(clubB.Id, s2.Id, 3));
        await db.SaveChangesAsync();
        return comp.Id;
    }

    [Fact]
    public async Task GetClubSummary_AggregatesPointsMedalsAndSwimmers()
    {
        await using var db = CreateDb(nameof(GetClubSummary_AggregatesPointsMedalsAndSwimmers));
        var compId = await SeedClubSummaryFixtureAsync(db);
        var repo = new ResultRepository(db, NoCache());

        var summary = await repo.GetClubSummaryAsync(new ResultFilter { CompetitionId = compId });

        Assert.Equal(2, summary.Count);
        // Отсортировано по очкам убыв.: Alpha (30+28=58) впереди Beta (26).
        var alpha = summary[0];
        Assert.Equal("Alpha", alpha.Club);
        Assert.Equal(58, alpha.Points);
        Assert.Equal(1, alpha.Gold);
        Assert.Equal(1, alpha.Silver);
        Assert.Equal(0, alpha.Bronze);
        Assert.Equal(2, alpha.SwimmerCount);   // s1 и s2 (по фамилиям)
        Assert.Equal(2, alpha.SuccessfulCount); // только заплывы с очками > 0

        var beta = summary[1];
        Assert.Equal("Beta", beta.Club);
        Assert.Equal(26, beta.Points);
        Assert.Equal(1, beta.Bronze);
    }

    [Fact]
    public async Task GetClubSummary_RelayDoublesPoints()
    {
        await using var db = CreateDb(nameof(GetClubSummary_RelayDoublesPoints));
        var style = new Style { Name = "freestyle" };
        var comp = new Competition
        {
            Name = "Relay Meet", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "01/01/2024", PoolType = "50m", IsMasters = false
        };
        var swimmer = new Swimmer { LastName = "Xxx", FirstName = "X", LastNameEn = "Xxx", FirstNameEn = "X", BirthYear = 2000 };
        var relay = new Relay { TeamName = "Gamma Relay", SwimmersName = "X Xxx, Y Yyy" };
        // Эстафета в БД привязана к клубу (ClubId не nullable); имя клуба пустое —
        // ключ уходит на Relay.TeamName, как в клиентском getClubsSummary.
        var emptyClub = new Club { Name = "", NameEn = "" };
        db.AddRange(style, comp, swimmer, relay, emptyClub);
        db.ClubPointsRules.Add(new ClubPointsRule
        {
            Version = "test", Scope = "all", EffectiveFrom = new DateOnly(2000, 1, 1), DefaultPoints = 0,
            Entries = [new ClubPointsRuleEntry { Place = 1, Points = 30 }]
        });
        await db.SaveChangesAsync();

        // Эстафетный заплыв без ClubId — клуб берётся из Relay.TeamName; золото → 30×2 = 60.
        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = emptyClub.Id, StyleId = style.Id,
            RelayId = relay.Id, Distance = "4x100", Gender = "male",
            CompetitionDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeOriginal = "3:30.00", AgeGroup = "Open", EventStyleAge = "4x100 freestyle Open", Position = 1
        });
        await db.SaveChangesAsync();
        var repo = new ResultRepository(db, NoCache());

        var summary = await repo.GetClubSummaryAsync(new ResultFilter { CompetitionId = comp.Id });

        var gamma = Assert.Single(summary);
        Assert.Equal("Gamma Relay", gamma.Club);
        Assert.Equal(60, gamma.Points);
        Assert.Equal(1, gamma.Gold);
    }

    [Fact]
    public async Task GetClubSummary_EmptyDb_ReturnsEmpty()
    {
        await using var db = CreateDb(nameof(GetClubSummary_EmptyDb_ReturnsEmpty));
        var repo = new ResultRepository(db, NoCache());

        var summary = await repo.GetClubSummaryAsync(new ResultFilter { CompetitionId = 123 });

        Assert.Empty(summary);
    }
}
