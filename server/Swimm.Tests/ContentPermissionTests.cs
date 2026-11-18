using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
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
/// Права на контент отдельно от входа (docs/plans/content-permissions-plan.md): правило
/// <see cref="ContentPermissionRules"/>, сервис для эндпоинтов, исключение в /Admin/Users
/// (<see cref="AdminRepository.SetContentPermissionAsync"/>) и настройки политики в реестре.
/// </summary>
public class ContentPermissionTests
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

    private static SettingsStub PublishOff() => new(new() { [ContentPermissionRules.DefaultCanPublishKey] = "false" });

    private static async Task<AppUser> AddUserAsync(SwimmDbContext db, bool? photos = null, bool? videos = null, bool? publish = null)
    {
        var user = new AppUser
        {
            Email = $"u{Guid.NewGuid():N}@x.test", DisplayName = "u", SecurityStamp = "s",
            CanAddPhotos = photos, CanAddVideos = videos, CanPublish = publish,
        };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    // ── ContentPermissionRules.IsAllowed — все ветки ────────────────────────

    [Theory]
    // админ — всегда да, что бы ни стояло
    [InlineData(true, false, false, true)]
    [InlineData(true, null, false, true)]
    // исключение важнее политики — в обе стороны
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, false)]
    // без исключения — политика
    [InlineData(false, null, true, true)]
    [InlineData(false, null, false, false)]
    public void IsAllowed_AllBranches(bool isAdmin, bool? personal, bool policy, bool expected)
    {
        Assert.Equal(expected, ContentPermissionRules.IsAllowed(isAdmin, personal, policy));
    }

    [Theory]
    [InlineData("video", ContentAction.AddVideo)]
    [InlineData("VIDEO", ContentAction.AddVideo)]
    [InlineData("image", ContentAction.AddPhoto)]
    [InlineData("album", ContentAction.AddPhoto)]
    [InlineData(null, ContentAction.AddPhoto)]
    public void AddActionFor_VideoIsVideo_RestIsPhoto(string? mediaType, ContentAction expected)
    {
        Assert.Equal(expected, ContentPermissionRules.AddActionFor(mediaType));
    }

    [Fact]
    public void Evaluate_NoSettings_EverythingAllowed()
    {
        // Решение Влада 18.11.2026: дефолт — всё можно, как до плана.
        var p = ContentPermissionRules.Evaluate(new SettingsStub(), false, null, null, null);
        Assert.True(p.AddPhotos);
        Assert.True(p.AddVideos);
        Assert.True(p.Publish);
    }

    [Fact]
    public void Evaluate_PhotosAndVideosAreSeparate()
    {
        var p = ContentPermissionRules.Evaluate(new SettingsStub(), false, canAddPhotos: true, canAddVideos: false, canPublish: null);
        Assert.True(p.AddPhotos);
        Assert.False(p.AddVideos);
        Assert.True(p.Publish);
    }

    [Fact]
    public void Evaluate_PolicyOff_PersonalYesStillAllows()
    {
        // «Новым — без публикации», а этому админ разрешил.
        Assert.False(ContentPermissionRules.Evaluate(PublishOff(), false, null, null, null).Publish);
        Assert.True(ContentPermissionRules.Evaluate(PublishOff(), false, null, null, true).Publish);
    }

    [Fact]
    public void PolicyKey_MatchesAction()
    {
        Assert.Equal(ContentPermissionRules.DefaultCanAddPhotosKey, ContentPermissionRules.PolicyKey(ContentAction.AddPhoto));
        Assert.Equal(ContentPermissionRules.DefaultCanAddVideosKey, ContentPermissionRules.PolicyKey(ContentAction.AddVideo));
        Assert.Equal(ContentPermissionRules.DefaultCanPublishKey, ContentPermissionRules.PolicyKey(ContentAction.Publish));
    }

    // ── ContentPermissionService ────────────────────────────────────────────

    [Fact]
    public async Task Service_UnknownUser_NothingAllowed()
    {
        await using var db = CreateDb(nameof(Service_UnknownUser_NothingAllowed));
        var p = await new ContentPermissionService(db, new SettingsStub()).GetAsync(999, isAdmin: false);
        Assert.False(p.AddPhotos || p.AddVideos || p.Publish);
    }

    [Fact]
    public async Task Service_DenyReason_FirstForbiddenAction()
    {
        await using var db = CreateDb(nameof(Service_DenyReason_FirstForbiddenAction));
        var user = await AddUserAsync(db, videos: true, publish: false);
        var service = new ContentPermissionService(db, new SettingsStub());

        Assert.Null(await service.DenyReasonAsync(user.Id, false, ContentAction.AddVideo));
        Assert.Equal(ContentPermissionRules.DeniedMessage(ContentAction.Publish),
            await service.DenyReasonAsync(user.Id, false, ContentAction.AddVideo, ContentAction.Publish));
    }

    [Fact]
    public async Task Service_Admin_AlwaysAllowed()
    {
        await using var db = CreateDb(nameof(Service_Admin_AlwaysAllowed));
        var user = await AddUserAsync(db, photos: false, videos: false, publish: false);

        Assert.Null(await new ContentPermissionService(db, PublishOff())
            .DenyReasonAsync(user.Id, isAdmin: true, ContentAction.AddPhoto, ContentAction.AddVideo, ContentAction.Publish));
    }

    [Fact]
    public async Task Service_PolicyOff_AppliesToUserWithoutOverride()
    {
        await using var db = CreateDb(nameof(Service_PolicyOff_AppliesToUserWithoutOverride));
        var plain = await AddUserAsync(db);
        var allowed = await AddUserAsync(db, publish: true);
        var service = new ContentPermissionService(db, PublishOff());

        Assert.NotNull(await service.DenyReasonAsync(plain.Id, false, ContentAction.Publish));
        Assert.Null(await service.DenyReasonAsync(allowed.Id, false, ContentAction.Publish));
        // Своё (приватное) добавлять политика публикации не мешает.
        Assert.Null(await service.DenyReasonAsync(plain.Id, false, ContentAction.AddVideo));
    }

    // ── AdminRepository.SetContentPermissionAsync ───────────────────────────

    [Fact]
    public async Task SetContentPermission_SetsEachFieldAndClears()
    {
        await using var db = CreateDb(nameof(SetContentPermission_SetsEachFieldAndClears));
        var user = await AddUserAsync(db);
        var repo = new AdminRepository(db);

        Assert.True(await repo.SetContentPermissionAsync(user.Id, ContentAction.AddPhoto, false));
        Assert.True(await repo.SetContentPermissionAsync(user.Id, ContentAction.AddVideo, true));
        Assert.True(await repo.SetContentPermissionAsync(user.Id, ContentAction.Publish, false));
        var row = await db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.Equal((false, true, false), (row.CanAddPhotos, row.CanAddVideos, row.CanPublish));

        Assert.True(await repo.SetContentPermissionAsync(user.Id, ContentAction.Publish, null));
        Assert.Null((await db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == user.Id)).CanPublish);

        var detail = await repo.GetUserDetailsAsync(user.Id);
        Assert.Equal((false, true, (bool?)null), (detail!.CanAddPhotos, detail.CanAddVideos, detail.CanPublish));
        var listed = (await repo.GetUsersAsync()).Single(u => u.Id == user.Id);
        Assert.False(listed.CanAddPhotos);
    }

    [Fact]
    public async Task SetContentPermission_DoesNotBumpSecurityStamp()
    {
        await using var db = CreateDb(nameof(SetContentPermission_DoesNotBumpSecurityStamp));
        var user = await AddUserAsync(db);

        await new AdminRepository(db).SetContentPermissionAsync(user.Id, ContentAction.Publish, false);
        Assert.Equal("s", (await db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == user.Id)).SecurityStamp);
    }

    [Fact]
    public async Task SetContentPermission_UnknownUser_False()
    {
        await using var db = CreateDb(nameof(SetContentPermission_UnknownUser_False));
        Assert.False(await new AdminRepository(db).SetContentPermissionAsync(12345, ContentAction.AddPhoto, false));
    }

    // ── Реестр настроек ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(ContentPermissionRules.DefaultCanAddPhotosKey)]
    [InlineData(ContentPermissionRules.DefaultCanAddVideosKey)]
    [InlineData(ContentPermissionRules.DefaultCanPublishKey)]
    public void Settings_RegistryHasPolicyKeys_DefaultTrue_BoolOnly(string key)
    {
        var settings = new AdminSettingsService(new MemoryCache(new MemoryCacheOptions()));
        var s = settings.Get(key);
        Assert.NotNull(s);
        Assert.Equal("true", s!.Value);
        Assert.Equal("bool", s.DataType);
        Assert.False(settings.Update(key, "maybe"));
        Assert.True(settings.Update(key, "false"));
        Assert.False(settings.GetValue(key, true));
    }
}
