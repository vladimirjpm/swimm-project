using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тестовые группы (<c>HubGroup.IsTest</c>, docs/plans/test-personas-plan.md): видят только
/// site-админ и utest-аккаунты, остальным группы нет (404, не заглушка), в каталоге её нет
/// никогда, официальной стать не может. Регрессия здесь = тестовая группа протекла живым людям.
/// </summary>
public class HubGroupTestGroupTests
{
    /// <summary>
    /// Общий корень: без него rw- и read-контексты (разные типы) получают РАЗНЫЕ in-memory
    /// хранилища даже при одном имени, и каталог читал бы пустую базу.
    /// </summary>
    private static readonly InMemoryDatabaseRoot Root = new();

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name, Root)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>Read-контекст на ту же in-memory базу (каталог читается через него).</summary>
    private static SwimmReadDbContext CreateReadDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>().UseInMemoryDatabase(name, Root).Options);

    private sealed class SettingsStub : ISettingsService
    {
        public IReadOnlyList<AdminSetting> GetAll() => [];
        public AdminSetting? Get(string key) => null;
        public T GetValue<T>(string key, T fallback) => fallback;
        public bool Update(string key, string newValue) => true;
    }

    private sealed record Seed(AppUser Owner, AppUser Outsider, AppUser Tester, HubGroup TestGroup, HubGroup RealGroup);

    private static async Task<Seed> SeedAsync(SwimmDbContext db)
    {
        var owner = new AppUser { Email = "utest-coach@swimm.test", DisplayName = "utest coach", SecurityStamp = "s" };
        var outsider = new AppUser { Email = "real.person@example.com", DisplayName = "Real", SecurityStamp = "s" };
        var tester = new AppUser { Email = "utest-member@swimm.test", DisplayName = "utest member", SecurityStamp = "s" };
        db.AppUsers.AddRange(owner, outsider, tester);
        await db.SaveChangesAsync();

        var testGroup = new HubGroup { Name = "[TEST] Open", Slug = "test-open", OwnerUserId = owner.Id, IsPublic = true, IsTest = true };
        var realGroup = new HubGroup { Name = "Real", Slug = "real", OwnerUserId = owner.Id, IsPublic = true };
        db.HubGroups.AddRange(testGroup, realGroup);
        await db.SaveChangesAsync();
        return new Seed(owner, outsider, tester, testGroup, realGroup);
    }

    // ── TestAccountRules ────────────────────────────────────────────────────

    [Theory]
    [InlineData("utest-member@swimm.test", true)]
    [InlineData("UTest-Parent@swimm.test", true)]   // регистр не важен
    [InlineData("real.person@example.com", false)]
    [InlineData("someone@utest.com", false)]        // префикс — начало email, не домен
    [InlineData(null, false)]
    public void IsTestEmail_ByPrefix(string? email, bool expected) =>
        Assert.Equal(expected, TestAccountRules.IsTestEmail(email));

    // ── Права (HubGroupPermissionService) ───────────────────────────────────

    [Fact]
    public async Task Permissions_TestGroup_NotFoundForRealUser_EvenIfAccountMember()
    {
        await using var db = CreateDb(nameof(Permissions_TestGroup_NotFoundForRealUser_EvenIfAccountMember));
        var s = await SeedAsync(db);
        // Даже если живого человека как-то добавили участником — группы для него нет.
        db.HubGroupUserMembers.Add(new HubGroupUserMember
        {
            HubGroupId = s.TestGroup.Id, UserId = s.Outsider.Id, Status = HubGroupUserMemberStatus.Active
        });
        await db.SaveChangesAsync();

        var perms = await new HubGroupPermissionService(db).GetPermissionsAsync(s.TestGroup.Id, s.Outsider.Id, isAdmin: false);

        Assert.False(perms.Exists);
        Assert.False(perms.CanEdit);
    }

    [Fact]
    public async Task Permissions_TestGroup_VisibleToUtestAndSiteAdmin_RealGroupUnchanged()
    {
        await using var db = CreateDb(nameof(Permissions_TestGroup_VisibleToUtestAndSiteAdmin_RealGroupUnchanged));
        var s = await SeedAsync(db);
        var service = new HubGroupPermissionService(db);

        var owner = await service.GetPermissionsAsync(s.TestGroup.Id, s.Owner.Id, isAdmin: false);
        Assert.True(owner.Exists);
        Assert.True(owner.IsOwner);

        Assert.True((await service.GetPermissionsAsync(s.TestGroup.Id, s.Tester.Id, isAdmin: false)).Exists);
        Assert.True((await service.GetPermissionsAsync(s.TestGroup.Id, s.Outsider.Id, isAdmin: true)).Exists);
        Assert.True((await service.GetPermissionsAsync(s.RealGroup.Id, s.Outsider.Id, isAdmin: false)).Exists);
    }

    // ── Публичная страница и каталог (HubGroupPublicRepository) ─────────────

    [Fact]
    public async Task Access_TestGroup_NullForRealUserAndAnonymous_OpenForUtestAndAdmin()
    {
        var name = nameof(Access_TestGroup_NullForRealUserAndAnonymous_OpenForUtestAndAdmin);
        await using var db = CreateDb(name);
        await using var read = CreateReadDb(name);
        var s = await SeedAsync(db);
        var repo = new HubGroupPublicRepository(read, db, new SettingsStub());

        // null — «группы нет» (404), а не заглушка «только для участников».
        Assert.Null(await repo.GetAccessAsync(s.TestGroup.Slug, s.Outsider.Id, isSiteAdmin: false));
        Assert.Null(await repo.GetAccessAsync(s.TestGroup.Slug, userId: null, isSiteAdmin: false));

        var tester = await repo.GetAccessAsync(s.TestGroup.Slug, s.Tester.Id, isSiteAdmin: false);
        Assert.NotNull(tester);
        Assert.True(tester!.CanView);
        Assert.True(tester.IsTest);

        var admin = await repo.GetAccessAsync(s.TestGroup.Slug, s.Outsider.Id, isSiteAdmin: true);
        Assert.True(admin!.CanView);

        var real = await repo.GetAccessAsync(s.RealGroup.Slug, s.Outsider.Id, isSiteAdmin: false);
        Assert.False(real!.IsTest);
    }

    [Fact]
    public async Task Catalog_NeverListsTestGroups()
    {
        var name = nameof(Catalog_NeverListsTestGroups);
        await using var db = CreateDb(name);
        await using var read = CreateReadDb(name);
        await SeedAsync(db);

        var list = await new HubGroupPublicRepository(read, db, new SettingsStub()).GetGroupsAsync();

        Assert.Equal(["real"], list.Select(g => g.Slug).ToArray());
    }

    // ── Вступление и официальный статус (HubGroupUserService) ───────────────

    [Fact]
    public async Task Join_TestGroup_RealUserRefused_UtestJoinsAsUsual()
    {
        await using var db = CreateDb(nameof(Join_TestGroup_RealUserRefused_UtestJoinsAsUsual));
        var s = await SeedAsync(db);
        var service = new HubGroupUserService(db, new HubGroupCrudCore(db), new SettingsStub());

        var refused = await service.JoinAsync(s.TestGroup.Id, s.Outsider.Id);
        Assert.False(refused.Success);

        // Для тестового аккаунта open-группа ведёт себя как обычная: вступил сразу.
        var joined = await service.JoinAsync(s.TestGroup.Id, s.Tester.Id);
        Assert.True(joined.Success);
        var member = await db.HubGroupUserMembers.SingleAsync(m => m.HubGroupId == s.TestGroup.Id);
        Assert.Equal(s.Tester.Id, member.UserId);
        Assert.Equal(HubGroupUserMemberStatus.Active, member.Status);
    }

    [Fact]
    public async Task ClubRequest_TestGroupCannotBecomeOfficial()
    {
        await using var db = CreateDb(nameof(ClubRequest_TestGroupCannotBecomeOfficial));
        var s = await SeedAsync(db);
        var club = new Club { Name = "Club" };
        db.Clubs.Add(club);
        await db.SaveChangesAsync();
        var service = new HubGroupUserService(db, new HubGroupCrudCore(db), new SettingsStub());

        var result = await service.SubmitClubRequestAsync(s.TestGroup.Id, s.Owner.Id,
            new HubGroupClubRequestInputDto { ClubId = club.Id });

        Assert.False(result.Success);
        Assert.Equal(HubGroupClubRules.TestGroupCannotBeOfficialError, result.Error);
        Assert.False(await db.HubGroupClubRequests.AnyAsync());
    }
}
