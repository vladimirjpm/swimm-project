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

    // ── 2B′: members-слой (тренерские разборы) ───────────────────────────────

    /// <summary>Официальная группа + пловец + заплыв — фикстура для members-тестов.</summary>
    private static async Task<(int userId, int groupId, int swimmerId, long resultId)> SeedOfficialWithResultAsync(
        SwimmDbContext db, bool relay = false)
    {
        var owner = new AppUser { Email = "o@example.com", DisplayName = "O", SecurityStamp = "s" };
        db.AppUsers.Add(owner);
        await db.SaveChangesAsync();
        var group = new HubGroup { Name = "Off", Slug = "off", OwnerUserId = owner.Id, IsPublic = true, IsOfficial = true };
        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "C", NameEn = "C" };
        var comp = new Competition { Name = "Meet", Date = "01/01/2026", PoolType = "50m" };
        var swimmer = new Swimmer { LastName = "Иванов", FirstName = "Иван", LastNameEn = "Ivanov", FirstNameEn = "Ivan", BirthYear = 2005 };
        Relay? rel = relay ? new Relay { TeamName = "T" } : null;
        db.AddRange(group, style, club, comp, swimmer);
        if (rel != null) db.Add(rel);
        await db.SaveChangesAsync();
        var result = new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            RelayId = rel?.Id, Distance = "100", Gender = "male",
            CompetitionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeOriginal = "1:00.00", AgeGroup = "Open", EventStyleAge = "100 freestyle Open"
        };
        db.Results.Add(result);
        await db.SaveChangesAsync();
        return (owner.Id, group.Id, swimmer.Id, result.Id);
    }

    private static HubGroupMediaInputDto MembersVideo(int? swimmerId = null, long? resultId = null) => new()
    {
        MediaType = "video",
        SourceType = "youtube",
        Url = "https://www.youtube.com/watch?v=abc123",
        Visibility = "members",
        SwimmerId = swimmerId,
        ResultId = resultId,
    };

    [Fact]
    public async Task AddAsync_MembersInUnofficialGroup_Rejected()
    {
        await using var db = CreateDb(nameof(AddAsync_MembersInUnofficialGroup_Rejected));
        var (userId, groupId) = await SeedGroupAsync(db); // IsOfficial = false
        var service = new HubGroupMediaService(db);

        var result = await service.AddAsync(groupId, MembersVideo(), userId);

        Assert.False(result.Success);
        Assert.Contains("official", result.Error);
    }

    [Fact]
    public async Task AddAsync_PublicWithAnchor_Rejected()
    {
        await using var db = CreateDb(nameof(AddAsync_PublicWithAnchor_Rejected));
        var (userId, groupId, swimmerId, _) = await SeedOfficialWithResultAsync(db);
        var service = new HubGroupMediaService(db);
        var input = ValidImage();
        input.SwimmerId = swimmerId; // якорь при public — запрещено

        var result = await service.AddAsync(groupId, input, userId);

        Assert.False(result.Success);
        Assert.Contains("members", result.Error);
    }

    [Fact]
    public async Task AddAsync_MembersWithResultAnchor_DenormalizesSwimmer()
    {
        await using var db = CreateDb(nameof(AddAsync_MembersWithResultAnchor_DenormalizesSwimmer));
        var (userId, groupId, swimmerId, resultId) = await SeedOfficialWithResultAsync(db);
        var service = new HubGroupMediaService(db);

        var result = await service.AddAsync(groupId, MembersVideo(resultId: resultId), userId);

        Assert.True(result.Success);
        var entity = await db.HubGroupMedia.FindAsync(result.Id);
        Assert.NotNull(entity);
        Assert.Equal(HubGroupMediaVisibility.Members, entity!.Visibility);
        Assert.Equal(resultId, entity.ResultId);
        Assert.Equal(swimmerId, entity.SwimmerId); // выведен из заплыва, не из входа
    }

    [Fact]
    public async Task AddAsync_MembersWithRelayResult_Rejected()
    {
        await using var db = CreateDb(nameof(AddAsync_MembersWithRelayResult_Rejected));
        var (userId, groupId, _, resultId) = await SeedOfficialWithResultAsync(db, relay: true);
        var service = new HubGroupMediaService(db);

        var result = await service.AddAsync(groupId, MembersVideo(resultId: resultId), userId);

        Assert.False(result.Success);
        Assert.Contains("relay", result.Error);
    }

    [Fact]
    public async Task AddAsync_MembersWithUnknownSwimmer_Rejected()
    {
        await using var db = CreateDb(nameof(AddAsync_MembersWithUnknownSwimmer_Rejected));
        var (userId, groupId, _, _) = await SeedOfficialWithResultAsync(db);
        var service = new HubGroupMediaService(db);

        var result = await service.AddAsync(groupId, MembersVideo(swimmerId: 99999), userId);

        Assert.False(result.Success);
        Assert.Contains("swimmer_id", result.Error);
    }

    [Fact]
    public async Task GetGalleryAsync_ExcludesMembersMedia()
    {
        await using var db = CreateDb(nameof(GetGalleryAsync_ExcludesMembersMedia));
        var (userId, groupId, _, _) = await SeedOfficialWithResultAsync(db);
        var service = new HubGroupMediaService(db);
        await service.AddAsync(groupId, ValidImage(), userId);           // public — в галерее
        await service.AddAsync(groupId, MembersVideo(), userId);          // members — нет

        var gallery = await service.GetGalleryAsync(groupId);

        Assert.Single(gallery);
        Assert.Equal("image", gallery[0].MediaType);
    }

    [Fact]
    public async Task GetMembersMediaAsync_ReturnsOnlyMembersWithAnchorContext()
    {
        await using var db = CreateDb(nameof(GetMembersMediaAsync_ReturnsOnlyMembersWithAnchorContext));
        var (userId, groupId, swimmerId, resultId) = await SeedOfficialWithResultAsync(db);
        var service = new HubGroupMediaService(db);
        await service.AddAsync(groupId, ValidImage(), userId);                       // public — не попадёт
        await service.AddAsync(groupId, MembersVideo(resultId: resultId), userId);   // разбор заплыва

        var media = await service.GetMembersMediaAsync(groupId);

        var item = Assert.Single(media);
        Assert.Equal(swimmerId, item.SwimmerId);
        Assert.Equal("Иванов Иван", item.SwimmerName);
        Assert.Equal(resultId, item.ResultId);
        Assert.Contains("freestyle 100", item.ResultLabel);
        Assert.Contains("Meet", item.ResultLabel);
    }

    [Fact]
    public async Task AddAsync_TrainingMedia_IgnoresVisibilityInput()
    {
        await using var db = CreateDb(nameof(AddAsync_TrainingMedia_IgnoresVisibilityInput));
        var (userId, groupId) = await SeedGroupAsync(db);
        var training = new TrainingSession { HubGroupId = groupId, ExternalTrainingId = "1", Date = DateTime.UtcNow, PoolType = "25m" };
        db.TrainingSessions.Add(training);
        await db.SaveChangesAsync();
        var service = new HubGroupMediaService(db);
        var input = ValidImage(training.Id);
        input.Visibility = "members"; // для медиа тренировки поле игнорируется

        var result = await service.AddAsync(groupId, input, userId);

        Assert.True(result.Success);
        var entity = await db.HubGroupMedia.FindAsync(result.Id);
        Assert.Equal(HubGroupMediaVisibility.Public, entity!.Visibility);
    }
}
