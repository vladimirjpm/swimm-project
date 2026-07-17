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

    // ── Тесты: привязка к result_id / competition_id (этап 2A→заплыв) ───────

    private static async Task<Swimmer> SeedSecondSwimmerAsync(SwimmDbContext db)
    {
        var swimmer2 = new Swimmer
        {
            LastName = "Петров", FirstName = "Пётр",
            LastNameEn = "Petrov", FirstNameEn = "Petr",
            BirthYear = 2001
        };
        db.Swimmers.Add(swimmer2);
        await db.SaveChangesAsync();
        return swimmer2;
    }

    private static async Task<Competition> SeedCompetitionAsync(SwimmDbContext db)
    {
        var competition = new Competition { Name = "Test Cup" };
        db.Competitions.Add(competition);
        await db.SaveChangesAsync();
        return competition;
    }

    private static async Task<ResultRecord> SeedResultAsync(SwimmDbContext db, int swimmerId, int competitionId)
    {
        var club = new Club { Name = "TestClub", NameEn = "TestClub" };
        var style = new Style { Name = "freestyle" };
        db.Clubs.Add(club);
        db.Styles.Add(style);
        await db.SaveChangesAsync();

        var result = new ResultRecord
        {
            SwimmerId = swimmerId,
            CompetitionId = competitionId,
            ClubId = club.Id,
            StyleId = style.Id,
            Distance = "50",
            Gender = "male",
            CompetitionDate = DateTime.UtcNow,
            Position = 1,
            TimeMillisecond = 30000
        };
        db.Results.Add(result);
        await db.SaveChangesAsync();
        return result;
    }

    [Fact]
    public async Task AddAsync_ValidResultId_ReturnsResultLevelWithCompetitionIdFromResult()
    {
        await using var db = CreateDb(nameof(AddAsync_ValidResultId_ReturnsResultLevelWithCompetitionIdFromResult));
        var (user, swimmer) = await SeedAsync(db);
        var competition = await SeedCompetitionAsync(db);
        var result = await SeedResultAsync(db, swimmer.Id, competition.Id);
        var repo = new UserMediaRepository(db);

        var request = YoutubeRequest(swimmer.Id);
        request.ResultId = result.Id;

        var dto = await repo.AddAsync(user.Id, request);

        Assert.NotNull(dto);
        Assert.Equal("result", dto!.Level);
        Assert.Equal(result.Id, dto.ResultId);
        Assert.Equal(competition.Id, dto.CompetitionId);
    }

    [Fact]
    public async Task AddAsync_ResultIdBelongsToAnotherSwimmer_ReturnsNull()
    {
        await using var db = CreateDb(nameof(AddAsync_ResultIdBelongsToAnotherSwimmer_ReturnsNull));
        var (user, swimmer) = await SeedAsync(db);
        var otherSwimmer = await SeedSecondSwimmerAsync(db);
        var competition = await SeedCompetitionAsync(db);
        var result = await SeedResultAsync(db, otherSwimmer.Id, competition.Id);
        var repo = new UserMediaRepository(db);

        var request = YoutubeRequest(swimmer.Id);
        request.ResultId = result.Id;

        var dto = await repo.AddAsync(user.Id, request);

        Assert.Null(dto);
    }

    [Fact]
    public async Task AddAsync_UnknownResultId_ReturnsNull()
    {
        await using var db = CreateDb(nameof(AddAsync_UnknownResultId_ReturnsNull));
        var (user, swimmer) = await SeedAsync(db);
        var repo = new UserMediaRepository(db);

        var request = YoutubeRequest(swimmer.Id);
        request.ResultId = 999999;

        var dto = await repo.AddAsync(user.Id, request);

        Assert.Null(dto);
    }

    [Fact]
    public async Task AddAsync_CompetitionIdWithoutResultId_ReturnsCompetitionLevel()
    {
        await using var db = CreateDb(nameof(AddAsync_CompetitionIdWithoutResultId_ReturnsCompetitionLevel));
        var (user, swimmer) = await SeedAsync(db);
        var competition = await SeedCompetitionAsync(db);
        var repo = new UserMediaRepository(db);

        var request = YoutubeRequest(swimmer.Id);
        request.CompetitionId = competition.Id;

        var dto = await repo.AddAsync(user.Id, request);

        Assert.NotNull(dto);
        Assert.Equal("competition", dto!.Level);
        Assert.Equal(competition.Id, dto.CompetitionId);
        Assert.Null(dto.ResultId);
    }

    [Fact]
    public async Task AddAsync_UnknownCompetitionId_ReturnsNull()
    {
        await using var db = CreateDb(nameof(AddAsync_UnknownCompetitionId_ReturnsNull));
        var (user, swimmer) = await SeedAsync(db);
        var repo = new UserMediaRepository(db);

        var request = YoutubeRequest(swimmer.Id);
        request.CompetitionId = 999999;

        var dto = await repo.AddAsync(user.Id, request);

        Assert.Null(dto);
    }

    [Fact]
    public async Task AddAsync_NeitherResultNorCompetitionId_ReturnsSwimmerLevel()
    {
        // Регресс: старое поведение (без привязки) не должно сломаться.
        await using var db = CreateDb(nameof(AddAsync_NeitherResultNorCompetitionId_ReturnsSwimmerLevel));
        var (user, swimmer) = await SeedAsync(db);
        var repo = new UserMediaRepository(db);

        var dto = await repo.AddAsync(user.Id, YoutubeRequest(swimmer.Id));

        Assert.NotNull(dto);
        Assert.Equal("swimmer", dto!.Level);
        Assert.Null(dto.ResultId);
        Assert.Null(dto.CompetitionId);
    }

    [Fact]
    public async Task AddAsync_ResultIdWithClientCompetitionId_IgnoresClientCompetitionId()
    {
        // Решение из задания: при заданном result_id клиентский competition_id игнорируется —
        // сервер сам подставляет CompetitionId из заплыва.
        await using var db = CreateDb(nameof(AddAsync_ResultIdWithClientCompetitionId_IgnoresClientCompetitionId));
        var (user, swimmer) = await SeedAsync(db);
        var competition = await SeedCompetitionAsync(db);
        var otherCompetition = await SeedCompetitionAsync(db);
        var result = await SeedResultAsync(db, swimmer.Id, competition.Id);
        var repo = new UserMediaRepository(db);

        var request = YoutubeRequest(swimmer.Id);
        request.ResultId = result.Id;
        request.CompetitionId = otherCompetition.Id; // «чужой» — должен быть проигнорирован

        var dto = await repo.AddAsync(user.Id, request);

        Assert.NotNull(dto);
        Assert.Equal("result", dto!.Level);
        Assert.Equal(competition.Id, dto.CompetitionId);
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
