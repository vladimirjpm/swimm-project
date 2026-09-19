using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Качать ли промежуточные для строки discovery (<see cref="CompetitionAdminRepository.ShouldFetchSplitsAsync"/>).
///
/// Бэкфилл 19.09.2026 прошёл discovery 201/202 без доклейки: у возрастного чемпионата
/// «אליפות חורף ארנה גילאי 11-10 מחוז צפון» в имени нет «ישראל», а правило смотрело только
/// на имя. Вторая улика — галка IsChampionship соревнования, уже лежащего в базе.
/// </summary>
public class SplitsFetchRuleTests
{
    private const string AgeChampName = "אליפות חורף ארנה גילאי 11-10 מחוז צפון";

    private static SwimmDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task ChampionshipByName_NoDbNeeded()
    {
        await using var db = CreateDb();
        Assert.True(await CompetitionAdminRepository.ShouldFetchSplitsAsync(
            db, "אליפות ישראל \"ארנה\" קיץ 2026", 123));
    }

    [Fact]
    public async Task NameWithoutIsrael_FlaggedCompetitionByOrgCompId_Fetches()
    {
        await using var db = CreateDb();
        db.Competitions.Add(new Competition { Id = 1, Name = AgeChampName, OrgCompId = 6599, IsChampionship = true });
        await db.SaveChangesAsync();

        Assert.True(await CompetitionAdminRepository.ShouldFetchSplitsAsync(db, AgeChampName, 6599));
    }

    [Fact]
    public async Task NameWithoutIsrael_FlagOnlyThroughCompetitionSources_Fetches()
    {
        await using var db = CreateDb();
        db.Competitions.Add(new Competition { Id = 1, Name = "склейка", IsChampionship = true });
        db.CompetitionSources.Add(new CompetitionSource { Id = 1, CompetitionId = 1, OrgCompId = 16786 });
        await db.SaveChangesAsync();

        Assert.True(await CompetitionAdminRepository.ShouldFetchSplitsAsync(db, AgeChampName, 16786));
    }

    [Fact]
    public async Task NameWithoutIsrael_CompetitionNotFlagged_Skips()
    {
        await using var db = CreateDb();
        db.Competitions.Add(new Competition { Id = 1, Name = AgeChampName, OrgCompId = 6599, IsChampionship = false });
        await db.SaveChangesAsync();

        Assert.False(await CompetitionAdminRepository.ShouldFetchSplitsAsync(db, AgeChampName, 6599));
        Assert.False(await CompetitionAdminRepository.ShouldFetchSplitsAsync(db, AgeChampName, null));
    }
}
