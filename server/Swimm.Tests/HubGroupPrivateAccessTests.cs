using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
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
/// Приватная группа (решение Влада 11.09.2026, §6-6 плана подписки): видят участники и
/// управляющие, остальным — заглушка «только для участников», а не 404. Раньше «приватная»
/// отдавала 404 всем, включая владельца, — режимом нельзя было пользоваться.
/// Решение «кому страница, кому заглушка» принимает контроллер по GetAccessAsync — здесь
/// проверяются правило, доступ и то, что в заглушке нет данных группы.
/// </summary>
public class HubGroupPrivateAccessTests
{
    private static SwimmReadDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class VisibilityStub(string visibility) : ISettingsService
    {
        public IReadOnlyList<AdminSetting> GetAll() => [];
        public AdminSetting? Get(string key) => null;
        public T GetValue<T>(string key, T fallback) =>
            key == HubGroupVisibilityRules.SettingKey ? (T)(object)visibility : fallback;
        public bool Update(string key, string newValue) => true;
    }

    private sealed class World
    {
        public AppUser Owner = null!, GroupAdmin = null!, Member = null!, Pending = null!, Stranger = null!;
        public HubGroup Private = null!, Public = null!;
    }

    private static async Task<World> SeedAsync(SwimmDbContext db)
    {
        AppUser U(string e) => new() { Email = e, DisplayName = e, SecurityStamp = "s" };
        var w = new World
        {
            Owner = U("owner@example.com"), GroupAdmin = U("admin@example.com"), Member = U("member@example.com"),
            Pending = U("pending@example.com"), Stranger = U("stranger@example.com")
        };
        db.AppUsers.AddRange(w.Owner, w.GroupAdmin, w.Member, w.Pending, w.Stranger);
        await db.SaveChangesAsync();

        var swimmer = new Swimmer { LastName = "כהן", FirstName = "טל", BirthYear = 2013 };
        w.Private = new HubGroup
        {
            Name = "Private squad", Slug = "private-squad", OwnerUserId = w.Owner.Id, IsPublic = false,
            Description = "Secret plans", JoinPolicy = HubGroupJoinPolicy.Open
        };
        w.Public = new HubGroup { Name = "Open squad", Slug = "open-squad", OwnerUserId = w.Owner.Id, IsPublic = true };
        db.AddRange(swimmer, w.Private, w.Public);
        await db.SaveChangesAsync();

        db.HubGroupMembers.Add(new HubGroupMember { HubGroupId = w.Private.Id, SwimmerId = swimmer.Id });
        db.HubGroupAdmins.Add(new HubGroupAdmin { HubGroupId = w.Private.Id, UserId = w.GroupAdmin.Id, GrantedByUserId = w.Owner.Id });
        db.HubGroupUserMembers.AddRange(
            new HubGroupUserMember { HubGroupId = w.Private.Id, UserId = w.Member.Id, Status = HubGroupUserMemberStatus.Active },
            new HubGroupUserMember { HubGroupId = w.Private.Id, UserId = w.Pending.Id, Status = HubGroupUserMemberStatus.Pending });
        await db.SaveChangesAsync();
        return w;
    }

    private static HubGroupPublicRepository Repo(SwimmReadDbContext db, string visibility = HubGroupVisibilityRules.PerGroup) =>
        new(db, db, new VisibilityStub(visibility));

    // ── Правило ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("perGroup", true, false)]
    [InlineData("perGroup", false, true)]
    [InlineData("private", true, true)]   // всё только для участников
    [InlineData("public", false, false)]  // галочка не действует
    public void IsPrivate_Matrix(string visibility, bool isPublic, bool expected)
    {
        Assert.Equal(expected, HubGroupVisibilityRules.IsPrivate(visibility, isPublic));
    }

    [Fact]
    public void SettingDefault_IsPerGroup_SoTheCheckboxWorks()
    {
        var settings = new AdminSettingsService(new MemoryCache(Options.Create(new MemoryCacheOptions())));

        Assert.Equal(HubGroupVisibilityRules.PerGroup, settings.Get(HubGroupVisibilityRules.SettingKey)!.Value);
    }

