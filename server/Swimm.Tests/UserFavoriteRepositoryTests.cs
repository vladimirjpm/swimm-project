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
/// Тесты репозитория UserFavoriteRepository.
/// Используется EF InMemory — partial unique indexes Postgres не эмулируются;
/// поведение при нарушении уникального ограничения тестируется через FaultyDbContext.
/// Транзакции InMemory трактует как no-op (предупреждение заглушено явно).
/// </summary>
public class UserFavoriteRepositoryTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static DbContextOptions<SwimmDbContext> BuildOptions(string name) =>
        new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            // InMemory не поддерживает транзакции (они трактуются как no-op);
            // заглушаем предупреждение, чтобы тест не падал на TransactionIgnoredWarning.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static SwimmDbContext CreateDb(string name) =>
        new SwimmDbContext(BuildOptions(name));

    /// <summary>Настоящий сервис настроек: лимиты проверяются тем же путём, что в /Admin/Settings.</summary>
    private static AdminSettingsService Settings(int? maxSwimmers = null, int? maxClubs = null)
    {
        var settings = new AdminSettingsService(new MemoryCache(Options.Create(new MemoryCacheOptions())));
        if (maxSwimmers is int s) Assert.True(settings.Update(FavoritesRules.MaxSwimmersKey, s.ToString()));
        if (maxClubs is int c) Assert.True(settings.Update(FavoritesRules.MaxClubsKey, c.ToString()));
        return settings;
    }

    private static UserFavoriteRepository Repo(SwimmDbContext db, ISettingsService? settings = null) =>
        new(db, settings ?? Settings());

    private static AddFavoriteRequest SwimmerReq(int id) => new() { TargetType = "swimmer", SwimmerId = id };
    private static AddFavoriteRequest ClubReq(int id) => new() { TargetType = "club", ClubId = id };

    private static async Task<List<Swimmer>> AddSwimmersAsync(SwimmDbContext db, int count)
    {
        var list = Enumerable.Range(1, count)
            .Select(i => new Swimmer { LastName = $"L{i}", FirstName = $"F{i}", LastNameEn = $"L{i}", FirstNameEn = $"F{i}", BirthYear = 2010 })
            .ToList();
        db.Swimmers.AddRange(list);
        await db.SaveChangesAsync();
        return list;
    }

    private static async Task<List<Club>> AddClubsAsync(SwimmDbContext db, int count)
    {
        var list = Enumerable.Range(1, count).Select(i => new Club { Name = $"C{i}", NameEn = $"C{i}" }).ToList();
        db.Clubs.AddRange(list);
        await db.SaveChangesAsync();
        return list;
    }

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

    /// <summary>Добавить и убедиться, что добавилось, — для тестов, где добавление лишь подготовка.</summary>
    private static async Task<FavoriteDto> AddOkAsync(UserFavoriteRepository repo, int userId, AddFavoriteRequest request)
    {
        var result = await repo.AddAsync(userId, request);
        Assert.Equal(AddFavoriteStatus.Added, result.Status);
        return result.Favorite!;
    }

    // ── Тест: пустой набор фаворитов — не падать ─────────────────────────────

    [Fact]
    public async Task GetForUser_EmptySet_ReturnsEmptyList()
    {
        await using var db = CreateDb(nameof(GetForUser_EmptySet_ReturnsEmptyList));
        var repo = Repo(db);

        var result = await repo.GetForUserAsync(999);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    // ── Тест: добавление фаворита ─────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_ValidSwimmer_ReturnsFavoriteDto()
    {
        await using var db = CreateDb(nameof(AddAsync_ValidSwimmer_ReturnsFavoriteDto));
        var (user, swimmer) = await SeedAsync(db);
        var repo = Repo(db);

        var result = await repo.AddAsync(user.Id, SwimmerReq(swimmer.Id));

        Assert.Equal(AddFavoriteStatus.Added, result.Status);
        var dto = result.Favorite!;
        Assert.Equal("swimmer", dto.TargetType);
        Assert.Equal(swimmer.Id, dto.SwimmerId);
        // Имя — иврит по умолчанию (здесь «иврит» — первые поля), тем же правилом, что в списке.
        Assert.Equal("Иванов Иван", dto.SwimmerName);
        Assert.False(dto.IsPrimary);
    }

    // ── Тест: дубль-фаворит — DbUpdateException = Duplicate (409) ─────────────

    [Fact]
    public async Task AddAsync_DbUpdateException_IsDuplicate()
    {
        await using var db = new FaultyDbContext(BuildOptions(nameof(AddAsync_DbUpdateException_IsDuplicate)));
        var (user, swimmer) = await SeedAsync(db);
        db.ThrowOnNextSave();
        var repo = Repo(db);

        var result = await repo.AddAsync(user.Id, SwimmerReq(swimmer.Id));

        Assert.Equal(AddFavoriteStatus.Duplicate, result.Status);
        Assert.Null(result.Favorite);
    }

    [Fact]
    public async Task AddAsync_SameSwimmerTwice_IsDuplicate()
    {
        // Явная проверка дубля (unique-индексы Postgres InMemory не эмулирует).
        await using var db = CreateDb(nameof(AddAsync_SameSwimmerTwice_IsDuplicate));
        var (user, swimmer) = await SeedAsync(db);
        var repo = Repo(db);

        await AddOkAsync(repo, user.Id, SwimmerReq(swimmer.Id));
        var again = await repo.AddAsync(user.Id, SwimmerReq(swimmer.Id));

        Assert.Equal(AddFavoriteStatus.Duplicate, again.Status);
        Assert.Single(await repo.GetForUserAsync(user.Id));
    }

    // ── Лимит избранного (решение 10.09.2026, FavoritesRules) ────────────────

    [Fact]
    public async Task AddAsync_SwimmerOverLimit_Refused_WithLimitAndHint()
    {
        await using var db = CreateDb(nameof(AddAsync_SwimmerOverLimit_Refused_WithLimitAndHint));
        var (user, _) = await SeedAsync(db);
        var swimmers = await AddSwimmersAsync(db, 3);
        var repo = Repo(db, Settings(maxSwimmers: 2));

        await AddOkAsync(repo, user.Id, SwimmerReq(swimmers[0].Id));
        await AddOkAsync(repo, user.Id, SwimmerReq(swimmers[1].Id));
        var third = await repo.AddAsync(user.Id, SwimmerReq(swimmers[2].Id));

        Assert.Equal(AddFavoriteStatus.LimitReached, third.Status);
        Assert.Equal(2, third.Limit);
        Assert.Equal("Up to 2 swimmers — use a group for bigger lists.", third.Message);
        Assert.Equal(2, (await repo.GetForUserAsync(user.Id)).Count);
    }

    [Fact]
    public async Task AddAsync_LimitsAreSeparatePerType()
    {
        // Пловцы клубам места не занимают и наоборот: у каждого типа свой счёт.
        await using var db = CreateDb(nameof(AddAsync_LimitsAreSeparatePerType));
        var (user, swimmer) = await SeedAsync(db);
        var clubs = await AddClubsAsync(db, 2);
        var repo = Repo(db, Settings(maxSwimmers: 1, maxClubs: 1));

        await AddOkAsync(repo, user.Id, SwimmerReq(swimmer.Id));
        await AddOkAsync(repo, user.Id, ClubReq(clubs[0].Id));
        var secondClub = await repo.AddAsync(user.Id, ClubReq(clubs[1].Id));

        Assert.Equal(AddFavoriteStatus.LimitReached, secondClub.Status);
        Assert.Equal("Up to 1 club.", secondClub.Message);
    }

    [Fact]
    public async Task AddAsync_PrimaryCountsAsSwimmer()
    {
        // «Это я» — такой же пловец в избранном: звезда не выносит его за пределы лимита.
        await using var db = CreateDb(nameof(AddAsync_PrimaryCountsAsSwimmer));
        var (user, _) = await SeedAsync(db);
        var swimmers = await AddSwimmersAsync(db, 3);
        var repo = Repo(db, Settings(maxSwimmers: 2));

        var me = await AddOkAsync(repo, user.Id, SwimmerReq(swimmers[0].Id));
        Assert.True(await repo.SetPrimaryAsync(user.Id, me.Id));
        await AddOkAsync(repo, user.Id, SwimmerReq(swimmers[1].Id));

        var third = await repo.AddAsync(user.Id, SwimmerReq(swimmers[2].Id));

        Assert.Equal(AddFavoriteStatus.LimitReached, third.Status);
    }

    [Fact]
    public async Task AddAsync_AboveLimit_KeepsEverything_OnlyCannotAdd()
    {
        // Лимит снизили ниже уже набранного: старое не теряется, новое не добавляется.
        await using var db = CreateDb(nameof(AddAsync_AboveLimit_KeepsEverything_OnlyCannotAdd));
        var (user, _) = await SeedAsync(db);
        var swimmers = await AddSwimmersAsync(db, 4);
        var settings = Settings(maxSwimmers: 5);
        var repo = Repo(db, settings);
        for (var i = 0; i < 3; i++)
            await AddOkAsync(repo, user.Id, SwimmerReq(swimmers[i].Id));

        Assert.True(settings.Update(FavoritesRules.MaxSwimmersKey, "2"));
        var next = await repo.AddAsync(user.Id, SwimmerReq(swimmers[3].Id));

        Assert.Equal(AddFavoriteStatus.LimitReached, next.Status);
        Assert.Equal(3, (await repo.GetForUserAsync(user.Id)).Count);
    }

    [Fact]
    public async Task AddAsync_DuplicateAtLimit_IsStillDuplicate()
    {
        // Повторный клик по уже горящему сердечку на пределе — «уже в избранном» (409), а не
        // «нет места»: иначе клиент погасил бы сердечко, которое горит.
        await using var db = CreateDb(nameof(AddAsync_DuplicateAtLimit_IsStillDuplicate));
        var (user, swimmer) = await SeedAsync(db);
        var repo = Repo(db, Settings(maxSwimmers: 1));

        await AddOkAsync(repo, user.Id, SwimmerReq(swimmer.Id));
        var again = await repo.AddAsync(user.Id, SwimmerReq(swimmer.Id));

        Assert.Equal(AddFavoriteStatus.Duplicate, again.Status);
    }

    [Fact]
    public async Task AddAsync_LimitFromSettings_AppliesWithoutRestart()
    {
        // Приёмка П1: лимит, поменянный в /Admin/Settings, действует без правки кода.
        await using var db = CreateDb(nameof(AddAsync_LimitFromSettings_AppliesWithoutRestart));
        var (user, _) = await SeedAsync(db);
        var clubs = await AddClubsAsync(db, 5);
        var settings = Settings();
        var repo = Repo(db, settings);

        for (var i = 0; i < FavoritesRules.DefaultMaxClubs; i++)
            await AddOkAsync(repo, user.Id, ClubReq(clubs[i].Id));
        Assert.Equal(AddFavoriteStatus.LimitReached, (await repo.AddAsync(user.Id, ClubReq(clubs[3].Id))).Status);

        Assert.True(settings.Update(FavoritesRules.MaxClubsKey, "4"));

        await AddOkAsync(repo, user.Id, ClubReq(clubs[3].Id));
        var fifth = await repo.AddAsync(user.Id, ClubReq(clubs[4].Id));
        Assert.Equal(AddFavoriteStatus.LimitReached, fifth.Status);
        Assert.Equal(4, fifth.Limit);
    }

    [Fact]
    public async Task AddAsync_LimitIsPerUser()
    {
        await using var db = CreateDb(nameof(AddAsync_LimitIsPerUser));
        var (user, swimmer) = await SeedAsync(db);
        var other = new AppUser { Email = "other@example.com", DisplayName = "Other", SecurityStamp = "s" };
        db.AppUsers.Add(other);
        await db.SaveChangesAsync();
        var repo = Repo(db, Settings(maxSwimmers: 1));

        await AddOkAsync(repo, user.Id, SwimmerReq(swimmer.Id));
        await AddOkAsync(repo, other.Id, SwimmerReq(swimmer.Id));
    }

    // ── FavoritesRules: тексты и лимиты ───────────────────────────────────────

    [Theory]
    [InlineData("swimmer", 30, "Up to 30 swimmers — use a group for bigger lists.")]
    [InlineData("swimmer", 1, "Up to 1 swimmer — use a group for bigger lists.")]
    [InlineData("club", 3, "Up to 3 clubs.")]
    [InlineData("club", 1, "Up to 1 club.")]
    public void FullHint_EnglishWithPlural(string targetType, int limit, string expected)
    {
        Assert.Equal(expected, FavoritesRules.FullHint(targetType, limit));
    }

    [Fact]
    public void LimitFor_Defaults30And3()
    {
        var settings = Settings();

        Assert.Equal(30, FavoritesRules.LimitFor(settings, "swimmer"));
        Assert.Equal(3, FavoritesRules.LimitFor(settings, "club"));
    }

    // ── Тест: IDOR — нельзя удалить чужой фаворит ────────────────────────────

    [Fact]
    public async Task RemoveAsync_WrongUserId_ReturnsFalse()
    {
        await using var db = CreateDb(nameof(RemoveAsync_WrongUserId_ReturnsFalse));
        var (user, swimmer) = await SeedAsync(db);
        var repo = Repo(db);

        var fav = await AddOkAsync(repo, user.Id, SwimmerReq(swimmer.Id));

        var ok = await repo.RemoveAsync(user.Id + 999, fav.Id);

        Assert.False(ok);
        Assert.Single(await repo.GetForUserAsync(user.Id));
    }

    // ── Тест: корректный RemoveAsync удаляет запись ───────────────────────────

    [Fact]
    public async Task RemoveAsync_OwnFavorite_ReturnsTrue()
    {
        await using var db = CreateDb(nameof(RemoveAsync_OwnFavorite_ReturnsTrue));
        var (user, swimmer) = await SeedAsync(db);
        var repo = Repo(db);

        var fav = await AddOkAsync(repo, user.Id, SwimmerReq(swimmer.Id));

        var ok = await repo.RemoveAsync(user.Id, fav.Id);

        Assert.True(ok);
        Assert.Empty(await repo.GetForUserAsync(user.Id));
    }

    // ── Тест: swap primary — сбрасывает старый, ставит новый ─────────────────

    [Fact]
    public async Task SetPrimaryAsync_Swap_ClearsOldSetsNew()
    {
        await using var db = CreateDb(nameof(SetPrimaryAsync_Swap_ClearsOldSetsNew));
        var (user, swimmer1) = await SeedAsync(db);

        var swimmer2 = new Swimmer
        {
            LastName = "Петров", FirstName = "Пётр",
            LastNameEn = "Petrov", FirstNameEn = "Petr",
            BirthYear = 2001
        };
        db.Swimmers.Add(swimmer2);
        await db.SaveChangesAsync();

        var repo = Repo(db);

        var fav1 = await AddOkAsync(repo, user.Id, SwimmerReq(swimmer1.Id));
        var fav2 = await AddOkAsync(repo, user.Id, SwimmerReq(swimmer2.Id));

        // Ставим primary на первый
        await repo.SetPrimaryAsync(user.Id, fav1.Id);
        var listAfterFirst = await repo.GetForUserAsync(user.Id);
        Assert.True(listAfterFirst.First(f => f.Id == fav1.Id).IsPrimary);
        Assert.False(listAfterFirst.First(f => f.Id == fav2.Id).IsPrimary);

        // Swap primary на второй
        await repo.SetPrimaryAsync(user.Id, fav2.Id);
        var listAfterSwap = await repo.GetForUserAsync(user.Id);
        Assert.False(listAfterSwap.First(f => f.Id == fav1.Id).IsPrimary);
        Assert.True(listAfterSwap.First(f => f.Id == fav2.Id).IsPrimary);
    }

    // ── Тест: гонка SetPrimary — DbUpdateException трактуется как no-op ──────

    [Fact]
    public async Task SetPrimaryAsync_DbUpdateException_ReturnsTrue()
    {
        var opts = BuildOptions(nameof(SetPrimaryAsync_DbUpdateException_ReturnsTrue));
        await using var db = new FaultyDbContext(opts);
        var (user, swimmer) = await SeedAsync(db);

        // Добавляем фаворит нормально (без исключения)
        var fav = new UserFavorite
        {
            UserId = user.Id, SwimmerId = swimmer.Id, TargetType = "swimmer",
            IsPrimary = false, SortOrder = 0, CreatedAt = DateTime.UtcNow
        };
        db.UserFavorites.Add(fav);
        await db.SaveChangesAsync();

        // Теперь следующий SaveChanges выбросит DbUpdateException (симуляция гонки)
        db.ThrowOnNextSave();
        var repo = Repo(db);
        var ok = await repo.SetPrimaryAsync(user.Id, fav.Id);

        Assert.True(ok); // гонка — no-op, не ошибка
    }

    // ── Тест: cascade-delete — удаление пловца убирает фавориты ──────────────

    [Fact]
    public async Task CascadeDelete_SwimmerDeleted_FavoritesRemoved()
    {
        await using var db = CreateDb(nameof(CascadeDelete_SwimmerDeleted_FavoritesRemoved));
        var (user, swimmer) = await SeedAsync(db);
        var repo = Repo(db);

        await AddOkAsync(repo, user.Id, SwimmerReq(swimmer.Id));

        // Загружаем фавориты в change tracker, чтобы EF InMemory мог применить каскад.
        await db.UserFavorites.Where(f => f.SwimmerId == swimmer.Id).LoadAsync();

        var trackedSwimmer = await db.Swimmers.FindAsync(swimmer.Id);
        db.Swimmers.Remove(trackedSwimmer!);
        await db.SaveChangesAsync();

        Assert.Empty(await repo.GetForUserAsync(user.Id));
    }

    // ── Тест: SetPrimary возвращает false для чужого фаворита (IDOR) ─────────

    [Fact]
    public async Task SetPrimaryAsync_WrongUserId_ReturnsFalse()
    {
        await using var db = CreateDb(nameof(SetPrimaryAsync_WrongUserId_ReturnsFalse));
        var (user, swimmer) = await SeedAsync(db);
        var repo = Repo(db);

        var fav = await AddOkAsync(repo, user.Id, SwimmerReq(swimmer.Id));

        var ok = await repo.SetPrimaryAsync(user.Id + 999, fav.Id);

        Assert.False(ok);
    }

    // ── FaultyDbContext: пробрасывает DbUpdateException по требованию ─────────

    private class FaultyDbContext : SwimmDbContext
    {
        private bool _shouldThrow;

        public FaultyDbContext(DbContextOptions<SwimmDbContext> options) : base(options) { }

        public void ThrowOnNextSave() => _shouldThrow = true;

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (_shouldThrow)
            {
                _shouldThrow = false;
                throw new DbUpdateException("Simulated unique constraint violation",
                    new Exception("unique_violation"));
            }
            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}
