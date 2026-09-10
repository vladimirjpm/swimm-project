using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
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
/// Лимит групп (решение 10.09.2026): единое правило <see cref="HubGroupCreationRules"/>,
/// персональное исключение в /Admin/Users (<see cref="AdminRepository.SetHubGroupLimitAsync"/>)
/// и настройки лимитов по роли. Проверка при создании — в <c>HubGroupUserServiceTests</c>.
/// </summary>
public class HubGroupLimitTests
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

    private static AdminRepository BuildRepo(SwimmDbContext db) => new(db, Mock.Of<ICacheService>());

    private static async Task<AppUser> AddUserAsync(SwimmDbContext db, string email, int? limit = null)
    {
        var user = new AppUser { Email = email, DisplayName = email, SecurityStamp = "s", HubGroupLimit = limit };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    // ── HubGroupCreationRules.EffectiveLimit ────────────────────────────────

    [Fact]
    public void EffectiveLimit_Admin_IsUnlimited_EvenWithPersonal()
    {
        Assert.Null(HubGroupCreationRules.EffectiveLimit(new SettingsStub(), isAdmin: true, isCoach: false, personalLimit: 1));
    }

    [Fact]
    public void EffectiveLimit_PersonalWinsOverRole()
    {
        var settings = new SettingsStub(new() { ["HubGroupMaxPerUser"] = "3", ["HubGroupMaxPerCoach"] = "3" });

        Assert.Equal(7, HubGroupCreationRules.EffectiveLimit(settings, false, isCoach: false, personalLimit: 7));
        Assert.Equal(0, HubGroupCreationRules.EffectiveLimit(settings, false, isCoach: true, personalLimit: 0));
    }

    [Fact]
    public void EffectiveLimit_NoPersonal_ByRole_DefaultsTo3()
    {
        var settings = new SettingsStub(new() { ["HubGroupMaxPerCoach"] = "6" });

        Assert.Equal(3, HubGroupCreationRules.EffectiveLimit(settings, false, isCoach: false, personalLimit: null));
        Assert.Equal(6, HubGroupCreationRules.EffectiveLimit(settings, false, isCoach: true, personalLimit: null));
    }

    [Fact]
    public void EffectiveLimit_ClampsToZeroAndMax()
    {
        var settings = new SettingsStub(new() { ["HubGroupMaxPerUser"] = "-5" });

        Assert.Equal(0, HubGroupCreationRules.EffectiveLimit(settings, false, false, personalLimit: null));
        Assert.Equal(HubGroupCreationRules.MaxLimit,
            HubGroupCreationRules.EffectiveLimit(settings, false, false, personalLimit: 100_000));
    }

    [Fact]
    public void Evaluate_PolicyAdmin_DeniesNonAdmin_WithEnglishReason()
    {
        var settings = new SettingsStub(new() { ["HubGroupCreationPolicy"] = "admin" });

        var e = HubGroupCreationRules.Evaluate(settings, isAdmin: false, isCoach: true, personalLimit: 5, owned: 0);

        Assert.False(e.CanCreate);
        Assert.Equal("Creating groups is currently limited to site admins.", e.Reason);
    }

    // ── /Admin/Users: персональный лимит ───────────────────────────────────

    [Fact]
    public async Task SetHubGroupLimit_SetsAndClears()
    {
        await using var db = CreateDb(nameof(SetHubGroupLimit_SetsAndClears));
        var user = await AddUserAsync(db, "u@example.com");
        var repo = BuildRepo(db);

        Assert.True(await repo.SetHubGroupLimitAsync(user.Id, 10));
        Assert.Equal(10, (await db.AppUsers.SingleAsync(u => u.Id == user.Id)).HubGroupLimit);

        Assert.True(await repo.SetHubGroupLimitAsync(user.Id, null));
        Assert.Null((await db.AppUsers.SingleAsync(u => u.Id == user.Id)).HubGroupLimit);
    }

    [Fact]
    public async Task SetHubGroupLimit_DoesNotBumpSecurityStamp()
    {
        // Лимит не в claims — отзывать сессии незачем.
        await using var db = CreateDb(nameof(SetHubGroupLimit_DoesNotBumpSecurityStamp));
        var user = await AddUserAsync(db, "u@example.com");

        await BuildRepo(db).SetHubGroupLimitAsync(user.Id, 0);

        Assert.Equal("s", (await db.AppUsers.SingleAsync(u => u.Id == user.Id)).SecurityStamp);
    }

    [Fact]
    public async Task SetHubGroupLimit_UnknownUser_False()
    {
        await using var db = CreateDb(nameof(SetHubGroupLimit_UnknownUser_False));

        Assert.False(await BuildRepo(db).SetHubGroupLimitAsync(12345, 5));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(HubGroupCreationRules.MaxLimit + 1)]
    public async Task SetHubGroupLimit_OutOfRange_Throws(int limit)
    {
        await using var db = CreateDb(nameof(SetHubGroupLimit_OutOfRange_Throws) + limit);
        var user = await AddUserAsync(db, "u@example.com");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => BuildRepo(db).SetHubGroupLimitAsync(user.Id, limit));
        Assert.Null((await db.AppUsers.SingleAsync(u => u.Id == user.Id)).HubGroupLimit);
    }

    [Fact]
    public async Task GetUsersAndDetails_CarryOwnedCountAndPersonalLimit()
    {
        await using var db = CreateDb(nameof(GetUsersAndDetails_CarryOwnedCountAndPersonalLimit));
        var user = await AddUserAsync(db, "u@example.com", limit: 5);
        var other = await AddUserAsync(db, "o@example.com");
        db.HubGroups.AddRange(
            new HubGroup { Name = "A", Slug = "a", OwnerUserId = user.Id },
            new HubGroup { Name = "B", Slug = "b", OwnerUserId = user.Id },
            new HubGroup { Name = "C", Slug = "c", OwnerUserId = other.Id });
        await db.SaveChangesAsync();
        var repo = BuildRepo(db);

        var row = Assert.Single(await repo.GetUsersAsync(), u => u.Id == user.Id);
        var detail = await repo.GetUserDetailsAsync(user.Id);

        Assert.Equal(2, row.HubGroupsOwned);
        Assert.Equal(5, row.HubGroupLimit);
        Assert.Equal(2, detail!.HubGroupsOwned);
        Assert.Equal(5, detail.HubGroupLimit);
    }

    // ── Настройки лимитов по роли ──────────────────────────────────────────

    private static AdminSettingsService BuildSettings() =>
        new(new MemoryCache(Options.Create(new MemoryCacheOptions())));

    [Fact]
    public void Settings_Defaults_AreWorkingMode_AnyWith3And3()
    {
        var svc = BuildSettings();

        Assert.Equal("any", svc.Get("HubGroupCreationPolicy")!.Value);
        Assert.Equal("3", svc.Get("HubGroupMaxPerUser")!.Value);
        Assert.Equal("3", svc.Get("HubGroupMaxPerCoach")!.Value);
    }

    [Theory]
    [InlineData("HubGroupMaxPerUser")]
    [InlineData("HubGroupMaxPerCoach")]
    public void Settings_RoleLimit_RejectsOutOfRange_AcceptsZero(string key)
    {
        var svc = BuildSettings();

        Assert.False(svc.Update(key, "-1"));
        Assert.False(svc.Update(key, (HubGroupCreationRules.MaxLimit + 1).ToString()));
        Assert.True(svc.Update(key, "0"));
        Assert.Equal("0", svc.Get(key)!.Value);
    }
}
