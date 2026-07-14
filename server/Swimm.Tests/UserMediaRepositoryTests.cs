using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты репозитория UserMediaRepository (2A — личное owner-only медиа).
/// EF InMemory — по образцу UserFavoriteRepositoryTests.
/// </summary>
public class UserMediaRepositoryTests
{
    private static DbContextOptions<SwimmDbContext> BuildOptions(string name) =>
        new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static SwimmDbContext CreateDb(string name) =>
        new SwimmDbContext(BuildOptions(name));

    private static async Task<(AppUser user, Swimmer swimmer)> SeedAsync(SwimmDbContext db)
    {
        var swimmer = new Swimmer
        {
            LastName = "Иванов", FirstName = "Иван",
            LastNameEn = "Ivanov", FirstNameEn = "Ivan",
            BirthYear = 2000
        };
        var user = new AppUser
        {
            Email = "test@example.com",
            DisplayName = "Test",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
        db.Swimmers.Add(swimmer);
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        return (user, swimmer);
    }

    private static AddUserMediaRequest YoutubeRequest(int swimmerId) => new()
    {
        SwimmerId = swimmerId,
        MediaType = "video",
        SourceType = "youtube",
        Url = "https://www.youtube.com/watch?v=abc123"
    };

    // ── Тест: пустой набор — не падать ────────────────────────────────────────

    [Fact]
    public async Task GetForUser_EmptySet_ReturnsEmptyList()
    {
        await using var db = CreateDb(nameof(GetForUser_EmptySet_ReturnsEmptyList));
        var repo = new UserMediaRepository(db);

        var result = await repo.GetForUserAsync(999);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    // ── Тест: добавление ────────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_ValidSwimmer_ReturnsDtoWithPrivateVisibilityAndSwimmerLevel()
    {
        await using var db = CreateDb(nameof(AddAsync_ValidSwimmer_ReturnsDtoWithPrivateVisibilityAndSwimmerLevel));
        var (user, swimmer) = await SeedAsync(db);
        var repo = new UserMediaRepository(db);

        var dto = await repo.AddAsync(user.Id, YoutubeRequest(swimmer.Id));

        Assert.NotNull(dto);
        Assert.Equal(swimmer.Id, dto!.SwimmerId);
        Assert.Equal("swimmer", dto.Level);
        Assert.Equal("video", dto.MediaType);
        Assert.Equal("youtube", dto.SourceType);

        // Visibility в entity должна остаться "private" (проверяем через прямой запрос к БД).
        var stored = await db.UserMedia.AsNoTracking().FirstAsync(m => m.Id == dto.Id);
        Assert.Equal("private", stored.Visibility);
    }

    // ── Тест: несуществующий SwimmerId → null ───────────────────────────────

    [Fact]
    public async Task AddAsync_UnknownSwimmerId_ReturnsNull()
    {
        await using var db = CreateDb(nameof(AddAsync_UnknownSwimmerId_ReturnsNull));
        var (user, _) = await SeedAsync(db);
        var repo = new UserMediaRepository(db);

        var dto = await repo.AddAsync(user.Id, YoutubeRequest(999999));

        Assert.Null(dto);
    }

    // ── Тест: IDOR — чужое медиа не приходит в GetForUser ───────────────────

    [Fact]
    public async Task GetForUser_OnlyReturnsOwnMedia()
    {
        await using var db = CreateDb(nameof(GetForUser_OnlyReturnsOwnMedia));
        var (user1, swimmer) = await SeedAsync(db);
        var user2 = new AppUser { Email = "other@example.com", DisplayName = "Other", SecurityStamp = Guid.NewGuid().ToString("N") };
        db.AppUsers.Add(user2);
        await db.SaveChangesAsync();

        var repo = new UserMediaRepository(db);
        await repo.AddAsync(user1.Id, YoutubeRequest(swimmer.Id));
        await repo.AddAsync(user2.Id, YoutubeRequest(swimmer.Id));

        var user1Media = await repo.GetForUserAsync(user1.Id);
        var user2Media = await repo.GetForUserAsync(user2.Id);

        Assert.Single(user1Media);
        Assert.Single(user2Media);
        Assert.NotEqual(user1Media[0].Id, user2Media[0].Id);
    }

    // ── Тест: IDOR — RemoveAsync чужого → false ──────────────────────────────

    [Fact]
    public async Task RemoveAsync_WrongUserId_ReturnsFalse()
    {
        await using var db = CreateDb(nameof(RemoveAsync_WrongUserId_ReturnsFalse));
        var (user, swimmer) = await SeedAsync(db);
        var repo = new UserMediaRepository(db);

        var media = await repo.AddAsync(user.Id, YoutubeRequest(swimmer.Id));
        Assert.NotNull(media);

        var ok = await repo.RemoveAsync(user.Id + 999, media!.Id);

        Assert.False(ok);
        Assert.Single(await repo.GetForUserAsync(user.Id));
    }

    // ── Тест: корректный RemoveAsync удаляет запись ──────────────────────────

    [Fact]
    public async Task RemoveAsync_OwnMedia_ReturnsTrue()
    {
        await using var db = CreateDb(nameof(RemoveAsync_OwnMedia_ReturnsTrue));
        var (user, swimmer) = await SeedAsync(db);
        var repo = new UserMediaRepository(db);

        var media = await repo.AddAsync(user.Id, YoutubeRequest(swimmer.Id));
        Assert.NotNull(media);

        var ok = await repo.RemoveAsync(user.Id, media!.Id);

        Assert.True(ok);
        Assert.Empty(await repo.GetForUserAsync(user.Id));
    }

    // ── Тест: фильтр swimmerId в GetForUserAsync ─────────────────────────────

    [Fact]
    public async Task GetForUser_FilterBySwimmerId_ReturnsOnlyMatching()
    {
        await using var db = CreateDb(nameof(GetForUser_FilterBySwimmerId_ReturnsOnlyMatching));
        var (user, swimmer1) = await SeedAsync(db);
        var swimmer2 = new Swimmer
        {
            LastName = "Петров", FirstName = "Пётр",
            LastNameEn = "Petrov", FirstNameEn = "Petr",
            BirthYear = 2001
        };
        db.Swimmers.Add(swimmer2);
        await db.SaveChangesAsync();

        var repo = new UserMediaRepository(db);
        await repo.AddAsync(user.Id, YoutubeRequest(swimmer1.Id));
        await repo.AddAsync(user.Id, YoutubeRequest(swimmer2.Id));

        var forSwimmer1 = await repo.GetForUserAsync(user.Id, swimmer1.Id);
        var all = await repo.GetForUserAsync(user.Id);

        Assert.Single(forSwimmer1);
        Assert.Equal(swimmer1.Id, forSwimmer1[0].SwimmerId);
        Assert.Equal(2, all.Count);
    }

    // ── Тест: каскад — удаление Swimmer удаляет его UserMedia ───────────────

    [Fact]
    public async Task CascadeDelete_SwimmerDeleted_MediaRemoved()
    {
        await using var db = CreateDb(nameof(CascadeDelete_SwimmerDeleted_MediaRemoved));
        var (user, swimmer) = await SeedAsync(db);
        var repo = new UserMediaRepository(db);

        await repo.AddAsync(user.Id, YoutubeRequest(swimmer.Id));

        // Загружаем медиа в change tracker, чтобы EF InMemory мог применить каскад.
        await db.UserMedia.Where(m => m.SwimmerId == swimmer.Id).LoadAsync();

        var trackedSwimmer = await db.Swimmers.FindAsync(swimmer.Id);
        db.Swimmers.Remove(trackedSwimmer!);
        await db.SaveChangesAsync();

        Assert.Empty(await repo.GetForUserAsync(user.Id));
    }
}
