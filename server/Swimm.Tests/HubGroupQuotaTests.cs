using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Потолки групп (Ш3.0, 28.09.2026, <see cref="HubGroupQuotaRules"/>): рубильник самозаписи,
/// аккаунтов в группе, членств и заявок аккаунта, ручных пловцов в составе. Аккаунты дешёвые —
/// каждая самообслуживаемая вставка упирается в потолок из /Admin/Settings.
/// </summary>
public class HubGroupQuotaTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class SettingsStub(Dictionary<string, string>? values = null) : ISettingsService
    {
        private readonly Dictionary<string, string> _values = values ?? new();
        public IReadOnlyList<AdminSetting> GetAll() => [];
        public AdminSetting? Get(string key) => null;
        public T GetValue<T>(string key, T fallback) =>
            _values.TryGetValue(key, out var raw) ? (T)Convert.ChangeType(raw, typeof(T)) : fallback;
        public bool Update(string key, string newValue) { _values[key] = newValue; return true; }
    }

    private static HubGroupUserService Users(SwimmDbContext db, Dictionary<string, string>? values = null) =>
        new(db, new HubGroupCrudCore(db), new SettingsStub(values));

    private static HubGroupAdminService Admin(SwimmDbContext db, Dictionary<string, string>? values = null) =>
        new(db, new HubGroupCrudCore(db), Mock.Of<IAdminAuditService>(), new SettingsStub(values));

    private static async Task<AppUser> AddUserAsync(SwimmDbContext db, string email)
    {
        var user = new AppUser { Email = email, DisplayName = email, SecurityStamp = "s" };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<HubGroup> AddGroupAsync(
        SwimmDbContext db, AppUser owner, string slug, string joinPolicy = HubGroupJoinPolicy.Open)
    {
        var group = new HubGroup { Name = slug, Slug = slug, OwnerUserId = owner.Id, JoinPolicy = joinPolicy };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    private static async Task AddAccountMembersAsync(SwimmDbContext db, HubGroup group, int count, string status = HubGroupUserMemberStatus.Active)
    {
        for (var i = 0; i < count; i++)
        {
            var u = await AddUserAsync(db, $"{group.Slug}-m{i}@x");
            db.HubGroupUserMembers.Add(new HubGroupUserMember { HubGroupId = group.Id, UserId = u.Id, Status = status });
        }
        await db.SaveChangesAsync();
    }

    private static async Task<Swimmer> AddSwimmerAsync(SwimmDbContext db, string last)
    {
        var s = new Swimmer { LastName = last, FirstName = "F", LastNameEn = last, FirstNameEn = "F", BirthYear = 2012 };
        db.Swimmers.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    // ── Самозапись ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Join_UnderAllLimits_Succeeds()
    {
        using var db = CreateDb(nameof(Join_UnderAllLimits_Succeeds));
        var owner = await AddUserAsync(db, "owner@x");
        var me = await AddUserAsync(db, "me@x");
        var group = await AddGroupAsync(db, owner, "g");

        var result = await Users(db).JoinAsync(group.Id, me.Id);

        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public async Task Join_SelfJoinSwitchedOff_Refused()
    {
        using var db = CreateDb(nameof(Join_SelfJoinSwitchedOff_Refused));
        var owner = await AddUserAsync(db, "owner@x");
        var me = await AddUserAsync(db, "me@x");
        var group = await AddGroupAsync(db, owner, "g");

        var result = await Users(db, new() { [HubGroupQuotaRules.SelfJoinEnabledKey] = "false" })
            .JoinAsync(group.Id, me.Id);

        Assert.False(result.Success);
        Assert.Equal(HubGroupQuotaRules.SelfJoinClosedError, result.Error);
        Assert.False(await db.HubGroupUserMembers.AnyAsync(m => m.UserId == me.Id));
    }

    [Fact]
    public async Task Join_GroupFull_CountsPendingToo()
    {
        using var db = CreateDb(nameof(Join_GroupFull_CountsPendingToo));
        var owner = await AddUserAsync(db, "owner@x");
        var me = await AddUserAsync(db, "me@x");
        var group = await AddGroupAsync(db, owner, "g");
        await AddAccountMembersAsync(db, group, 2);
        await AddAccountMembersAsync(db, group, 1, HubGroupUserMemberStatus.Pending);

        var result = await Users(db, new() { [HubGroupQuotaRules.MaxAccountMembersKey] = "3" })
            .JoinAsync(group.Id, me.Id);

        Assert.False(result.Success);
        Assert.Equal(HubGroupQuotaRules.GroupFullError(3), result.Error);
    }

    [Fact]
    public async Task Join_TooManyMemberships_Refused()
    {
        using var db = CreateDb(nameof(Join_TooManyMemberships_Refused));
        var owner = await AddUserAsync(db, "owner@x");
        var me = await AddUserAsync(db, "me@x");
        var users = Users(db, new() { [HubGroupQuotaRules.MaxMembershipsPerUserKey] = "2" });
        for (var i = 0; i < 2; i++)
        {
            var g = await AddGroupAsync(db, owner, $"g{i}");
            Assert.True((await users.JoinAsync(g.Id, me.Id)).Success);
        }
        var third = await AddGroupAsync(db, owner, "g-third");

        var result = await users.JoinAsync(third.Id, me.Id);

        Assert.False(result.Success);
        Assert.Equal(HubGroupQuotaRules.TooManyMembershipsError(2), result.Error);
    }

    [Fact]
    public async Task Join_PendingLimit_OnlyForRequests()
    {
        using var db = CreateDb(nameof(Join_PendingLimit_OnlyForRequests));
        var owner = await AddUserAsync(db, "owner@x");
        var me = await AddUserAsync(db, "me@x");
        var users = Users(db, new() { [HubGroupQuotaRules.MaxPendingPerUserKey] = "1" });
        var approval1 = await AddGroupAsync(db, owner, "a1", HubGroupJoinPolicy.Approval);
        var approval2 = await AddGroupAsync(db, owner, "a2", HubGroupJoinPolicy.Approval);
        var open = await AddGroupAsync(db, owner, "open");

        Assert.True((await users.JoinAsync(approval1.Id, me.Id)).Success);

        // Вторая заявка — упирается; вступление в открытую группу заявкой не является.
        var second = await users.JoinAsync(approval2.Id, me.Id);
        Assert.False(second.Success);
        Assert.Equal(HubGroupQuotaRules.TooManyPendingError(1), second.Error);
        Assert.True((await users.JoinAsync(open.Id, me.Id)).Success);
    }

    [Fact]
    public async Task Join_AlreadyMember_GetsOwnRefusal_NotQuota()
    {
        using var db = CreateDb(nameof(Join_AlreadyMember_GetsOwnRefusal_NotQuota));
        var owner = await AddUserAsync(db, "owner@x");
        var me = await AddUserAsync(db, "me@x");
        var group = await AddGroupAsync(db, owner, "g");
        var users = Users(db, new() { [HubGroupQuotaRules.MaxAccountMembersKey] = "1" });
        Assert.True((await users.JoinAsync(group.Id, me.Id)).Success);

        var again = await users.JoinAsync(group.Id, me.Id);

        Assert.False(again.Success);
        Assert.NotEqual(HubGroupQuotaRules.GroupFullError(1), again.Error);
    }

    // ── Добавление аккаунта по email ────────────────────────────────────────

    [Fact]
    public async Task AddByEmail_GroupFull_Refused()
    {
        using var db = CreateDb(nameof(AddByEmail_GroupFull_Refused));
        var owner = await AddUserAsync(db, "owner@x");
        await AddUserAsync(db, "new@x");
        var group = await AddGroupAsync(db, owner, "g");
        await AddAccountMembersAsync(db, group, 2);

        var result = await Users(db, new() { [HubGroupQuotaRules.MaxAccountMembersKey] = "2" })
            .AddUserMemberAsync(group.Id, "new@x", owner.Id);

        Assert.False(result.Success);
        Assert.Equal(HubGroupQuotaRules.GroupFullError(2), result.Error);
    }

    [Fact]
    public async Task AddByEmail_TargetInTooManyGroups_Refused_WorksWhenSelfJoinClosed()
    {
        using var db = CreateDb(nameof(AddByEmail_TargetInTooManyGroups_Refused_WorksWhenSelfJoinClosed));
        var owner = await AddUserAsync(db, "owner@x");
        var target = await AddUserAsync(db, "target@x");
        var busy = await AddGroupAsync(db, owner, "busy");
        db.HubGroupUserMembers.Add(new HubGroupUserMember { HubGroupId = busy.Id, UserId = target.Id });
        await db.SaveChangesAsync();
        var group = await AddGroupAsync(db, owner, "g");

        var capped = await Users(db, new() { [HubGroupQuotaRules.MaxMembershipsPerUserKey] = "1" })
            .AddUserMemberAsync(group.Id, "target@x", owner.Id);
        Assert.False(capped.Success);
        Assert.Equal(HubGroupQuotaRules.UserTooManyMembershipsError(1), capped.Error);

        // Рубильник закрывает только самозапись: админ группы по email добавляет.
        var added = await Users(db, new() { [HubGroupQuotaRules.SelfJoinEnabledKey] = "false" })
            .AddUserMemberAsync(group.Id, "target@x", owner.Id);
        Assert.True(added.Success, added.Error);
    }

    // ── Ручной состав ───────────────────────────────────────────────────────

    [Fact]
    public async Task Roster_ManualLimit_ClubRowsNotCounted()
    {
        using var db = CreateDb(nameof(Roster_ManualLimit_ClubRowsNotCounted));
        var owner = await AddUserAsync(db, "owner@x");
        var group = await AddGroupAsync(db, owner, "g");
        var manual = await AddSwimmerAsync(db, "Manual");
        var fromClub1 = await AddSwimmerAsync(db, "Club1");
        var fromClub2 = await AddSwimmerAsync(db, "Club2");
        var extra = await AddSwimmerAsync(db, "Extra");
        db.HubGroupMembers.AddRange(
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = fromClub1.Id, Source = HubGroupMemberSource.Club },
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = fromClub2.Id, Source = HubGroupMemberSource.Club });
        await db.SaveChangesAsync();
        var admin = Admin(db, new() { [HubGroupQuotaRules.MaxManualSwimmersKey] = "1" });

        // Два клубных в составе не мешают первому ручному.
        Assert.True((await admin.AddMemberAsync(group.Id, manual.Id, "member")).Success);

        var over = await admin.AddMemberAsync(group.Id, extra.Id, "member");
        Assert.False(over.Success);
        Assert.Equal(HubGroupQuotaRules.RosterFullError(1), over.Error);

        // Клубный, которого выбрали руками, становится ручным — тоже в счёт.
        var promote = await admin.AddMemberAsync(group.Id, fromClub1.Id, "member");
        Assert.False(promote.Success);
        Assert.Equal(HubGroupMemberSource.Club,
            (await db.HubGroupMembers.SingleAsync(m => m.SwimmerId == fromClub1.Id)).Source);
    }

    [Fact]
    public async Task Roster_AlreadyManual_KeepsOwnRefusal()
    {
        using var db = CreateDb(nameof(Roster_AlreadyManual_KeepsOwnRefusal));
        var owner = await AddUserAsync(db, "owner@x");
        var group = await AddGroupAsync(db, owner, "g");
        var s = await AddSwimmerAsync(db, "S");
        var admin = Admin(db, new() { [HubGroupQuotaRules.MaxManualSwimmersKey] = "1" });
        Assert.True((await admin.AddMemberAsync(group.Id, s.Id, "member")).Success);

        var again = await admin.AddMemberAsync(group.Id, s.Id, "member");

        Assert.False(again.Success);
        Assert.NotEqual(HubGroupQuotaRules.RosterFullError(1), again.Error);
    }

    // ── Правило и настройки ─────────────────────────────────────────────────

    [Fact]
    public void Limit_DefaultsAndClamp()
    {
        Assert.Equal(150, HubGroupQuotaRules.Limit(new SettingsStub(), HubGroupQuotaRules.MaxAccountMembersKey));
        Assert.Equal(200, HubGroupQuotaRules.Limit(new SettingsStub(), HubGroupQuotaRules.MaxManualSwimmersKey));
        Assert.Equal(20, HubGroupQuotaRules.Limit(new SettingsStub(), HubGroupQuotaRules.MaxMembershipsPerUserKey));
        Assert.Equal(5, HubGroupQuotaRules.Limit(new SettingsStub(), HubGroupQuotaRules.MaxPendingPerUserKey));
        Assert.Equal(1, HubGroupQuotaRules.Limit(
            new SettingsStub(new() { [HubGroupQuotaRules.MaxPendingPerUserKey] = "0" }), HubGroupQuotaRules.MaxPendingPerUserKey));
        Assert.True(HubGroupQuotaRules.SelfJoinEnabled(new SettingsStub()));
    }

    [Fact]
    public void Settings_RegisteredWithDefaults_AndValidated()
    {
        var settings = new AdminSettingsService(
            new Microsoft.Extensions.Caching.Memory.MemoryCache(
                Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())));

        foreach (var key in HubGroupQuotaRules.LimitKeys)
        {
            Assert.Equal(HubGroupQuotaRules.DefaultFor(key).ToString(), settings.Get(key)!.Value);
            Assert.False(settings.Update(key, "0"));
            Assert.False(settings.Update(key, "5001"));
            Assert.True(settings.Update(key, "7"));
        }
        Assert.Equal("true", settings.Get(HubGroupQuotaRules.SelfJoinEnabledKey)!.Value);
        Assert.False(settings.Update(HubGroupQuotaRules.SelfJoinEnabledKey, "maybe"));
    }
}
