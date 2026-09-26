using Microsoft.EntityFrameworkCore;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Уровни пловцов группы (docs/plans/lane-plans-plan.md, L1): правила списка
/// (<see cref="HubGroupLevelRules"/>) и сервис (<see cref="HubGroupLevelService"/>) — стандартный
/// набор при первом открытии, сохранение списка целиком, уровень пловца и то, что он переживает
/// клубную пересборку состава (ключ по SwimmerId, не по строке HubGroupMembers).
/// </summary>
public class HubGroupLevelTests
{
    // ── Правила ─────────────────────────────────────────────────────────────

    private static HubGroupLevelInputDto In(string? name, int? id = null, string? description = null, string? color = null) =>
        new() { Id = id, Name = name, Description = description, Color = color };

    [Fact]
    public void Normalize_RanksFollowListOrder_AndCleansInput()
    {
        var (levels, error) = HubGroupLevelRules.Normalize(
        [
            In("  Fast   lane ", id: 7, description: "  ", color: "#1E88E5"),
            In("Beginner", description: " New "),
        ]);

        Assert.Null(error);
        Assert.Collection(levels!,
            l => Assert.Equal(new HubGroupLevelRules.NormalizedLevel(7, 1, "Fast lane", null, "#1e88e5"), l),
            l => Assert.Equal(new HubGroupLevelRules.NormalizedLevel(null, 2, "Beginner", "New", null), l));
    }

    public static TheoryData<List<HubGroupLevelInputDto>> InvalidLists => new()
    {
        new List<HubGroupLevelInputDto>(),                                           // хотя бы один уровень
        Enumerable.Range(1, HubGroupLevelRules.MaxLevels + 1).Select(i => In($"L{i}")).ToList(),
        new List<HubGroupLevelInputDto> { In("   ") },                               // без имени
        new List<HubGroupLevelInputDto> { In(new string('x', HubGroupLevelRules.NameMaxLength + 1)) },
        new List<HubGroupLevelInputDto> { In("Fast"), In("fast") },                  // дубль без учёта регистра
        new List<HubGroupLevelInputDto> { In("A", id: 1), In("B", id: 1) },          // один уровень дважды
        new List<HubGroupLevelInputDto> { In("A", color: "blue") },
        new List<HubGroupLevelInputDto> { In("A", description: new string('x', HubGroupLevelRules.DescriptionMaxLength + 1)) },
    };

    [Theory]
    [MemberData(nameof(InvalidLists))]
    public void Normalize_RejectsInvalidList(List<HubGroupLevelInputDto> input)
    {
        var (levels, error) = HubGroupLevelRules.Normalize(input);

        Assert.Null(levels);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    // ── Сервис ──────────────────────────────────────────────────────────────

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private sealed record Seed(int GroupId, int OtherGroupId, int Ann, int Ben, int Hidden, int Outsider);

    /// <summary>
    /// Группа: Ann (ручной), Ben (клубный), Hidden (клубный скрытый); Outsider — пловец не из
    /// группы. Вторая группа — для «чужого уровня».
    /// </summary>
    private static async Task<Seed> SeedAsync(SwimmDbContext db)
    {
        var owner = new AppUser { Email = "owner@example.com", DisplayName = "Owner", SecurityStamp = "s" };
        db.AppUsers.Add(owner);
        await db.SaveChangesAsync();

        var group = new HubGroup { Name = "G", Slug = "g", OwnerUserId = owner.Id, IsPublic = true };
        var other = new HubGroup { Name = "O", Slug = "o", OwnerUserId = owner.Id, IsPublic = true };
        var ann = new Swimmer { LastName = "כהן", FirstName = "ענת", LastNameEn = "Cohen", FirstNameEn = "Anat", BirthYear = 1980 };
        var ben = new Swimmer { LastName = "לוי", FirstName = "בן", BirthYear = 1975 };
        var hidden = new Swimmer { LastName = "מוסתר", FirstName = "א", BirthYear = 1990 };
        var outsider = new Swimmer { LastName = "זר", FirstName = "ב", BirthYear = 1990 };
        db.AddRange(group, other, ann, ben, hidden, outsider);
        await db.SaveChangesAsync();

        db.HubGroupMembers.AddRange(
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = ann.Id, SortOrder = 1 },
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = ben.Id, SortOrder = 2, Source = HubGroupMemberSource.Club },
            new HubGroupMember
            {
                HubGroupId = group.Id, SwimmerId = hidden.Id, SortOrder = 3,
                Source = HubGroupMemberSource.Club, IsExcluded = true,
            });
        await db.SaveChangesAsync();

