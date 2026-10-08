using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Overview отдаёт club_points_unofficial — витрина подписывает клубный зачёт чипом
/// «Unofficial», когда официального зачёта у соревнования нет, а очки посчитаны нами по шкале
/// правила (docs/competition-overview-cards.md, раздел Top clubs).
/// Признак: HasOfficialClubStanding = false, либо null + ClubPointsDisabled; явное true главнее
/// пометки, ручная привязка правила снимает признак. В выборке из нескольких соревнований —
/// только если так у всех.
/// </summary>
public class OverviewClubPointsUnofficialTests
{
    private static SwimmReadDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NullCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static ResultRecord Row(int compId, int swimmerId, int clubId, int styleId) => new()
    {
        CompetitionId = compId, SwimmerId = swimmerId, ClubId = clubId, StyleId = styleId,
        Distance = "100", Gender = "male",
        CompetitionDate = new DateTime(2025, 4, 25, 0, 0, 0, DateTimeKind.Utc),
        TimeOriginal = "1:00.00", Position = 1, AgeGroup = "Open",
        EventStyleAge = "100 freestyle Open"
    };

    private static Competition Comp(bool? hasOfficial, bool disabled, int? eventId = null, int? day = null) => new()
    {
        Name = "Memorial", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
        Date = "25/04/2025", PoolType = "50m",
        HasOfficialClubStanding = hasOfficial, ClubPointsDisabled = disabled,
        EventId = eventId, DayNumber = day
    };

    [Theory]
    [InlineData(false, false, true)]   // loglig проверен, зачёта нет
    [InlineData(false, true, true)]
    [InlineData(null, true, true)]     // не проверяли, но админ пометил «не ведётся»
    [InlineData(null, false, false)]   // не проверяли и пометки нет — не утверждаем
    [InlineData(true, false, false)]   // официальный зачёт есть
    [InlineData(true, true, false)]    // явное true главнее противоречащей пометки
    public async Task SingleCompetition_FlagFollowsOfficialStanding(bool? hasOfficial, bool disabled, bool expected)
    {
        await using var db = CreateDb($"{nameof(SingleCompetition_FlagFollowsOfficialStanding)}_{hasOfficial}_{disabled}");

        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var swimmer = new Swimmer { FirstName = "A", LastName = "B", FirstNameEn = "A", LastNameEn = "B", BirthYear = 2012 };
        var comp = Comp(hasOfficial, disabled);
        db.AddRange(style, club, swimmer, comp);
        await db.SaveChangesAsync();
        db.Results.Add(Row(comp.Id, swimmer.Id, club.Id, style.Id));
        await db.SaveChangesAsync();

        var overview = await new ResultRepository(db, new NullCache())
            .GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = comp.Id });

        Assert.Equal(expected, overview.ClubPointsUnofficial);
    }

    [Fact]
    public async Task ExplicitRule_RemovesUnofficial_EvenWithoutLogligTable()
    {
        // Чемпионат, чей зачёт на loglig не публикуется, но правило привязано вручную по
        // регламенту: шкалу выбрал организатор, а не автоподбор — чипа нет (решение 08.10.2026).
        await using var db = CreateDb(nameof(ExplicitRule_RemovesUnofficial_EvenWithoutLogligTable));

        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var swimmer = new Swimmer { FirstName = "A", LastName = "B", FirstNameEn = "A", LastNameEn = "B", BirthYear = 2012 };
        var rule = new PointRuleClubs
        {
            Version = "test.30", EffectiveFrom = new DateOnly(2025, 1, 1), Scope = "all",
            DefaultPoints = 0, MaxScoringPlace = 24, RelayMultiplier = 2, ManualOnly = true,
            Entries = { new PointRuleClubsEntry { Place = 1, Points = 30 } }
        };
        db.AddRange(style, club, swimmer, rule);
        await db.SaveChangesAsync();
        var comp = Comp(false, false);
        comp.PointRuleClubsId = rule.Id;
        db.Add(comp);
        await db.SaveChangesAsync();
        db.Results.Add(Row(comp.Id, swimmer.Id, club.Id, style.Id));
        await db.SaveChangesAsync();

        var overview = await new ResultRepository(db, new NullCache())
            .GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = comp.Id });

        Assert.False(overview.ClubPointsUnofficial);
    }

    [Fact]
    public async Task Event_AllDaysWithoutStanding_Unofficial()
    {
        await using var db = CreateDb(nameof(Event_AllDaysWithoutStanding_Unofficial));
        var overview = await EventOverview(db, (false, false), (null, true));

        Assert.True(overview.ClubPointsUnofficial);
    }

    [Fact]
    public async Task Event_OneDayWithOfficialStanding_NotUnofficial()
    {
        // Сумма, в которую вошёл официальный зачёт, уже не «неофициальная целиком» — чип врал бы.
        await using var db = CreateDb(nameof(Event_OneDayWithOfficialStanding_NotUnofficial));
        var overview = await EventOverview(db, (false, false), (true, false));

        Assert.False(overview.ClubPointsUnofficial);
    }

    private static async Task<CompetitionOverviewDto> EventOverview(
        SwimmReadDbContext db, params (bool? hasOfficial, bool disabled)[] days)
    {
        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var swimmer = new Swimmer { FirstName = "A", LastName = "B", FirstNameEn = "A", LastNameEn = "B", BirthYear = 2012 };
        var evt = new CompetitionEvent { Name = "Memorial" };
        db.AddRange(style, club, swimmer, evt);
        await db.SaveChangesAsync();

        var comps = days.Select((d, i) => Comp(d.hasOfficial, d.disabled, evt.Id, i + 1)).ToList();
        db.AddRange(comps);
        await db.SaveChangesAsync();
        db.Results.AddRange(comps.Select(c => Row(c.Id, swimmer.Id, club.Id, style.Id)));
        await db.SaveChangesAsync();

        var overview = await new ResultRepository(db, new NullCache())
            .GetCompetitionOverviewAsync(new ResultFilter { EventId = evt.Id });
        return overview;
    }
}
