using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>Ручная правка результата (ResultAdminRepository): парс времени, валидации, эстафеты.</summary>
public class ResultAdminRepositoryTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
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

    private static ResultAdminRepository Repo(SwimmDbContext db) => new(db, new NullCache());

    /// <summary>Шпион пересчёта: сам расчёт использует ExecuteUpdate и на InMemory не работает,
    /// поэтому проверяем факт вызова.</summary>
    private sealed class RecalcSpy : ICompetitionRecalculationService
    {
        public List<int> Calls { get; } = [];
        public bool Throw { get; init; }

        public Task<int> RecalculateCompetitionAsync(int competitionId, CancellationToken ct = default)
        {
            Calls.Add(competitionId);
            if (Throw) throw new InvalidOperationException("boom");
            return Task.FromResult(2);
        }

        public Task<int> RecalculateAllCombinedAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    /// <summary>Кладёт один индивидуальный результат, возвращает его Id + ключевые сущности.</summary>
    private static async Task<(long resultId, int swimmerId, int clubId)> SeedResult(
        SwimmDbContext db, int? timeMs = 62340)
    {
        var swimmer = new Swimmer { LastName = "Cohen", FirstName = "Dan" };
        var swimmer2 = new Swimmer { LastName = "Levi", FirstName = "Noa" };
        var comp = new Competition { Name = "Meet A", Date = "01/06/2026", PoolType = "25m" };
        var club = new Club { Name = "Club A" };
        var club2 = new Club { Name = "Club B" };
        var style = new Style { Name = "freestyle" };
        db.AddRange(swimmer, swimmer2, comp, club, club2, style);
        var r = new ResultRecord
        {
            Swimmer = swimmer, Competition = comp, Club = club, Style = style,
            Distance = "100", Gender = "male", CompetitionDate = new DateTime(2026, 6, 1),
            Position = 3, Heat = 1, Lane = 4, TimeMillisecond = timeMs, TimeOriginal = "1:02.34"
        };
        db.Results.Add(r);
        await db.SaveChangesAsync();
        return (r.Id, swimmer2.Id, club2.Id);
    }

    private static ResultEditInputDto BaseInput(ResultEditDto d) => new()
    {
        SwimmerId = d.SwimmerId, ClubId = d.ClubId, Distance = d.Distance, Gender = d.Gender,
        AgeGroup = d.AgeGroup, EventStyleAge = d.EventStyleAge, Position = d.Position,
        PositionAgeGroup = d.PositionAgeGroup, Heat = d.Heat, Lane = d.Lane,
        TimeText = d.TimeText, TimeFail = d.TimeFail, TimeFailNote = d.TimeFailNote,
        InternationalPoints = d.InternationalPoints, Note = d.Note
    };

    [Fact]
    public async Task GetById_FormatsTime_AndContext()
    {
        await using var db = CreateDb(nameof(GetById_FormatsTime_AndContext));
        var (id, _, _) = await SeedResult(db);

        var d = await Repo(db).GetByIdAsync(id);
        Assert.NotNull(d);
        Assert.Equal("1:02.34", d!.TimeText);
        Assert.Equal("Meet A", d.CompetitionName);
        Assert.Equal("Cohen Dan", d.SwimmerName);
    }

    [Fact]
    public async Task Update_ChangesTimeAndPosition()
    {
        await using var db = CreateDb(nameof(Update_ChangesTimeAndPosition));
        var (id, _, _) = await SeedResult(db);
        var input = BaseInput((await Repo(db).GetByIdAsync(id))!);
        input.TimeText = "59.10";
        input.Position = 1;

        var res = await Repo(db).UpdateAsync(id, input);
        Assert.True(res.Success);

        var row = await db.Results.SingleAsync();
        Assert.Equal(59100, row.TimeMillisecond);
        Assert.Equal("59.10", row.TimeOriginal);
        Assert.Equal(1, row.Position);
    }

    [Fact]
    public async Task Update_InvalidTime_FailsWithoutChange()
    {
        await using var db = CreateDb(nameof(Update_InvalidTime_FailsWithoutChange));
        var (id, _, _) = await SeedResult(db);
        var input = BaseInput((await Repo(db).GetByIdAsync(id))!);
        input.TimeText = "не время";

        var res = await Repo(db).UpdateAsync(id, input);
        Assert.False(res.Success);
        Assert.Equal(62340, (await db.Results.SingleAsync()).TimeMillisecond);   // не изменилось
    }

    [Fact]
    public async Task Update_EmptyTime_ClearsMs()
    {
        await using var db = CreateDb(nameof(Update_EmptyTime_ClearsMs));
        var (id, _, _) = await SeedResult(db);
        var input = BaseInput((await Repo(db).GetByIdAsync(id))!);
        input.TimeText = "";
        input.TimeFail = true;
        input.TimeFailNote = "DQ";

        var res = await Repo(db).UpdateAsync(id, input);
        Assert.True(res.Success);
        var row = await db.Results.SingleAsync();
        Assert.Null(row.TimeMillisecond);
        Assert.Equal("", row.TimeOriginal);
        Assert.True(row.TimeFail);
    }

    [Fact]
    public async Task Update_ReassignsSwimmerAndClub()
    {
        await using var db = CreateDb(nameof(Update_ReassignsSwimmerAndClub));
        var (id, otherSwimmer, otherClub) = await SeedResult(db);
        var input = BaseInput((await Repo(db).GetByIdAsync(id))!);
        input.SwimmerId = otherSwimmer;
        input.ClubId = otherClub;

        var res = await Repo(db).UpdateAsync(id, input);
        Assert.True(res.Success);
        var row = await db.Results.SingleAsync();
        Assert.Equal(otherSwimmer, row.SwimmerId);
        Assert.Equal(otherClub, row.ClubId);
    }

    [Fact]
    public async Task Update_UnknownSwimmer_Fails()
    {
        await using var db = CreateDb(nameof(Update_UnknownSwimmer_Fails));
        var (id, _, _) = await SeedResult(db);
        var input = BaseInput((await Repo(db).GetByIdAsync(id))!);
        input.SwimmerId = 999999;

        var res = await Repo(db).UpdateAsync(id, input);
        Assert.False(res.Success);
    }

    [Fact]
    public async Task Update_NegativeLane_Fails()
    {
        await using var db = CreateDb(nameof(Update_NegativeLane_Fails));
        var (id, _, _) = await SeedResult(db);
        var input = BaseInput((await Repo(db).GetByIdAsync(id))!);
        input.Lane = -1;

        var res = await Repo(db).UpdateAsync(id, input);
        Assert.False(res.Success);
    }

    [Fact]
    public async Task RelayRow_NotEditable()
    {
        await using var db = CreateDb(nameof(RelayRow_NotEditable));
        var swimmer = new Swimmer { LastName = "A", FirstName = "X" };
        var comp = new Competition { Name = "Meet", Date = "01/06/2026", PoolType = "25m" };
        var club = new Club { Name = "Club" };
        var style = new Style { Name = "free_relay" };
        var relay = new Relay { TeamName = "Team" };
        db.AddRange(swimmer, comp, club, style, relay);
        var r = new ResultRecord
        {
            Swimmer = swimmer, Competition = comp, Club = club, Style = style, Relay = relay,
            Distance = "4x100", Gender = "male", CompetitionDate = new DateTime(2026, 6, 1)
        };
        db.Results.Add(r);
        await db.SaveChangesAsync();

        Assert.Null(await Repo(db).GetByIdAsync(r.Id));
        var res = await Repo(db).UpdateAsync(r.Id, new ResultEditInputDto
        {
            SwimmerId = swimmer.Id, ClubId = club.Id, Distance = "4x100", Gender = "male"
        });
        Assert.False(res.Success);
    }

    [Fact]
    public async Task Update_TriggersCombinedRecalculation()
    {
        // Объединённое место — производная от времени: исправили опечатку в протоколе,
        // а порядок в общем зачёте остался бы от старого значения.
        await using var db = CreateDb(nameof(Update_TriggersCombinedRecalculation));
        var (id, _, _) = await SeedResult(db);
        var spy = new RecalcSpy();
        var repo = new ResultAdminRepository(db, new NullCache(), spy);

        var dto = (await repo.GetByIdAsync(id))!;
        var input = BaseInput(dto);
        input.TimeText = "01:01.00";
        var res = await repo.UpdateAsync(id, input);

        Assert.True(res.Success);
        var compId = (await db.Results.AsNoTracking().FirstAsync(r => r.Id == id)).CompetitionId;
        Assert.Equal([compId], spy.Calls);
    }

    [Fact]
    public async Task Update_SurvivesRecalculationFailure()
    {
        await using var db = CreateDb(nameof(Update_SurvivesRecalculationFailure));
        var (id, _, _) = await SeedResult(db);
        var repo = new ResultAdminRepository(db, new NullCache(), new RecalcSpy { Throw = true });

        var dto = (await repo.GetByIdAsync(id))!;
        var input = BaseInput(dto);
        input.TimeText = "01:02.00";
        var res = await repo.UpdateAsync(id, input);

        Assert.True(res.Success);
        Assert.Equal(62000, (await db.Results.AsNoTracking().FirstAsync(r => r.Id == id)).TimeMillisecond);
    }
}