        return new Seed(group.Id, other.Id, ann.Id, ben.Id, hidden.Id, outsider.Id);
    }

    [Fact]
    public async Task Get_SeedsStandardLevelsOnce()
    {
        await using var db = CreateDb(nameof(Get_SeedsStandardLevelsOnce));
        var s = await SeedAsync(db);
        var service = new HubGroupLevelService(db);

        var first = await service.GetAsync(s.GroupId);
        var second = await service.GetAsync(s.GroupId);

        Assert.Equal(HubGroupLevelRules.Defaults.Select(d => d.Name), first!.Levels.Select(l => l.Name));
        Assert.Equal([1, 2, 3, 4], first.Levels.Select(l => l.Rank));
        Assert.Equal(first.Levels.Select(l => l.Id), second!.Levels.Select(l => l.Id));
        Assert.Equal(4, await db.HubGroupLevels.CountAsync(l => l.HubGroupId == s.GroupId));
        // Соседняя группа не тронута: набор заводится только открытой.
        Assert.False(await db.HubGroupLevels.AnyAsync(l => l.HubGroupId == s.OtherGroupId));
    }

    [Fact]
    public async Task Get_MissingGroup_ReturnsNull()
    {
        await using var db = CreateDb(nameof(Get_MissingGroup_ReturnsNull));

        Assert.Null(await new HubGroupLevelService(db).GetAsync(404));
    }

    [Fact]
    public async Task Get_ListsVisibleRosterOnly_AndCountsIt()
    {
        await using var db = CreateDb(nameof(Get_ListsVisibleRosterOnly_AndCountsIt));
        var s = await SeedAsync(db);
        var service = new HubGroupLevelService(db);
        var advanced = (await service.GetAsync(s.GroupId))!.Levels[0].Id;
        await service.SetSwimmerLevelAsync(s.GroupId, s.Ann, advanced);
        // Уровень скрытого остался в таблице с прошлого раза — в счёт он не идёт.
        db.HubGroupSwimmerLevels.Add(new HubGroupSwimmerLevel { HubGroupId = s.GroupId, SwimmerId = s.Hidden, LevelId = advanced });
        await db.SaveChangesAsync();

        var dto = (await service.GetAsync(s.GroupId))!;

        Assert.Equal([s.Ann, s.Ben], dto.Swimmers.Select(w => w.SwimmerId));
        Assert.Equal("כהן ענת", dto.Swimmers[0].Name);
        Assert.Equal("Cohen Anat", dto.Swimmers[0].NameEn);
        Assert.Equal(advanced, dto.Swimmers[0].LevelId);
        Assert.Null(dto.Swimmers[1].LevelId);
        Assert.Equal(1, dto.Levels[0].SwimmerCount);
    }

    [Fact]
    public async Task SetSwimmerLevel_SetsChangesAndClears()
    {
        await using var db = CreateDb(nameof(SetSwimmerLevel_SetsChangesAndClears));
        var s = await SeedAsync(db);
        var service = new HubGroupLevelService(db);
        var levels = (await service.GetAsync(s.GroupId))!.Levels;

        Assert.True((await service.SetSwimmerLevelAsync(s.GroupId, s.Ben, levels[1].Id)).Success);
        Assert.True((await service.SetSwimmerLevelAsync(s.GroupId, s.Ben, levels[3].Id)).Success);
        Assert.Equal(levels[3].Id, (await db.HubGroupSwimmerLevels.SingleAsync()).LevelId);

        Assert.True((await service.SetSwimmerLevelAsync(s.GroupId, s.Ben, null)).Success);
        Assert.False(await db.HubGroupSwimmerLevels.AnyAsync());
    }

    [Fact]
    public async Task SetSwimmerLevel_RejectsOutsiderHiddenAndForeignLevel()
    {
        await using var db = CreateDb(nameof(SetSwimmerLevel_RejectsOutsiderHiddenAndForeignLevel));
        var s = await SeedAsync(db);
        var service = new HubGroupLevelService(db);
        var own = (await service.GetAsync(s.GroupId))!.Levels[0].Id;
        var foreign = (await service.GetAsync(s.OtherGroupId))!.Levels[0].Id;

        Assert.False((await service.SetSwimmerLevelAsync(s.GroupId, s.Outsider, own)).Success);
        Assert.False((await service.SetSwimmerLevelAsync(s.GroupId, s.Hidden, own)).Success);
        Assert.False((await service.SetSwimmerLevelAsync(s.GroupId, s.Ann, foreign)).Success);
        Assert.False(await db.HubGroupSwimmerLevels.AnyAsync());
    }

    [Fact]
    public async Task Level_SurvivesClubRosterRebuild()
    {
        await using var db = CreateDb(nameof(Level_SurvivesClubRosterRebuild));
        var s = await SeedAsync(db);
        var service = new HubGroupLevelService(db);
        var level = (await service.GetAsync(s.GroupId))!.Levels[2].Id;
        await service.SetSwimmerLevelAsync(s.GroupId, s.Ben, level);

        // Пересборка подписки: клубная строка уходит и заводится заново с новым Id.
        db.HubGroupMembers.Remove(await db.HubGroupMembers.SingleAsync(m => m.SwimmerId == s.Ben));
        await db.SaveChangesAsync();
        db.HubGroupMembers.Add(new HubGroupMember
        {
            HubGroupId = s.GroupId, SwimmerId = s.Ben, SortOrder = 2, Source = HubGroupMemberSource.Club,
        });
        await db.SaveChangesAsync();

        var dto = (await service.GetAsync(s.GroupId))!;
        Assert.Equal(level, dto.Swimmers.Single(w => w.SwimmerId == s.Ben).LevelId);
    }

    [Fact]
    public async Task SaveLevels_ReordersRenamesAddsAndRemoves()
    {
        await using var db = CreateDb(nameof(SaveLevels_ReordersRenamesAddsAndRemoves));
        var s = await SeedAsync(db);
        var service = new HubGroupLevelService(db);
        var levels = (await service.GetAsync(s.GroupId))!.Levels;
        await service.SetSwimmerLevelAsync(s.GroupId, s.Ann, levels[0].Id);  // остаётся
        await service.SetSwimmerLevelAsync(s.GroupId, s.Ben, levels[3].Id);  // уровень удалят

        // Второй и первый меняются местами, третий переименован, четвёртый удалён, пятый новый.
        var result = await service.SaveLevelsAsync(s.GroupId, new HubGroupLevelsInputDto
        {
            Levels =
            [
                In("Intermediate", id: levels[1].Id),
                In("Advanced", id: levels[0].Id),
                In("Improvers", id: levels[2].Id),
                In("Kids", color: "#43A047"),
            ],
        });

        Assert.True(result.Success, result.Error);
        var dto = (await service.GetAsync(s.GroupId))!;
        Assert.Equal(["Intermediate", "Advanced", "Improvers", "Kids"], dto.Levels.Select(l => l.Name));
        Assert.Equal([1, 2, 3, 4], dto.Levels.Select(l => l.Rank));
        Assert.Equal(levels[1].Id, dto.Levels[0].Id);
        Assert.Equal("#43a047", dto.Levels[3].Color);
        Assert.Equal(levels[0].Id, dto.Swimmers.Single(w => w.SwimmerId == s.Ann).LevelId);
        Assert.Null(dto.Swimmers.Single(w => w.SwimmerId == s.Ben).LevelId);
    }

    [Fact]
    public async Task SaveLevels_UnknownOrForeignId_FailsWithoutChanges()
    {
        await using var db = CreateDb(nameof(SaveLevels_UnknownOrForeignId_FailsWithoutChanges));
        var s = await SeedAsync(db);
        var service = new HubGroupLevelService(db);
        var own = (await service.GetAsync(s.GroupId))!.Levels;
        var foreign = (await service.GetAsync(s.OtherGroupId))!.Levels[0].Id;

        var result = await service.SaveLevelsAsync(s.GroupId, new HubGroupLevelsInputDto
        {
            Levels = [In("Mine", id: own[0].Id), In("Theirs", id: foreign)],
        });

        Assert.False(result.Success);
        Assert.Equal(own.Select(l => l.Name), (await service.GetAsync(s.GroupId))!.Levels.Select(l => l.Name));
        Assert.Equal("Advanced", (await db.HubGroupLevels.SingleAsync(l => l.Id == foreign)).Name);
    }
}
