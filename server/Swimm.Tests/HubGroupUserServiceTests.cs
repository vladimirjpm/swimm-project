using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты пользовательского самообслуживания групп (8.6, <see cref="HubGroupUserService"/>):
/// enforcement политики/лимита создания, админы группы, и гейт привязки к клубу —
/// ввод ClubId на пользовательском пути игнорируется (официальная связь только через
/// одобрение админа 8.7; иначе спуфинг/перепривязка клуба крафтовым запросом).
/// </summary>
public class HubGroupUserServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private sealed class NoopCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult(default(T));
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    /// <summary>Настройки с настраиваемыми значениями (policy/лимит).</summary>
    private sealed class SettingsStub : ISettingsService
    {
        private readonly Dictionary<string, string> _values;
        public SettingsStub(Dictionary<string, string>? values = null) => _values = values ?? new();
        public IReadOnlyList<AdminSetting> GetAll() => [];
        public AdminSetting? Get(string key) => null;
        public T GetValue<T>(string key, T fallback) =>
            _values.TryGetValue(key, out var raw) ? (T)Convert.ChangeType(raw, typeof(T)) : fallback;
        public bool Update(string key, string newValue) { _values[key] = newValue; return true; }
    }

    private static HubGroupUserService Service(SwimmDbContext db, ISettingsService? settings = null) =>
        new(db, new HubGroupCrudCore(db, new NoopCacheService()), settings ?? new SettingsStub());

    private static async Task<AppUser> AddUserAsync(SwimmDbContext db, string email)
    {
        var user = new AppUser { Email = email, DisplayName = email, SecurityStamp = "s" };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    // ── GetCreateEligibilityAsync ───────────────────────────────────────────

    [Fact]
    public async Task Eligibility_Admin_AlwaysCanCreate_NoLimit()
    {
        await using var db = CreateDb(nameof(Eligibility_Admin_AlwaysCanCreate_NoLimit));
        var settings = new SettingsStub(new() { ["HubGroupCreationPolicy"] = "admin" });

        var e = await Service(db, settings).GetCreateEligibilityAsync(userId: 1, isAdmin: true, isCoach: false);

        Assert.True(e.CanCreate);
        Assert.Null(e.Remaining);
    }

    [Fact]
    public async Task Eligibility_PolicyAdmin_NonAdmin_Denied()
    {
        await using var db = CreateDb(nameof(Eligibility_PolicyAdmin_NonAdmin_Denied));
        var settings = new SettingsStub(new() { ["HubGroupCreationPolicy"] = "admin" });

        var e = await Service(db, settings).GetCreateEligibilityAsync(userId: 1, isAdmin: false, isCoach: true);

        Assert.False(e.CanCreate);
    }

    [Fact]
    public async Task Eligibility_PolicyCoach_NonCoach_Denied_CoachAllowed()
    {
        await using var db = CreateDb(nameof(Eligibility_PolicyCoach_NonCoach_Denied_CoachAllowed));
        var settings = new SettingsStub(new() { ["HubGroupCreationPolicy"] = "coach", ["HubGroupMaxPerUser"] = "3" });
        var svc = Service(db, settings);

        Assert.False((await svc.GetCreateEligibilityAsync(1, isAdmin: false, isCoach: false)).CanCreate);
        Assert.True((await svc.GetCreateEligibilityAsync(1, isAdmin: false, isCoach: true)).CanCreate);
    }

    [Fact]
    public async Task Eligibility_PolicyAny_RespectsMaxPerUserLimit()
    {
        await using var db = CreateDb(nameof(Eligibility_PolicyAny_RespectsMaxPerUserLimit));
        var settings = new SettingsStub(new() { ["HubGroupCreationPolicy"] = "any", ["HubGroupMaxPerUser"] = "2" });
        var owner = await AddUserAsync(db, "owner@example.com");
        db.HubGroups.Add(new HubGroup { Name = "A", Slug = "a", OwnerUserId = owner.Id });
        await db.SaveChangesAsync();
        var svc = Service(db, settings);

        var underLimit = await svc.GetCreateEligibilityAsync(owner.Id, isAdmin: false, isCoach: false);
        Assert.True(underLimit.CanCreate);
        Assert.Equal(1, underLimit.Remaining);

        db.HubGroups.Add(new HubGroup { Name = "B", Slug = "b", OwnerUserId = owner.Id });
        await db.SaveChangesAsync();

        var atLimit = await svc.GetCreateEligibilityAsync(owner.Id, isAdmin: false, isCoach: false);
        Assert.False(atLimit.CanCreate);
        Assert.Equal(0, atLimit.Remaining);
    }

    // ── ClubId gating (S1) ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_IgnoresInputClubId()
    {
        await using var db = CreateDb(nameof(CreateAsync_IgnoresInputClubId));
        var settings = new SettingsStub(new() { ["HubGroupCreationPolicy"] = "any", ["HubGroupMaxPerUser"] = "5" });
        var owner = await AddUserAsync(db, "owner@example.com");

        var result = await Service(db, settings).CreateAsync(
            new HubGroupInputDto { Name = "Free Group", ClubId = 999, IsPublic = true },
            owner.Id, isAdmin: false, isCoach: false);

        Assert.True(result.Success);
        var group = await db.HubGroups.SingleAsync(g => g.Id == result.Id);
        Assert.Null(group.ClubId); // ввод ClubId проигнорирован
    }

    [Fact]
    public async Task UpdateAsync_PreservesExistingClubId_IgnoresInput()
    {
        await using var db = CreateDb(nameof(UpdateAsync_PreservesExistingClubId_IgnoresInput));
        var owner = await AddUserAsync(db, "owner@example.com");
        var club = new Club { Name = "Real Club" };
        var otherClub = new Club { Name = "Other Club" };
        db.Clubs.AddRange(club, otherClub);
        await db.SaveChangesAsync();
        var group = new HubGroup
        {
            Name = "Official", Slug = "official", OwnerUserId = owner.Id,
            IsPublic = true, IsOfficial = true, ClubId = club.Id
        };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();

        // Владелец правит имя и (крафтом) шлёт другой ClubId — должен быть проигнорирован.
        var result = await Service(db).UpdateAsync(group.Id,
            new HubGroupInputDto { Name = "Renamed", ClubId = otherClub.Id, IsPublic = true });

        Assert.True(result.Success);
        var updated = await db.HubGroups.SingleAsync(g => g.Id == group.Id);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal(club.Id, updated.ClubId); // клуб не сменился и не обнулился
        Assert.True(updated.IsOfficial);
    }

    // ── Admins (админы группы) ───────────────────────────────────────────────

    [Fact]
    public async Task AddAdmin_HappyPath_ByExactEmail()
    {
        await using var db = CreateDb(nameof(AddAdmin_HappyPath_ByExactEmail));
        var owner = await AddUserAsync(db, "owner@example.com");
        var target = await AddUserAsync(db, "co@example.com");
        var group = new HubGroup { Name = "G", Slug = "g", OwnerUserId = owner.Id };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();

        var result = await Service(db).AddAdminAsync(group.Id, "co@example.com", owner.Id);

        Assert.True(result.Success);
        var row = await db.HubGroupAdmins.SingleAsync(m => m.HubGroupId == group.Id && m.UserId == target.Id);
        Assert.Equal(owner.Id, row.GrantedByUserId); // кто назначил — проставляется
    }

    [Fact]
    public async Task AddAdmin_TrimsEmailAndMatches()
    {
        await using var db = CreateDb(nameof(AddAdmin_TrimsEmailAndMatches));
        var owner = await AddUserAsync(db, "owner@example.com");
        var target = await AddUserAsync(db, "co@example.com");
        var group = new HubGroup { Name = "G", Slug = "g", OwnerUserId = owner.Id };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();

        var result = await Service(db).AddAdminAsync(group.Id, "  co@example.com  ", owner.Id);

        Assert.True(result.Success);
        Assert.True(await db.HubGroupAdmins.AnyAsync(m => m.UserId == target.Id));
    }

    [Fact]
    public async Task AddAdmin_EmptyEmail_Fails()
    {
        await using var db = CreateDb(nameof(AddAdmin_EmptyEmail_Fails));
        var owner = await AddUserAsync(db, "owner@example.com");
        var group = new HubGroup { Name = "G", Slug = "g", OwnerUserId = owner.Id };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();

        var result = await Service(db).AddAdminAsync(group.Id, "   ", owner.Id);

        Assert.False(result.Success);
        Assert.Empty(db.HubGroupAdmins);
    }

    [Fact]
    public async Task AddAdmin_GroupNotFound_Fails()
    {
        await using var db = CreateDb(nameof(AddAdmin_GroupNotFound_Fails));
        await AddUserAsync(db, "co@example.com");

        var result = await Service(db).AddAdminAsync(hubGroupId: 999, "co@example.com", grantedByUserId: 1);

        Assert.False(result.Success);
        Assert.Empty(db.HubGroupAdmins);
    }

    [Fact]
    public async Task AddAdmin_UnknownEmail_Fails()
    {
        await using var db = CreateDb(nameof(AddAdmin_UnknownEmail_Fails));
        var owner = await AddUserAsync(db, "owner@example.com");
        var group = new HubGroup { Name = "G", Slug = "g", OwnerUserId = owner.Id };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();

        var result = await Service(db).AddAdminAsync(group.Id, "nobody@example.com", owner.Id);

        Assert.False(result.Success);
        Assert.Empty(db.HubGroupAdmins);
    }

    [Fact]
    public async Task AddAdmin_Duplicate_Fails()
    {
        await using var db = CreateDb(nameof(AddAdmin_Duplicate_Fails));
        var owner = await AddUserAsync(db, "owner@example.com");
        var target = await AddUserAsync(db, "co@example.com");
        var group = new HubGroup { Name = "G", Slug = "g", OwnerUserId = owner.Id };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();
        var svc = Service(db);
        await svc.AddAdminAsync(group.Id, "co@example.com", owner.Id);

        var again = await svc.AddAdminAsync(group.Id, "co@example.com", owner.Id);

        Assert.False(again.Success);
        Assert.Single(db.HubGroupAdmins);
    }

    [Fact]
    public async Task RemoveAdmin_HappyPath_AndNotFound()
    {
        await using var db = CreateDb(nameof(RemoveAdmin_HappyPath_AndNotFound));
        var owner = await AddUserAsync(db, "owner@example.com");
        var target = await AddUserAsync(db, "co@example.com");
        var group = new HubGroup { Name = "G", Slug = "g", OwnerUserId = owner.Id };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();
        var svc = Service(db);
        await svc.AddAdminAsync(group.Id, "co@example.com", owner.Id);

        var removed = await svc.RemoveAdminAsync(group.Id, target.Id);
        Assert.True(removed.Success);
        Assert.Empty(db.HubGroupAdmins);

        var missing = await svc.RemoveAdminAsync(group.Id, target.Id);
        Assert.False(missing.Success);
    }
}
