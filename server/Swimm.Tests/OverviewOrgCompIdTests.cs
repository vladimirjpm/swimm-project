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
/// Overview соревнования отдаёт org_comp_id — адрес, по которому клиент открывает таб
/// Start list (docs/tasks/start-list-ui-sonnet.md, шаг 0). Источник: Competition.OrgCompId,
/// а у дня многодневки, где штамп не проставлен на день, — Competition.Event.OrgCompId
/// (см. CompetitionIdentity: штамп многодневки живёт на событии).
/// </summary>
public class OverviewOrgCompIdTests
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
        CompetitionDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        TimeOriginal = "1:00.00", Position = 1, AgeGroup = "Open",
        EventStyleAge = "100 freestyle Open"
    };

    [Fact]
    public async Task SingleDayCompetition_OrgCompId_FromCompetitionItself()
    {
        await using var db = CreateDb(nameof(SingleDayCompetition_OrgCompId_FromCompetitionItself));

        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var swimmer = new Swimmer { FirstName = "A", LastName = "B", FirstNameEn = "A", LastNameEn = "B", BirthYear = 2000 };
        var comp = new Competition
        {
            Name = "Meet", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "01/01/2024", PoolType = "50m", OrgCompId = 16835
        };
        db.AddRange(style, club, swimmer, comp);
        await db.SaveChangesAsync();
        db.Results.Add(Row(comp.Id, swimmer.Id, club.Id, style.Id));
        await db.SaveChangesAsync();

        var overview = await new ResultRepository(db, new NullCache())
            .GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = comp.Id });

        Assert.Equal(16835, overview.OrgCompId);
    }

    [Fact]
    public async Task MultiDayEvent_DayWithoutOwnStamp_FallsBackToEventOrgCompId()
    {
        await using var db = CreateDb(nameof(MultiDayEvent_DayWithoutOwnStamp_FallsBackToEventOrgCompId));

        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var swimmer = new Swimmer { FirstName = "A", LastName = "B", FirstNameEn = "A", LastNameEn = "B", BirthYear = 2000 };
        var evt = new CompetitionEvent { Name = "Champs", OrgCompId = 16786 };
        db.AddRange(style, club, swimmer, evt);
        await db.SaveChangesAsync();

        // День 2 многодневки: штамп OrgCompId на самом дне пуст (унитарный ключ достался
        // только одному дню), но событие несёт штамп — овервью должен взять его оттуда.
        var day2 = new Competition
        {
            Name = "Champs day 2", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "02/01/2024", PoolType = "50m", EventId = evt.Id, DayNumber = 2, OrgCompId = null
        };
        db.Add(day2);
        await db.SaveChangesAsync();
        db.Results.Add(Row(day2.Id, swimmer.Id, club.Id, style.Id));
        await db.SaveChangesAsync();

        var overview = await new ResultRepository(db, new NullCache())
            .GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = day2.Id });

        Assert.Equal(16786, overview.OrgCompId);
    }

    [Fact]
    public async Task ManuallyAddedCompetition_NoStamp_OrgCompIdIsNull()
    {
        await using var db = CreateDb(nameof(ManuallyAddedCompetition_NoStamp_OrgCompIdIsNull));

        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var swimmer = new Swimmer { FirstName = "A", LastName = "B", FirstNameEn = "A", LastNameEn = "B", BirthYear = 2000 };
        var comp = new Competition
        {
            Name = "Manual meet", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "01/01/2024", PoolType = "50m", OrgCompId = null
        };
        db.AddRange(style, club, swimmer, comp);
        await db.SaveChangesAsync();
        db.Results.Add(Row(comp.Id, swimmer.Id, club.Id, style.Id));
        await db.SaveChangesAsync();

        var overview = await new ResultRepository(db, new NullCache())
            .GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = comp.Id });

        Assert.Null(overview.OrgCompId);
    }

    // ── Источники стартового протокола (CompetitionSources) ─────────────────────
    // У одного нашего старта источников бывает НЕСКОЛЬКО (окружные протоколы одного
    // чемпионата лежат под разными compID), и подтабы таба Start list строятся по ним.

    [Fact]
    public async Task NoLinks_SourcesSynthesizedFromScalarOrgCompId()
    {
        await using var db = CreateDb(nameof(NoLinks_SourcesSynthesizedFromScalarOrgCompId));

        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var swimmer = new Swimmer { FirstName = "A", LastName = "B", FirstNameEn = "A", LastNameEn = "B", BirthYear = 2000 };
        var comp = new Competition
        {
            Name = "Meet", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "01/01/2024", PoolType = "50m", OrgCompId = 16835
        };
        db.AddRange(style, club, swimmer, comp);
        await db.SaveChangesAsync();
        db.Results.Add(Row(comp.Id, swimmer.Id, club.Id, style.Id));
        await db.SaveChangesAsync();

        var overview = await new ResultRepository(db, new NullCache())
            .GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = comp.Id });

        // Совместимость: соревнования, заведённые до CompetitionSources, обязаны показывать
        // свой протокол — клиент ходит одним путём (по списку), значит список пустым быть не может.
        var single = Assert.Single(overview.StartListSources);
        Assert.Equal(16835, single.OrgCompId);
        Assert.Equal(1, single.Index);
        Assert.Equal("01/01", single.Date);
    }

    [Fact]
    public async Task ManuallyAddedCompetition_NoStamp_SourcesEmpty()
    {
        await using var db = CreateDb(nameof(ManuallyAddedCompetition_NoStamp_SourcesEmpty));

        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var swimmer = new Swimmer { FirstName = "A", LastName = "B", FirstNameEn = "A", LastNameEn = "B", BirthYear = 2000 };
        var comp = new Competition
        {
            Name = "Manual meet", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "01/01/2024", PoolType = "50m", OrgCompId = null
        };
        db.AddRange(style, club, swimmer, comp);
        await db.SaveChangesAsync();
        db.Results.Add(Row(comp.Id, swimmer.Id, club.Id, style.Id));
        await db.SaveChangesAsync();

        var overview = await new ResultRepository(db, new NullCache())
            .GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = comp.Id });

        // Нет ни привязок, ни штампа — таба Start list у соревнования быть не должно.
        Assert.Empty(overview.StartListSources);
    }

    [Fact]
    public async Task MultipleLinks_AllSourcesReturned_OrderedAndNumbered()
    {
        await using var db = CreateDb(nameof(MultipleLinks_AllSourcesReturned_OrderedAndNumbered));

        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var swimmer = new Swimmer { FirstName = "A", LastName = "B", FirstNameEn = "A", LastNameEn = "B", BirthYear = 2000 };
        var evt = new CompetitionEvent { Name = "Champs 8-11" };
        db.AddRange(style, club, swimmer, evt);
        await db.SaveChangesAsync();

        var day1 = new Competition
        {
            Name = "Champs 8-11", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "16/02/2026", PoolType = "25m", EventId = evt.Id, DayNumber = 1
        };
        var day2 = new Competition
        {
            Name = "Champs 8-11", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "19/02/2026", PoolType = "25m", EventId = evt.Id, DayNumber = 2
        };
        db.AddRange(day1, day2);
        await db.SaveChangesAsync();
        db.Results.AddRange(Row(day1.Id, swimmer.Id, club.Id, style.Id), Row(day2.Id, swimmer.Id, club.Id, style.Id));

        // Порядок привязок нарочно обратный дате: сортировать обязан сервер, иначе номера
        // подтабов «поедут» при любом изменении порядка выборки.
        db.Set<CompetitionSource>().AddRange(
            new CompetitionSource
            {
                CompetitionId = day2.Id, OrgCompId = 16786,
                SourceDate = new DateTime(2026, 2, 19), SourceName = "דרום"
            },
            new CompetitionSource
            {
                CompetitionId = day1.Id, OrgCompId = 16787,
                SourceDate = new DateTime(2026, 2, 16), SourceName = "מרכז"
            });
        await db.SaveChangesAsync();

        var overview = await new ResultRepository(db, new NullCache())
            .GetCompetitionOverviewAsync(new ResultFilter { EventId = evt.Id });

        Assert.Equal([16787, 16786], overview.StartListSources.Select(s => s.OrgCompId).ToArray());
        Assert.Equal([1, 2], overview.StartListSources.Select(s => s.Index).ToArray());
        Assert.Equal(["16/02", "19/02"], overview.StartListSources.Select(s => s.Date ?? "").ToArray());
        // Штампа OrgCompId у дней тут нет вовсе — и это ровно тот случай, ради которого
        // заведена таблица: раньше таб Start list у такого соревнования не появлялся.
        Assert.Null(overview.OrgCompId);
    }

    [Fact]
    public async Task Sources_CountEntriesPerOrgCompId()
    {
        await using var db = CreateDb(nameof(Sources_CountEntriesPerOrgCompId));

        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var swimmer = new Swimmer { FirstName = "A", LastName = "B", FirstNameEn = "A", LastNameEn = "B", BirthYear = 2000 };
        var comp = new Competition
        {
            Name = "Meet", Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "01/01/2024", PoolType = "50m"
        };
        db.AddRange(style, club, swimmer, comp);
        await db.SaveChangesAsync();
        db.Results.Add(Row(comp.Id, swimmer.Id, club.Id, style.Id));
        db.Set<CompetitionSource>().Add(new CompetitionSource { CompetitionId = comp.Id, OrgCompId = 16786 });

        // Две заявки этого источника и одна чужая — в счётчик должны попасть только свои.
        db.CompetitionEntries.AddRange(
            Entry(16786, swimmer.Id, club.Id, style.Id, 1),
            Entry(16786, swimmer.Id, club.Id, style.Id, 2),
            Entry(99999, swimmer.Id, club.Id, style.Id, 3));
        await db.SaveChangesAsync();

        var overview = await new ResultRepository(db, new NullCache())
            .GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = comp.Id });

        Assert.Equal(2, Assert.Single(overview.StartListSources).EntryCount);
    }

    private static CompetitionEntry Entry(int orgCompId, int swimmerId, int clubId, int styleId, int lane) => new()
    {
        OrgCompId = orgCompId, OrgDisciplineId = 1, Heat = 1, Lane = lane,
        SwimmerId = swimmerId, ClubId = clubId, StyleId = styleId, Distance = "100",
        Gender = "male", CompDate = new DateTime(2024, 1, 1), Status = CompetitionEntryStatus.Entered
    };
}