    // ── Доступ ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task PrivateGroup_MembersAndManagersSee_OthersDoNot()
    {
        await using var db = CreateDb(nameof(PrivateGroup_MembersAndManagersSee_OthersDoNot));
        var w = await SeedAsync(db);
        var repo = Repo(db);

        async Task<bool> Can(int? userId, bool siteAdmin = false) =>
            (await repo.GetAccessAsync(w.Private.Slug, userId, siteAdmin))!.CanView;

        Assert.True(await Can(w.Owner.Id));
        Assert.True(await Can(w.GroupAdmin.Id));
        Assert.True(await Can(w.Member.Id));
        Assert.True(await Can(w.Stranger.Id, siteAdmin: true));

        Assert.False(await Can(null));             // гость
        Assert.False(await Can(w.Stranger.Id));
        Assert.False(await Can(w.Pending.Id));     // заявка ещё не участие

        var access = await repo.GetAccessAsync(w.Private.Slug, null, false);
        Assert.True(access!.IsPrivate);
        Assert.Null(await repo.GetAccessAsync("no-such-group", w.Owner.Id, false));
    }

    [Fact]
    public async Task PublicGroup_EveryoneSees_GlobalPrivateMakesItMembersOnly()
    {
        await using var db = CreateDb(nameof(PublicGroup_EveryoneSees_GlobalPrivateMakesItMembersOnly));
        var w = await SeedAsync(db);

        var open = await Repo(db).GetAccessAsync(w.Public.Slug, null, false);
        Assert.False(open!.IsPrivate);
        Assert.True(open.CanView);

        var global = await Repo(db, HubGroupVisibilityRules.Private).GetAccessAsync(w.Public.Slug, w.Stranger.Id, false);
        Assert.True(global!.IsPrivate);
        Assert.False(global.CanView);
    }

    // ── Что отдаётся ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Stub_CarriesOnlyWhoAndHowToJoin_NoGroupData()
    {
        await using var db = CreateDb(nameof(Stub_CarriesOnlyWhoAndHowToJoin_NoGroupData));
        var w = await SeedAsync(db);

        var stub = await Repo(db).GetMembersOnlyStubAsync(w.Private.Slug);

        Assert.True(stub!.MembersOnly);
        Assert.True(stub.IsPrivate);
        Assert.Equal("Private squad", stub.Name);
        Assert.Equal(HubGroupJoinPolicy.Approval, stub.JoinPolicy); // в приватную — только заявкой
        Assert.Empty(stub.Members);
        Assert.Null(stub.Description);
        Assert.Empty(stub.RecentResults);
    }

    [Fact]
    public async Task FullPage_IsServedForPrivateGroup_ToWhoeverTheControllerLetsIn()
    {
        await using var db = CreateDb(nameof(FullPage_IsServedForPrivateGroup_ToWhoeverTheControllerLetsIn));
        var w = await SeedAsync(db);

        // Раньше репозиторий возвращал null (→ 404 и владельцу). Теперь отдаёт, а гейт — в контроллере.
        var page = await Repo(db).GetBySlugAsync(w.Private.Slug);

        Assert.NotNull(page);
        Assert.True(page!.IsPrivate);
        Assert.False(page.MembersOnly);
        Assert.Single(page.Members);
        Assert.NotNull(await Repo(db).GetRosterSwimmerIdsAsync(w.Private.Slug));
    }

    [Fact]
    public async Task Catalog_NeverListsPrivateGroups()
    {
        await using var db = CreateDb(nameof(Catalog_NeverListsPrivateGroups));
        var w = await SeedAsync(db);

        var names = (await Repo(db).GetGroupsAsync()).Select(g => g.Name).ToList();

        Assert.Equal(["Open squad"], names);
        Assert.Empty(await Repo(db, HubGroupVisibilityRules.Private).GetGroupsAsync());
    }
}
