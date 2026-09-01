using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Персональный план на соревнование (docs/plans/start-list-ticket-plan.md, шаг Т3):
/// за кем следит залогиненный пользователь в табе Start list.
/// </summary>
public class StartListPlanRepositoryTests
{
    private const int UserId = 1;
    private const int OrgCompId = 16786;

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private static StartListPlanSaveRequest Save(
        int[]? swimmers = null, int[]? clubs = null, bool imComing = false, bool notifyMe = false) =>
        new(swimmers, clubs, imComing, notifyMe);

    [Fact]
    public async Task Save_ThenGet_RoundTripsComposition()
    {
        await using var db = CreateDb(nameof(Save_ThenGet_RoundTripsComposition));
        var repo = new StartListPlanRepository(db);

        await repo.SaveAsync(UserId, OrgCompId, Save([10, 42], [506], imComing: true));
        var plan = await repo.GetAsync(UserId, OrgCompId);

        Assert.NotNull(plan);
        Assert.Equal([10, 42], plan!.SwimmerIds);
        Assert.Equal([506], plan.ClubIds);
        Assert.True(plan.ImComing);
        Assert.False(plan.NotifyMe);
    }

    /// <summary>
    /// Главное различие всей фичи: «плана нет» и «сохранён пустой план» — РАЗНЫЕ состояния.
    /// В первом витрина подставляет избранных, во втором человек всё снял сам, и возвращать
    /// ему избранных нельзя. Поэтому null против пустых списков, а не «пусто значит нет».
    /// </summary>
    [Fact]
    public async Task Get_NoPlanIsNull_EmptyPlanIsNotNull()
    {
        await using var db = CreateDb(nameof(Get_NoPlanIsNull_EmptyPlanIsNotNull));
        var repo = new StartListPlanRepository(db);

        Assert.Null(await repo.GetAsync(UserId, OrgCompId));

        await repo.SaveAsync(UserId, OrgCompId, Save([], []));
        var plan = await repo.GetAsync(UserId, OrgCompId);

        Assert.NotNull(plan);
        Assert.Empty(plan!.SwimmerIds);
        Assert.Empty(plan.ClubIds);
    }

    /// <summary>Состав пишется ЦЕЛИКОМ: второе сохранение заменяет первое, а не дополняет.</summary>
    [Fact]
    public async Task Save_Twice_ReplacesCompositionAndKeepsOneRow()
    {
        await using var db = CreateDb(nameof(Save_Twice_ReplacesCompositionAndKeepsOneRow));
        var repo = new StartListPlanRepository(db);

        await repo.SaveAsync(UserId, OrgCompId, Save([10, 42]));
        var plan = await repo.SaveAsync(UserId, OrgCompId, Save([77], notifyMe: true));

        Assert.Equal([77], plan.SwimmerIds);
        Assert.True(plan.NotifyMe);
        Assert.False(plan.ImComing);
        Assert.Equal(1, await db.UserStartListPlans.CountAsync());
    }

    [Fact]
    public async Task Save_DropsDuplicatesAndGarbage()
    {
        await using var db = CreateDb(nameof(Save_DropsDuplicatesAndGarbage));

        var plan = await new StartListPlanRepository(db)
            .SaveAsync(UserId, OrgCompId, Save([10, 10, 0, -5, 42]));

        Assert.Equal([10, 42], plan.SwimmerIds);
    }

    /// <summary>План принадлежит паре «пользователь + соревнование»: чужой не виден.</summary>
    [Fact]
    public async Task Get_IsScopedToUserAndCompetition()
    {
        await using var db = CreateDb(nameof(Get_IsScopedToUserAndCompetition));
        var repo = new StartListPlanRepository(db);

        await repo.SaveAsync(UserId, OrgCompId, Save([10]));

        Assert.Null(await repo.GetAsync(UserId + 1, OrgCompId));
        Assert.Null(await repo.GetAsync(UserId, OrgCompId + 1));
    }

    [Fact]
    public async Task GetAll_ReturnsFreshestFirst()
    {
        await using var db = CreateDb(nameof(GetAll_ReturnsFreshestFirst));
        var repo = new StartListPlanRepository(db);

        await repo.SaveAsync(UserId, OrgCompId, Save([10]));
        await repo.SaveAsync(UserId, OrgCompId + 1, Save([42]));
        // Первому плану состарим отметку — иначе оба сохранены в одну миллисекунду.
        db.UserStartListPlans.Single(p => p.OrgCompId == OrgCompId).UpdatedAt =
            DateTime.UtcNow.AddHours(-1);
        await db.SaveChangesAsync();

        var all = await repo.GetAllAsync(UserId);

        Assert.Equal([OrgCompId + 1, OrgCompId], all.Select(p => p.OrgCompId));
    }

    [Fact]
    public async Task Delete_RemovesPlan_AndSaysWhenThereWasNone()
    {
        await using var db = CreateDb(nameof(Delete_RemovesPlan_AndSaysWhenThereWasNone));
        var repo = new StartListPlanRepository(db);

        await repo.SaveAsync(UserId, OrgCompId, Save([10]));

        Assert.True(await repo.DeleteAsync(UserId, OrgCompId));
        Assert.Null(await repo.GetAsync(UserId, OrgCompId));
        Assert.False(await repo.DeleteAsync(UserId, OrgCompId));
    }
}
