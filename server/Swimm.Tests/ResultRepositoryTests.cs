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
        db.PointRulesClubs.Add(new PointRuleClubs
        {
            Version = "test", Scope = "all", EffectiveFrom = new DateOnly(2000, 1, 1), DefaultPoints = 0,
            Entries =
            [
                new PointRuleClubsEntry { Place = 1, Points = 30 },
                new PointRuleClubsEntry { Place = 2, Points = 28 },
                new PointRuleClubsEntry { Place = 3, Points = 26 },
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
        db.PointRulesClubs.Add(new PointRuleClubs
        {
            Version = "test", Scope = "all", EffectiveFrom = new DateOnly(2000, 1, 1), DefaultPoints = 0,
            Entries = [new PointRuleClubsEntry { Place = 1, Points = 30 }]
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
    public async Task GetPaged_RelayRow_CarriesMemberSwimmerIds()
    {
        // Страж относительно docs/relays.md (чек-лист п.3): /api/results обязан отдавать
        // состав ног member_swimmer_ids — клиентские скоупы my/favorites и персональная
        // полоса матчат эстафету по нему, а не по SwimmerId владельца строки.
        // Регресс без этого поля: 4X50 комплекс терялся из ?filter=favorites (0875c1c).
        await using var db = CreateDb(nameof(GetPaged_RelayRow_CarriesMemberSwimmerIds));
        var style = new Style { Name = "freestyle" };
        var comp = new Competition
        {
            Name = "Relay Meet", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "01/01/2024", PoolType = "50m", IsMasters = false
        };
        var owner = new Swimmer { LastName = "Owner", FirstName = "O", LastNameEn = "Owner", FirstNameEn = "O", BirthYear = 2000 };
        var leg2 = new Swimmer { LastName = "Leg", FirstName = "L", LastNameEn = "Leg", FirstNameEn = "L", BirthYear = 2001 };
        var club = new Club { Name = "C", NameEn = "C" };
        var relay = new Relay { TeamName = "Team", SwimmersName = "O Owner, L Leg" };
        db.AddRange(style, comp, owner, leg2, club, relay);
        await db.SaveChangesAsync();
        db.RelayMembers.AddRange(
            new RelayMember { RelayId = relay.Id, SwimmerId = owner.Id, LegOrder = 1 },
            new RelayMember { RelayId = relay.Id, SwimmerId = leg2.Id, LegOrder = 2 });
        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = owner.Id, ClubId = club.Id, StyleId = style.Id,
            RelayId = relay.Id, Distance = "4X50", Gender = "male",
            CompetitionDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeOriginal = "2:00.00", AgeGroup = "Open", EventStyleAge = "4X50 freestyle Open", Position = 1
        });
        await db.SaveChangesAsync();
        var repo = new ResultRepository(db, NoCache());

        var (items, _, _) = await repo.GetPagedAsync(new ResultFilter { CompetitionId = comp.Id }, 1, 10);

        var row = Assert.Single(items);
        Assert.True(row.IsRelay);
        Assert.NotNull(row.MemberSwimmerIds);
        // Нога-не-владелец обязана присутствовать — по ней клиент матчит эстафету.
        Assert.Contains(leg2.Id, row.MemberSwimmerIds!);
        Assert.Contains(owner.Id, row.MemberSwimmerIds!);
    }

    [Fact]
    public async Task GetClubSummary_EmptyDb_ReturnsEmpty()
    {
        await using var db = CreateDb(nameof(GetClubSummary_EmptyDb_ReturnsEmpty));
        var repo = new ResultRepository(db, NoCache());

        var summary = await repo.GetClubSummaryAsync(new ResultFilter { CompetitionId = 123 });

        Assert.Empty(summary);
    }

    // ── GetCompetitionOverviewAsync ─────────────────────────────────────────────

    /// <summary>
    /// Многодневное событие (EventId=1, 2 Competition-дня) для тестов Summary/Days:
    /// день 1 — 4 строки (2 личных клуба-A + 1 эстафета клуба-A + 1 личная псевдоклуба),
    /// день 2 — 2 строки (личные, клуб-B). Итого: result_count=6, swimmer_count=4 (личные
    /// s1,s2,s4,s5 — эстафетный s3 не в счёте), club_count=2 (clubA, clubB — псевдоклуб не в счёте).
    /// </summary>
    private static async Task<int> SeedOverviewFixtureAsync(SwimmReadDbContext db)
    {
        var style = new Style { Name = "freestyle" };
        var clubA = new Club { Name = "Alpha", NameEn = "Alpha" };
        var clubB = new Club { Name = "Beta", NameEn = "Beta" };
        var pseudoClub = new Club { Name = "Unattached", NameEn = "Unattached", IsPseudo = true };
        var compA = new Competition
        {
            Name = "Meet", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "01/01/2024", PoolType = "50m", EventId = 1, DayNumber = 1
        };
        var compB = new Competition
        {
            Name = "Meet", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "02/01/2024", PoolType = "50m", EventId = 1, DayNumber = 2
        };
        var s1 = new Swimmer { LastName = "Aaa", FirstName = "A", LastNameEn = "Aaa", FirstNameEn = "A", BirthYear = 2000 };
        var s2 = new Swimmer { LastName = "Bbb", FirstName = "B", LastNameEn = "Bbb", FirstNameEn = "B", BirthYear = 2000 };
        var s3 = new Swimmer { LastName = "Ccc", FirstName = "C", LastNameEn = "Ccc", FirstNameEn = "C", BirthYear = 2000 };
        var s4 = new Swimmer { LastName = "Ddd", FirstName = "D", LastNameEn = "Ddd", FirstNameEn = "D", BirthYear = 2000 };
        var s5 = new Swimmer { LastName = "Eee", FirstName = "E", LastNameEn = "Eee", FirstNameEn = "E", BirthYear = 2000 };
        var relay = new Relay { TeamName = "Alpha Relay", SwimmersName = "C Ccc" };
        db.AddRange(style, clubA, clubB, pseudoClub, compA, compB, s1, s2, s3, s4, s5, relay);
        await db.SaveChangesAsync();

        var date1 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var date2 = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        ResultRecord Row(int comp, DateTime date, int club, int swimmer, int? relayId = null) => new()
        {
            CompetitionId = comp, SwimmerId = swimmer, ClubId = club, StyleId = style.Id,
            RelayId = relayId, Distance = relayId is null ? "100" : "4x100", Gender = "male",
            CompetitionDate = date, TimeOriginal = "1:00.00", AgeGroup = "Open",
            EventStyleAge = "100 freestyle Open"
        };
        // День 1: 4 строки.
        db.Results.AddRange(
            Row(compA.Id, date1, clubA.Id, s1.Id),
            Row(compA.Id, date1, clubA.Id, s2.Id),
            Row(compA.Id, date1, clubA.Id, s3.Id, relay.Id),
            Row(compA.Id, date1, pseudoClub.Id, s4.Id));
        // День 2: 2 строки.
        db.Results.AddRange(
            Row(compB.Id, date2, clubB.Id, s2.Id),
            Row(compB.Id, date2, clubB.Id, s5.Id));
        await db.SaveChangesAsync();
        return 1; // EventId
    }

    [Fact]
    public async Task Overview_EmptyDb_ReturnsEmptyDto()
    {
        await using var db = CreateDb(nameof(Overview_EmptyDb_ReturnsEmptyDto));
        var repo = new ResultRepository(db, NoCache());

        var overview = await repo.GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = 999 });

        Assert.Equal(0, overview.Summary.ResultCount);
        Assert.Equal(0, overview.Summary.DayCount);
        Assert.Equal(0, overview.Summary.SwimmerCount);
        Assert.Equal(0, overview.Summary.ClubCount);
        Assert.Empty(overview.Days);
        Assert.Null(overview.BestSwim);
        Assert.Null(overview.TopMedalist);
        Assert.Empty(overview.TopClubs);
        Assert.Empty(overview.Records);
    }

    [Fact]
    public async Task Overview_Summary_CountsSwimmersClubsDaysResults()
    {
        await using var db = CreateDb(nameof(Overview_Summary_CountsSwimmersClubsDaysResults));
        var eventId = await SeedOverviewFixtureAsync(db);
        var repo = new ResultRepository(db, NoCache());

        var overview = await repo.GetCompetitionOverviewAsync(new ResultFilter { EventId = eventId });

        Assert.Equal(6, overview.Summary.ResultCount);
        Assert.Equal(2, overview.Summary.DayCount);
        Assert.Equal(4, overview.Summary.SwimmerCount);  // s1,s2,s4,s5 — эстафетный s3 не в счёте
        Assert.Equal(2, overview.Summary.ClubCount);      // Alpha, Beta — псевдоклуб не в счёте
    }

    [Fact]
    public async Task Overview_Days_OrderedWithPerDayCounts()
    {
        await using var db = CreateDb(nameof(Overview_Days_OrderedWithPerDayCounts));
        var eventId = await SeedOverviewFixtureAsync(db);
        var repo = new ResultRepository(db, NoCache());

        var overview = await repo.GetCompetitionOverviewAsync(new ResultFilter { EventId = eventId });

        Assert.Equal(2, overview.Days.Count);
        var day1 = overview.Days[0];
        var day2 = overview.Days[1];
        Assert.Equal(1, day1.DayNumber);
        Assert.Equal(4, day1.ResultCount);
        Assert.Equal(2, day2.DayNumber);
        Assert.Equal(2, day2.ResultCount);
    }

    [Fact]
    public async Task Overview_BestSwim_MaxPointsWithTieBreaks()
    {
        await using var db = CreateDb(nameof(Overview_BestSwim_MaxPointsWithTieBreaks));
        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var comp = new Competition
        {
            Name = "Meet", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "01/01/2024", PoolType = "50m"
        };
        var sFast = new Swimmer { LastName = "Fast", FirstName = "F", LastNameEn = "Fast", FirstNameEn = "F", BirthYear = 2000 };
        var sSlow = new Swimmer { LastName = "Slow", FirstName = "S", LastNameEn = "Slow", FirstNameEn = "S", BirthYear = 2000 };
        var sFail = new Swimmer { LastName = "Fail", FirstName = "X", LastNameEn = "Fail", FirstNameEn = "X", BirthYear = 2000 };
        db.AddRange(style, club, comp, sFast, sSlow, sFail);
        await db.SaveChangesAsync();

        var date = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // Равные очки (800), но sFast быстрее — должен победить по тай-брейку времени.
        db.Results.AddRange(
            new ResultRecord
            {
                CompetitionId = comp.Id, SwimmerId = sSlow.Id, ClubId = club.Id, StyleId = style.Id,
                Distance = "100", Gender = "male", CompetitionDate = date, TimeOriginal = "1:00.00",
                TimeMillisecond = 60000, InternationalPoints = 800, AgeGroup = "Open", EventStyleAge = "100 freestyle Open"
            },
            new ResultRecord
            {
                CompetitionId = comp.Id, SwimmerId = sFast.Id, ClubId = club.Id, StyleId = style.Id,
                Distance = "100", Gender = "male", CompetitionDate = date, TimeOriginal = "0:55.00",
                TimeMillisecond = 55000, InternationalPoints = 800, AgeGroup = "Open", EventStyleAge = "100 freestyle Open"
            },
            // Больше очков, но TimeFail — игнорируется.
            new ResultRecord
            {
                CompetitionId = comp.Id, SwimmerId = sFail.Id, ClubId = club.Id, StyleId = style.Id,
                Distance = "100", Gender = "male", CompetitionDate = date, TimeOriginal = "0:50.00",
                TimeMillisecond = 50000, InternationalPoints = 900, TimeFail = true, AgeGroup = "Open",
                EventStyleAge = "100 freestyle Open"
            });
        await db.SaveChangesAsync();
        var repo = new ResultRepository(db, NoCache());

        var overview = await repo.GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = comp.Id });

        Assert.NotNull(overview.BestSwim);
        Assert.Equal(sFast.Id, overview.BestSwim!.SwimmerId);
        Assert.Equal(800, overview.BestSwim.Points);
    }

    [Fact]
    public async Task Overview_BestSwim_NullWhenNoPoints()
    {
        await using var db = CreateDb(nameof(Overview_BestSwim_NullWhenNoPoints));
        var comp = await SeedResultAsync(db);
        var repo = new ResultRepository(db, NoCache());

        var overview = await repo.GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = comp.CompetitionId });

        Assert.Null(overview.BestSwim);
    }

    [Fact]
    public async Task Overview_TopMedalist_CountsAndOrdering()
    {
        await using var db = CreateDb(nameof(Overview_TopMedalist_CountsAndOrdering));
        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var comp = new Competition
        {
            Name = "Meet", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "01/01/2024", PoolType = "50m"
        };
        // s1: 1 золото + 1 серебро = 2 медали. s2: 2 золота = 2 медали (тай-брейк по золоту).
        var s1 = new Swimmer { LastName = "Aaa", FirstName = "A", LastNameEn = "Aaa", FirstNameEn = "A", BirthYear = 2000 };
        var s2 = new Swimmer { LastName = "Bbb", FirstName = "B", LastNameEn = "Bbb", FirstNameEn = "B", BirthYear = 2000 };
        var s3 = new Swimmer { LastName = "Ccc", FirstName = "C", LastNameEn = "Ccc", FirstNameEn = "C", BirthYear = 2000 };
        var relay = new Relay { TeamName = "Alpha Relay", SwimmersName = "C Ccc" };
        db.AddRange(style, club, comp, s1, s2, s3, relay);
        await db.SaveChangesAsync();

        var date = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        ResultRecord Row(int swimmer, int? pos, bool timeFail = false, int? relayId = null) => new()
        {
            CompetitionId = comp.Id, SwimmerId = swimmer, ClubId = club.Id, StyleId = style.Id,
            RelayId = relayId, Distance = "100", Gender = "male", CompetitionDate = date,
            TimeOriginal = "1:00.00", TimeFail = timeFail, Position = pos, AgeGroup = "Open",
            EventStyleAge = "100 freestyle Open"
        };
        db.Results.AddRange(
            Row(s1.Id, 1), Row(s1.Id, 2),
            Row(s2.Id, 1), Row(s2.Id, 1),
            // Эстафетная "медаль" — не считается.
            Row(s3.Id, 1, relayId: relay.Id),
            // TimeFail на 1-м месте — не считается.
            Row(s3.Id, 1, timeFail: true));
        await db.SaveChangesAsync();
        var repo = new ResultRepository(db, NoCache());

        var overview = await repo.GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = comp.Id });

        Assert.NotNull(overview.TopMedalist);
        Assert.Equal(s2.Id, overview.TopMedalist!.SwimmerId); // равенство медалей (2=2), больше золота
        Assert.Equal(2, overview.TopMedalist.Gold);
        Assert.Equal(0, overview.TopMedalist.Silver);
    }

    [Fact]
    public async Task Overview_TopClubs_SplitByGender()
    {
        await using var db = CreateDb(nameof(Overview_TopClubs_SplitByGender));
        var style = new Style { Name = "freestyle" };
        var clubM = new Club { Name = "MenClub", NameEn = "MenClub" };
        var clubW = new Club { Name = "WomenClub", NameEn = "WomenClub" };
        var comp = new Competition
        {
            Name = "Meet", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "01/01/2024", PoolType = "50m", IsMasters = false
        };
        var sm = new Swimmer { LastName = "Mmm", FirstName = "M", LastNameEn = "Mmm", FirstNameEn = "M", BirthYear = 2000 };
        var sw = new Swimmer { LastName = "Www", FirstName = "W", LastNameEn = "Www", FirstNameEn = "W", BirthYear = 2000 };
        db.AddRange(style, clubM, clubW, comp, sm, sw);
        db.PointRulesClubs.Add(new PointRuleClubs
        {
            Version = "test", Scope = "all", EffectiveFrom = new DateOnly(2000, 1, 1), DefaultPoints = 0,
            Entries = [new PointRuleClubsEntry { Place = 1, Points = 30 }]
        });
        await db.SaveChangesAsync();

        var date = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Results.AddRange(
            new ResultRecord
            {
                CompetitionId = comp.Id, SwimmerId = sm.Id, ClubId = clubM.Id, StyleId = style.Id,
                // Значения как в реальной БД Results.Gender: "male"/"female" (НЕ "M"/"F")
                Distance = "100", Gender = "male", CompetitionDate = date, TimeOriginal = "1:00.00",
                AgeGroup = "Open", EventStyleAge = "100 freestyle Open", Position = 1
            },
            new ResultRecord
            {
                CompetitionId = comp.Id, SwimmerId = sw.Id, ClubId = clubW.Id, StyleId = style.Id,
                Distance = "100", Gender = "female", CompetitionDate = date, TimeOriginal = "1:00.00",
                AgeGroup = "Open", EventStyleAge = "100 freestyle Open", Position = 1
            });
        await db.SaveChangesAsync();
        var repo = new ResultRepository(db, NoCache());

        var overview = await repo.GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = comp.Id });

        Assert.Equal(2, overview.TopClubs.Count);
        var men = Assert.Single(overview.TopClubsMen);
        Assert.Equal("MenClub", men.Club);
        var women = Assert.Single(overview.TopClubsWomen);
        Assert.Equal("WomenClub", women.Club);
    }

    [Fact]
    public async Task Overview_HighPointAward_SumsPointsPerAgeGenderWithTiesAndExclusions()
    {
        await using var db = CreateDb(nameof(Overview_HighPointAward_SumsPointsPerAgeGenderWithTiesAndExclusions));
        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var comp = new Competition
        {
            Name = "Meet", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "01/01/2024", PoolType = "50m" // год 2024 → возраст = 2024 − BirthYear
        };
        // age 12 male: A (200+100=300) > B (250) → A выигрывает, без ничьи
        var m12a = new Swimmer { LastName = "M12A", FirstName = "A", LastNameEn = "M12A", FirstNameEn = "A", BirthYear = 2012 };
        var m12b = new Swimmer { LastName = "M12B", FirstName = "B", LastNameEn = "M12B", FirstNameEn = "B", BirthYear = 2012 };
        // age 13 female: две по 400 → ничья (обе, is_tie=true)
        var f13a = new Swimmer { LastName = "F13A", FirstName = "C", LastNameEn = "F13A", FirstNameEn = "C", BirthYear = 2011 };
        var f13b = new Swimmer { LastName = "F13B", FirstName = "D", LastNameEn = "F13B", FirstNameEn = "D", BirthYear = 2011 };
        // исключения: без года рождения; пол "none"
        var noYear = new Swimmer { LastName = "NoYear", FirstName = "N", LastNameEn = "NoYear", FirstNameEn = "N", BirthYear = 0 };
        var relaySw = new Swimmer { LastName = "Relay", FirstName = "R", LastNameEn = "Relay", FirstNameEn = "R", BirthYear = 2012 };
        var relay = new Relay { TeamName = "Alpha Relay", SwimmersName = "R Relay" };
        db.AddRange(style, club, comp, m12a, m12b, f13a, f13b, noYear, relaySw, relay);
        await db.SaveChangesAsync();

        var date = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        ResultRecord Row(int swimmer, string gender, int pts, int? relayId = null) => new()
        {
            CompetitionId = comp.Id, SwimmerId = swimmer, ClubId = club.Id, StyleId = style.Id,
            RelayId = relayId, Distance = relayId is null ? "100" : "4x100", Gender = gender,
            CompetitionDate = date, TimeOriginal = "1:00.00", AgeGroup = "Open",
            EventStyleAge = "100 freestyle Open", InternationalPoints = pts
        };
        db.Results.AddRange(
            Row(m12a.Id, "male", 200), Row(m12a.Id, "male", 100), // сумма 300
            Row(m12b.Id, "male", 250),
            Row(f13a.Id, "female", 400),
            Row(f13b.Id, "female", 400),
            Row(noYear.Id, "male", 999),          // без года рождения — исключён
            Row(relaySw.Id, "male", 999, relay.Id)); // эстафета — исключена
        await db.SaveChangesAsync();
        var repo = new ResultRepository(db, NoCache());

        var overview = await repo.GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = comp.Id });
        var awards = overview.HighPointAwards;

        // Ожидаем 3 награды: age12 male (A), age13 female (два — ничья). noYear и эстафета исключены.
        Assert.Equal(3, awards.Count);

        var m12 = Assert.Single(awards, a => a.Age == 12 && a.Gender == "male");
        Assert.Equal(m12a.Id, m12.SwimmerId);
        Assert.Equal(300, m12.Points);
        Assert.False(m12.IsTie);

        var f13 = awards.Where(a => a.Age == 13 && a.Gender == "female").ToList();
        Assert.Equal(2, f13.Count);
        Assert.All(f13, a => Assert.Equal(400, a.Points));
        Assert.All(f13, a => Assert.True(a.IsTie));
        Assert.Contains(f13, a => a.SwimmerId == f13a.Id);
        Assert.Contains(f13, a => a.SwimmerId == f13b.Id);

        // m12b (250) не выиграл; noYear и relay не в наградах.
        Assert.DoesNotContain(awards, a => a.SwimmerId == m12b.Id);
        Assert.DoesNotContain(awards, a => a.SwimmerId == noYear.Id);
        Assert.DoesNotContain(awards, a => a.SwimmerId == relaySw.Id);
    }
}
