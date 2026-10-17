using Microsoft.EntityFrameworkCore;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сидер тестовых персонажей (<see cref="PersonaSeeder"/>, docs/plans/test-personas-plan.md):
/// кто в каком состоянии, идемпотентность и --reset. Главный риск — сидер, который при
/// повторе задваивает строки или трогает не-тестовые данные.
/// </summary>
public class PersonaSeederTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    /// <summary>Роли, стиль и 8 детей с заплывами — минимум, из которого сидер собирает состав.</summary>
    private static async Task SeedBaseAsync(SwimmDbContext db)
    {
        db.AppRoles.AddRange(new AppRole { Name = "Admin" }, new AppRole { Name = "User" }, new AppRole { Name = "Coach" });
        db.Styles.Add(new Style { Name = "freestyle" });
        for (var i = 0; i < 8; i++)
        {
            var swimmer = new Swimmer { FirstName = $"Kid{i}", LastName = "Test", BirthYear = 2013, Gender = i % 2 == 0 ? "male" : "female" };
            db.Swimmers.Add(swimmer);
            await db.SaveChangesAsync();
            // Чем меньше i, тем больше заплывов — порядок выбора состава предсказуем.
            for (var n = 0; n < 10 - i; n++)
                db.Results.Add(new ResultRecord { SwimmerId = swimmer.Id, CompetitionDate = DateTime.UtcNow.AddDays(-10) });
        }
        // Живой пользователь — сидер и --reset не должны его тронуть.
        db.AppUsers.Add(new AppUser { Email = "real.person@example.com", DisplayName = "Real", SecurityStamp = "s" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Seed_CreatesPersonasInTheirStates()
    {
        await using var db = CreateDb(nameof(Seed_CreatesPersonasInTheirStates));
        await SeedBaseAsync(db);

        await new PersonaSeeder(db).SeedAsync();

        var users = await db.AppUsers.Where(u => u.Email.EndsWith("@" + TestPersonas.EmailDomain))
            .ToDictionaryAsync(u => u.DisplayName);
        Assert.Equal(TestPersonas.All.Count, users.Count);
        Assert.All(users.Values, u => Assert.True(TestAccountRules.IsTestEmail(u.Email)));
        Assert.False(users[TestPersonas.Blocked].IsActive);
        Assert.Equal(2, users[TestPersonas.Coach].HubGroupLimit);

        var groups = await db.HubGroups.ToListAsync();
        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.True(g.IsTest));
        Assert.All(groups, g => Assert.Equal(users[TestPersonas.Coach].Id, g.OwnerUserId));
        var approval = groups.Single(g => g.Slug == TestPersonas.ApprovalGroupSlug);
        Assert.Equal(HubGroupJoinPolicy.Approval, approval.JoinPolicy);

        var pending = await db.HubGroupUserMembers.SingleAsync(m => m.UserId == users[TestPersonas.Pending].Id);
        Assert.Equal(approval.Id, pending.HubGroupId);
        Assert.Equal(HubGroupUserMemberStatus.Pending, pending.Status);
        Assert.Equal(2, await db.HubGroupUserMembers.CountAsync(m => m.UserId == users[TestPersonas.Member].Id
                                                                    && m.Status == HubGroupUserMemberStatus.Active));
        Assert.False(await db.HubGroupUserMembers.AnyAsync(m => m.UserId == users[TestPersonas.Parent].Id));
        Assert.True(await db.HubGroupAdmins.AnyAsync(a => a.UserId == users[TestPersonas.GroupAdmin].Id));

        Assert.Equal(2, await db.UserFavorites.CountAsync(f => f.UserId == users[TestPersonas.Parent].Id && f.IsFamily));
        Assert.True((await db.UserFavorites.SingleAsync(f => f.UserId == users[TestPersonas.SwimmerMe].Id)).IsPrimary);

        Assert.Equal(4, await db.UserMedia.CountAsync(m => m.UserId == users[TestPersonas.MediaAuthor].Id));
        Assert.Equal(2, await db.UserMediaPublications.CountAsync());
        Assert.Equal(6 * 4, await db.TrainingResults.CountAsync());

        // Состав — самые активные дети; у первого больше всего заплывов.
        var roster = await db.HubGroupMembers.Where(m => m.HubGroupId == approval.Id).OrderBy(m => m.SortOrder).ToListAsync();
        Assert.Equal(6, roster.Count);
        Assert.Equal("Kid0", (await db.Swimmers.FindAsync(roster[0].SwimmerId))!.FirstName);
    }

    [Fact]
    public async Task Seed_Twice_DuplicatesNothing()
    {
        await using var db = CreateDb(nameof(Seed_Twice_DuplicatesNothing));
        await SeedBaseAsync(db);
        var seeder = new PersonaSeeder(db);

        await seeder.SeedAsync();
        var counts = await CountsAsync(db);
        await seeder.SeedAsync();

        Assert.Equal(counts, await CountsAsync(db));
    }

    [Fact]
    public async Task Seed_RestoresPersonaState_WhenChangedByHand()
    {
        await using var db = CreateDb(nameof(Seed_RestoresPersonaState_WhenChangedByHand));
        await SeedBaseAsync(db);
        var seeder = new PersonaSeeder(db);
        await seeder.SeedAsync();

        // Кто-то одобрил заявку и разблокировал blocked, проверяя руками.
        var pending = await db.HubGroupUserMembers.SingleAsync(m => m.Status == HubGroupUserMemberStatus.Pending);
        pending.Status = HubGroupUserMemberStatus.Active;
        (await db.AppUsers.SingleAsync(u => u.DisplayName == TestPersonas.Blocked)).IsActive = true;
        await db.SaveChangesAsync();

        await seeder.SeedAsync();

        Assert.Equal(HubGroupUserMemberStatus.Pending, (await db.HubGroupUserMembers.FindAsync(pending.Id))!.Status);
        Assert.False((await db.AppUsers.SingleAsync(u => u.DisplayName == TestPersonas.Blocked)).IsActive);
    }

    [Fact]
    public async Task Seed_RefusesToTouchNonTestGroupWithSameSlug()
    {
        await using var db = CreateDb(nameof(Seed_RefusesToTouchNonTestGroupWithSameSlug));
        await SeedBaseAsync(db);
        var owner = await db.AppUsers.SingleAsync();
        db.HubGroups.Add(new HubGroup { Name = "Real", Slug = TestPersonas.OpenGroupSlug, OwnerUserId = owner.Id });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new PersonaSeeder(db).SeedAsync());
        Assert.False((await db.HubGroups.SingleAsync()).IsTest);
    }

    [Fact]
    public async Task Reset_RemovesOnlyTestData_AndSeedsAgain()
    {
        await using var db = CreateDb(nameof(Reset_RemovesOnlyTestData_AndSeedsAgain));
        await SeedBaseAsync(db);
        var seeder = new PersonaSeeder(db);
        await seeder.SeedAsync();
        var firstCoachId = (await db.AppUsers.SingleAsync(u => u.DisplayName == TestPersonas.Coach)).Id;

        var log = await seeder.SeedAsync(reset: true);

        Assert.Contains(log, l => l.StartsWith("reset: удалено тест-групп 2"));
        Assert.NotEqual(firstCoachId, (await db.AppUsers.SingleAsync(u => u.DisplayName == TestPersonas.Coach)).Id);
        Assert.True(await db.AppUsers.AnyAsync(u => u.Email == "real.person@example.com"));
        Assert.Equal(TestPersonas.All.Count + 1, await db.AppUsers.CountAsync());
        Assert.Equal(2, await db.HubGroups.CountAsync());
    }

    // ── Сброс одного персонажа (кнопка «Сбросить» в /Admin/Users, 17.10.2026) ──────────

    [Fact]
    public async Task ResetPersona_ClearsManualChanges_KeepsId()
    {
        await using var db = CreateDb(nameof(ResetPersona_ClearsManualChanges_KeepsId));
        await SeedBaseAsync(db);
        var seeder = new PersonaSeeder(db);
        await seeder.SeedAsync();

        var newbie = await db.AppUsers.SingleAsync(u => u.DisplayName == TestPersonas.Newbie);
        var id = newbie.Id;
        var swimmerId = await db.Swimmers.Select(s => s.Id).FirstAsync();
        var adminRoleId = await db.AppRoles.Where(r => r.Name == "Admin").Select(r => r.Id).SingleAsync();
        // Руками «пощупали»: «это я», клуб-опрос-отказ, своя группа, лишняя роль, новое имя.
        db.UserFavorites.Add(new UserFavorite { UserId = id, TargetType = "swimmer", SwimmerId = swimmerId, IsPrimary = true });
        db.HubGroups.Add(new HubGroup { Name = "Mine", Slug = "utest-newbie-own", OwnerUserId = id });
        db.AppUserRoles.Add(new AppUserRole { UserId = id, RoleId = adminRoleId });
        newbie.DisplayName = "Renamed";
        newbie.ProfileRoleWatcher = true;
        newbie.ProfileRolesAnsweredAt = DateTime.UtcNow;
        newbie.ProfileSkipClub = true;
        await db.SaveChangesAsync();
        var parentFavsBefore = await db.UserFavorites.CountAsync(f => f.User.DisplayName == TestPersonas.Parent);

        var log = await seeder.ResetPersonaAsync(id);

        Assert.NotNull(log);
        db.ChangeTracker.Clear();
        var after = await db.AppUsers.SingleAsync(u => u.Id == id);
        Assert.Equal(TestPersonas.Newbie, after.DisplayName);
        Assert.False(after.ProfileRoleWatcher);
        Assert.Null(after.ProfileRolesAnsweredAt);
        Assert.False(after.ProfileSkipClub);
        Assert.Equal(0, await db.UserFavorites.CountAsync(f => f.UserId == id));
        Assert.False(await db.HubGroups.AnyAsync(g => g.Slug == "utest-newbie-own"));
        var roles = await db.AppUserRoles.Where(r => r.UserId == id).Select(r => r.Role.Name).ToListAsync();
        Assert.Equal(["User"], roles);
        // Соседей сброс не трогает: тест-группы и «дети» родителя на месте.
        Assert.Equal(2, await db.HubGroups.CountAsync());
        Assert.Equal(parentFavsBefore, await db.UserFavorites.CountAsync(f => f.User.DisplayName == TestPersonas.Parent));
    }

    [Fact]
    public async Task ResetPersona_RestoresWhatWasRemoved()
    {
        await using var db = CreateDb(nameof(ResetPersona_RestoresWhatWasRemoved));
        await SeedBaseAsync(db);
        var seeder = new PersonaSeeder(db);
        await seeder.SeedAsync();

        // Родитель снял «детей», участник вышел из групп — сброс возвращает как было.
        var parent = await db.AppUsers.SingleAsync(u => u.DisplayName == TestPersonas.Parent);
        var member = await db.AppUsers.SingleAsync(u => u.DisplayName == TestPersonas.Member);
        db.UserFavorites.RemoveRange(db.UserFavorites.Where(f => f.UserId == parent.Id));
        db.HubGroupUserMembers.RemoveRange(db.HubGroupUserMembers.Where(m => m.UserId == member.Id));
        await db.SaveChangesAsync();

        await seeder.ResetPersonaAsync(parent.Id);
        await seeder.ResetPersonaAsync(member.Id);

        Assert.Equal(2, await db.UserFavorites.CountAsync(f => f.UserId == parent.Id && f.IsFamily));
        Assert.Equal(2, await db.HubGroupUserMembers.CountAsync(m => m.UserId == member.Id));
    }

    [Fact]
    public async Task PersonaChanges_CleanAfterSeed_DirtyAfterEdit_CleanAfterReset()
    {
        await using var db = CreateDb(nameof(PersonaChanges_CleanAfterSeed_DirtyAfterEdit_CleanAfterReset));
        await SeedBaseAsync(db);
        var seeder = new PersonaSeeder(db);
        await seeder.SeedAsync();

        // Сторож зеркала: ожидаемое в GetPersonaChangesAsync обязано совпадать с тем, что выдаёт сидер.
        var clean = await seeder.GetPersonaChangesAsync();
        Assert.Equal(TestPersonas.All.Count, clean.Count);
        Assert.All(clean, kv => Assert.Empty(kv.Value));

        var parent = await db.AppUsers.SingleAsync(u => u.DisplayName == TestPersonas.Parent);
        var newbie = await db.AppUsers.SingleAsync(u => u.DisplayName == TestPersonas.Newbie);
        db.UserFavorites.RemoveRange(db.UserFavorites.Where(f => f.UserId == parent.Id).Take(1));
        newbie.ProfileSkipMe = true;
        db.HubGroupUserSeen.Add(new HubGroupUserSeen { UserId = newbie.Id, HubGroupId = (await db.HubGroups.FirstAsync()).Id });
        await db.SaveChangesAsync();

        var dirty = await seeder.GetPersonaChangesAsync();
        Assert.Equal(["избранное"], dirty[parent.Id]);
        Assert.Equal(["Set up profile (отказы / Dismiss)"], dirty[newbie.Id]); // «видел группу» — не изменение
        Assert.All(dirty.Where(kv => kv.Key != parent.Id && kv.Key != newbie.Id), kv => Assert.Empty(kv.Value));

        await seeder.ResetPersonaAsync(parent.Id);
        await seeder.ResetPersonaAsync(newbie.Id);
        Assert.All(await seeder.GetPersonaChangesAsync(), kv => Assert.Empty(kv.Value));
    }

    [Fact]
    public void Description_IsConsistent()
    {
        // Описание персонажей — данные: опечатка в slug или нике всплыла бы только при сиде.
        var slugs = TestPersonas.Groups.Select(g => g.Slug).ToHashSet();
        var nicks = TestPersonas.All.Select(p => p.Nick).ToHashSet();

        Assert.Equal(TestPersonas.All.Count, nicks.Count);                       // ники уникальны
        Assert.All(TestPersonas.All, p => Assert.True(TestAccountRules.IsTestEmail(p.Email)));
        Assert.All(TestPersonas.Groups, g => Assert.Contains(g.OwnerNick, nicks));
        Assert.All(TestPersonas.Groups, g => Assert.StartsWith(TestAccountRules.Prefix, g.Slug));
        Assert.All(TestPersonas.All, p =>
        {
            Assert.Contains(TestPersonas.RoleUser, p.Roles);
            Assert.All(p.Memberships, m => Assert.Contains(m.GroupSlug, slugs));
            Assert.All(p.AdminOf, s => Assert.Contains(s, slugs));
            Assert.All(p.Favorites, f => Assert.InRange(f.RosterIndex, 0, 5));      // состав — 6 пловцов
            Assert.All(p.Media, m => Assert.InRange(m.RosterIndex, 0, 5));
            Assert.True(p.Favorites.Count(f => f.Level == PersonaFavoriteLevel.Me) <= 1); // «это я» — один
        });
    }

    [Fact]
    public async Task ResetPersona_NotAPersona_Null()
    {
        await using var db = CreateDb(nameof(ResetPersona_NotAPersona_Null));
        await SeedBaseAsync(db);
        var seeder = new PersonaSeeder(db);
        await seeder.SeedAsync();

        var real = await db.AppUsers.SingleAsync(u => u.Email == "real.person@example.com");
        Assert.Null(await seeder.ResetPersonaAsync(real.Id));
        Assert.Null(await seeder.ResetPersonaAsync(int.MaxValue));
        Assert.Equal("Real", (await db.AppUsers.SingleAsync(u => u.Id == real.Id)).DisplayName);
    }

    private static async Task<(int, int, int, int, int, int, int, int, int)> CountsAsync(SwimmDbContext db) => (
        await db.AppUsers.CountAsync(),
        await db.AppUserRoles.CountAsync(),
        await db.HubGroups.CountAsync(),
        await db.HubGroupMembers.CountAsync(),
        await db.HubGroupUserMembers.CountAsync(),
        await db.HubGroupAdmins.CountAsync(),
        await db.UserFavorites.CountAsync(),
        await db.UserMedia.CountAsync() + await db.UserMediaPublications.CountAsync(),
        await db.TrainingSessions.CountAsync() + await db.TrainingResults.CountAsync());
}
