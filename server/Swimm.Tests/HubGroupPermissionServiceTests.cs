using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты единой точки авторизации групп (8.6, <see cref="HubGroupPermissionService"/>):
/// матрица прав владелец / админ группы / site-админ / посторонний. Именно на эти флаги
/// опирается <c>MyHubGroupsController</c>, поэтому регрессия здесь = дыра в доступе.
/// </summary>
public class HubGroupPermissionServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task<(int ownerId, int groupId)> SeedGroupAsync(SwimmDbContext db)
    {
        var owner = new AppUser { Email = "owner@example.com", DisplayName = "Owner", SecurityStamp = "s" };
        db.AppUsers.Add(owner);
        await db.SaveChangesAsync();
        var group = new HubGroup { Name = "G", Slug = "g", OwnerUserId = owner.Id, IsPublic = true };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();
        return (owner.Id, group.Id);
    }

    [Fact]
    public async Task Owner_CanEditDeleteAndManageAdmins_ButNotChangeOwner()
    {
        await using var db = CreateDb(nameof(Owner_CanEditDeleteAndManageAdmins_ButNotChangeOwner));
        var (ownerId, groupId) = await SeedGroupAsync(db);

        var perms = await new HubGroupPermissionService(db).GetPermissionsAsync(groupId, ownerId, isAdmin: false);

        Assert.True(perms.Exists);
        Assert.True(perms.IsOwner);
        Assert.False(perms.IsGroupAdmin);
        Assert.True(perms.CanEdit);
        Assert.True(perms.CanDelete);
        Assert.True(perms.CanManageAdmins);
        Assert.False(perms.CanChangeOwner);
    }

    [Fact]
    public async Task GroupAdmin_CanEdit_ButNotDeleteManageAdminsOrChangeOwner()
    {
        await using var db = CreateDb(nameof(GroupAdmin_CanEdit_ButNotDeleteManageAdminsOrChangeOwner));
        var (ownerId, groupId) = await SeedGroupAsync(db);
        var groupAdmin = new AppUser { Email = "co@example.com", DisplayName = "Co", SecurityStamp = "s" };
        db.AppUsers.Add(groupAdmin);
        await db.SaveChangesAsync();
        db.HubGroupAdmins.Add(new HubGroupAdmin { HubGroupId = groupId, UserId = groupAdmin.Id, GrantedByUserId = ownerId });
        await db.SaveChangesAsync();

        var perms = await new HubGroupPermissionService(db).GetPermissionsAsync(groupId, groupAdmin.Id, isAdmin: false);

        Assert.True(perms.Exists);
        Assert.False(perms.IsOwner);
        Assert.True(perms.IsGroupAdmin);
        Assert.True(perms.CanEdit);
        Assert.False(perms.CanDelete);
        Assert.False(perms.CanManageAdmins);
        Assert.False(perms.CanChangeOwner);
    }

    [Fact]
    public async Task Admin_NonOwner_CanDoEverythingIncludingChangeOwner()
    {
        await using var db = CreateDb(nameof(Admin_NonOwner_CanDoEverythingIncludingChangeOwner));
        var (_, groupId) = await SeedGroupAsync(db);

        // userId 999 — не владелец и не админ группы, но site-админ.
        var perms = await new HubGroupPermissionService(db).GetPermissionsAsync(groupId, userId: 999, isAdmin: true);

        Assert.True(perms.Exists);
        Assert.False(perms.IsOwner);
        Assert.True(perms.IsAdmin);
        Assert.True(perms.CanEdit);
        Assert.True(perms.CanDelete);
        Assert.True(perms.CanManageAdmins);
        Assert.True(perms.CanChangeOwner);
    }

    [Fact]
    public async Task Stranger_GroupExists_ButNoRights()
    {
        await using var db = CreateDb(nameof(Stranger_GroupExists_ButNoRights));
        var (_, groupId) = await SeedGroupAsync(db);

        var perms = await new HubGroupPermissionService(db).GetPermissionsAsync(groupId, userId: 999, isAdmin: false);

        Assert.True(perms.Exists);
        Assert.False(perms.CanEdit);
        Assert.False(perms.CanDelete);
        Assert.False(perms.CanManageAdmins);
        Assert.False(perms.CanChangeOwner);
    }

    [Fact]
    public async Task NonexistentGroup_ReturnsNotFound_PreservesIsAdmin()
    {
        await using var db = CreateDb(nameof(NonexistentGroup_ReturnsNotFound_PreservesIsAdmin));

        var perms = await new HubGroupPermissionService(db).GetPermissionsAsync(hubGroupId: 12345, userId: 1, isAdmin: true);

        Assert.False(perms.Exists);
        Assert.True(perms.IsAdmin);
        Assert.False(perms.CanEdit);
    }
}
