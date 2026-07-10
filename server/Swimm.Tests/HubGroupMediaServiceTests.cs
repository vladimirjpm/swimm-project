using Microsoft.EntityFrameworkCore;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты <see cref="HubGroupMediaService"/>: валидация мутаций (enum-значения, album-инвариант,
/// https-url, training_id из чужой группы) и фильтрация публичной галереи (TrainingId == null).
/// Авторизация (кто вправе мутировать) — на стороне контроллера через уже покрытый тестами
/// <see cref="HubGroupPermissionService"/> (CanEdit = владелец/админ группы/site-админ);
/// сервис данные приватности не проверяет, только валидирует форму и принадлежность группе.
/// </summary>
public class HubGroupMediaServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task<(int userId, int groupId)> SeedGroupAsync(SwimmDbContext db)
    {
        var owner = new AppUser { Email = "owner@example.com", DisplayName = "Owner", SecurityStamp = "s" };
        db.AppUsers.Add(owner);
        await db.SaveChangesAsync();
        var group = new HubGroup { Name = "G", Slug = "g", OwnerUserId = owner.Id, IsPublic = true };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();
        return (owner.Id, group.Id);
    }

    private static HubGroupMediaInputDto ValidImage(int? trainingId = null) => new()
    {
        MediaType = "image",
        SourceType = "other",
        Url = "https://example.com/photo.jpg",
        Caption = "Caption",
        TrainingId = trainingId,
    };

    [Fact]
    public async Task AddAsync_Valid_PersistsAndReturnsId()
    {
        await using var db = CreateDb(nameof(AddAsync_Valid_PersistsAndReturnsId));
        var (userId, groupId) = await SeedGroupAsync(db);
        var service = new HubGroupMediaService(db);

        var result = await service.AddAsync(groupId, ValidImage(), userId);

        Assert.True(result.Success);
        Assert.True(result.Id > 0);
        var saved = await db.HubGroupMedia.FindAsync(result.Id);
        Assert.NotNull(saved);
        Assert.Equal(groupId, saved!.HubGroupId);
        Assert.Null(saved.TrainingId);
    }

    [Theory]
    [InlineData("poster")]
    [InlineData("")]
    public async Task AddAsync_BadMediaType_Rejected(string mediaType)
    {
        await using var db = CreateDb(nameof(AddAsync_BadMediaType_Rejected) + mediaType);
        var (userId, groupId) = await SeedGroupAsync(db);
        var service = new HubGroupMediaService(db);
        var input = ValidImage();
        input.MediaType = mediaType;

        var result = await service.AddAsync(groupId, input, userId);

        Assert.False(result.Success);
        Assert.Contains("media_type", result.Error);
    }

    [Fact]
    public async Task AddAsync_BadSourceType_Rejected()
    {
        await using var db = CreateDb(nameof(AddAsync_BadSourceType_Rejected));
        var (userId, groupId) = await SeedGroupAsync(db);
        var service = new HubGroupMediaService(db);
        var input = ValidImage();
        input.SourceType = "tiktok";

        var result = await service.AddAsync(groupId, input, userId);

        Assert.False(result.Success);
        Assert.Contains("source_type", result.Error);
    }

    [Theory]
    [InlineData("album", "other")]
    [InlineData("image", "album")]
    public async Task AddAsync_AlbumInvariantViolated_Rejected(string mediaType, string sourceType)
    {
        await using var db = CreateDb(nameof(AddAsync_AlbumInvariantViolated_Rejected) + mediaType + sourceType);
        var (userId, groupId) = await SeedGroupAsync(db);
        var service = new HubGroupMediaService(db);
        var input = ValidImage();
        input.MediaType = mediaType;
        input.SourceType = sourceType;

        var result = await service.AddAsync(groupId, input, userId);

        Assert.False(result.Success);
        Assert.Contains("album", result.Error);
    }

    [Fact]
    public async Task AddAsync_AlbumInvariantSatisfied_Accepted()
    {
        await using var db = CreateDb(nameof(AddAsync_AlbumInvariantSatisfied_Accepted));
        var (userId, groupId) = await SeedGroupAsync(db);
        var service = new HubGroupMediaService(db);
        var input = ValidImage();
        input.MediaType = "album";
        input.SourceType = "album";

        var result = await service.AddAsync(groupId, input, userId);

        Assert.True(result.Success);
    }

    [Theory]
    [InlineData("http://example.com/photo.jpg")]
    [InlineData("ftp://example.com/photo.jpg")]
    [InlineData("")]
    public async Task AddAsync_NonHttpsUrl_Rejected(string url)
    {
        await using var db = CreateDb(nameof(AddAsync_NonHttpsUrl_Rejected) + url.Length);
        var (userId, groupId) = await SeedGroupAsync(db);
        var service = new HubGroupMediaService(db);
        var input = ValidImage();
        input.Url = url;

        var result = await service.AddAsync(groupId, input, userId);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task AddAsync_TrainingIdFromAnotherGroup_Rejected()
    {
        await using var db = CreateDb(nameof(AddAsync_TrainingIdFromAnotherGroup_Rejected));
        var (userId, groupId) = await SeedGroupAsync(db);
        var otherGroup = new HubGroup { Name = "Other", Slug = "other", OwnerUserId = userId, IsPublic = true };
        db.HubGroups.Add(otherGroup);
        await db.SaveChangesAsync();
        var session = new TrainingSession { HubGroupId = otherGroup.Id, ExternalTrainingId = "1", Date = DateTime.UtcNow, PoolType = "25m" };
        db.TrainingSessions.Add(session);
        await db.SaveChangesAsync();
        var service = new HubGroupMediaService(db);

        var result = await service.AddAsync(groupId, ValidImage(session.Id), userId);

        Assert.False(result.Success);
        Assert.Contains("training_id", result.Error);
    }

    [Fact]
    public async Task AddAsync_TrainingIdFromSameGroup_Accepted()
    {
        await using var db = CreateDb(nameof(AddAsync_TrainingIdFromSameGroup_Accepted));
        var (userId, groupId) = await SeedGroupAsync(db);
        var session = new TrainingSession { HubGroupId = groupId, ExternalTrainingId = "1", Date = DateTime.UtcNow, PoolType = "25m" };
        db.TrainingSessions.Add(session);
        await db.SaveChangesAsync();
        var service = new HubGroupMediaService(db);

        var result = await service.AddAsync(groupId, ValidImage(session.Id), userId);

        Assert.True(result.Success);
        var saved = await db.HubGroupMedia.FindAsync(result.Id);
        Assert.Equal(session.Id, saved!.TrainingId);
    }

    [Fact]
    public async Task GetGalleryAsync_ExcludesTrainingMedia()
    {
        await using var db = CreateDb(nameof(GetGalleryAsync_ExcludesTrainingMedia));
        var (userId, groupId) = await SeedGroupAsync(db);
        var session = new TrainingSession { HubGroupId = groupId, ExternalTrainingId = "1", Date = DateTime.UtcNow, PoolType = "25m" };
        db.TrainingSessions.Add(session);
        await db.SaveChangesAsync();
        var service = new HubGroupMediaService(db);
        await service.AddAsync(groupId, ValidImage(), userId); // public gallery item
        await service.AddAsync(groupId, ValidImage(session.Id), userId); // training-only item

        var gallery = await service.GetGalleryAsync(groupId);

        var item = Assert.Single(gallery);
        Assert.Equal("image", item.MediaType);
    }

    [Fact]
    public async Task DeleteAsync_MediaInAnotherGroup_ReturnsFalse()
    {
        await using var db = CreateDb(nameof(DeleteAsync_MediaInAnotherGroup_ReturnsFalse));
        var (userId, groupId) = await SeedGroupAsync(db);
        var otherGroup = new HubGroup { Name = "Other", Slug = "other2", OwnerUserId = userId, IsPublic = true };
        db.HubGroups.Add(otherGroup);
        await db.SaveChangesAsync();
        var service = new HubGroupMediaService(db);
        var added = await service.AddAsync(otherGroup.Id, ValidImage(), userId);

        var removed = await service.DeleteAsync(groupId, added.Id);

        Assert.False(removed);
        Assert.NotNull(await db.HubGroupMedia.FindAsync(added.Id));
    }

    [Fact]
    public async Task DeleteAsync_MediaInGroup_RemovesIt()
    {
        await using var db = CreateDb(nameof(DeleteAsync_MediaInGroup_RemovesIt));
        var (userId, groupId) = await SeedGroupAsync(db);
        var service = new HubGroupMediaService(db);
        var added = await service.AddAsync(groupId, ValidImage(), userId);

        var removed = await service.DeleteAsync(groupId, added.Id);

        Assert.True(removed);
        Assert.Null(await db.HubGroupMedia.FindAsync(added.Id));
    }
}
