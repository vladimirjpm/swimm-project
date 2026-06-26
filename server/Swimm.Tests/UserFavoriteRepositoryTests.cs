using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
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

    // ── Тест: пустой набор фаворитов — не падать ─────────────────────────────

    [Fact]
    public async Task GetForUser_EmptySet_ReturnsEmptyList()
    {
        await using var db = CreateDb(nameof(GetForUser_EmptySet_ReturnsEmptyList));
        var repo = new UserFavoriteRepository(db);

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
        var repo = new UserFavoriteRepository(db);

        var dto = await repo.AddAsync(user.Id, new AddFavoriteRequest
        {
            TargetType = "swimmer",
            SwimmerId = swimmer.Id
        });

        Assert.NotNull(dto);
        Assert.Equal("swimmer", dto!.TargetType);
        Assert.Equal(swimmer.Id, dto.SwimmerId);
        Assert.False(dto.IsPrimary);
    }

    // ── Тест: дубль-фаворит — AddAsync возвращает null при DbUpdateException ─

    [Fact]
    public async Task AddAsync_DbUpdateException_ReturnsNull()
    {
        await using var db = new FaultyDbContext(BuildOptions(nameof(AddAsync_DbUpdateException_ReturnsNull)));
        var (user, swimmer) = await SeedAsync(db);
        db.ThrowOnNextSave();
        var repo = new UserFavoriteRepository(db);

        var dto = await repo.AddAsync(user.Id, new AddFavoriteRequest
        {
            TargetType = "swimmer",
            SwimmerId = swimmer.Id
        });

        Assert.Null(dto);
    }

    // ── Тест: IDOR — нельзя удалить чужой фаворит ────────────────────────────

    [Fact]
    public async Task RemoveAsync_WrongUserId_ReturnsFalse()
    {
        await using var db = CreateDb(nameof(RemoveAsync_WrongUserId_ReturnsFalse));
        var (user, swimmer) = await SeedAsync(db);
        var repo = new UserFavoriteRepository(db);

        var fav = await repo.AddAsync(user.Id, new AddFavoriteRequest
        {
            TargetType = "swimmer",
            SwimmerId = swimmer.Id
        });
        Assert.NotNull(fav);

        var ok = await repo.RemoveAsync(user.Id + 999, fav!.Id);

        Assert.False(ok);
        Assert.Single(await repo.GetForUserAsync(user.Id));
    }

    // ── Тест: корректный RemoveAsync удаляет запись ───────────────────────────

    [Fact]
    public async Task RemoveAsync_OwnFavorite_ReturnsTrue()
    {
        await using var db = CreateDb(nameof(RemoveAsync_OwnFavorite_ReturnsTrue));
        var (user, swimmer) = await SeedAsync(db);
        var repo = new UserFavoriteRepository(db);

        var fav = await repo.AddAsync(user.Id, new AddFavoriteRequest
        {
            TargetType = "swimmer",
            SwimmerId = swimmer.Id
        });
        Assert.NotNull(fav);

        var ok = await repo.RemoveAsync(user.Id, fav!.Id);

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

        var repo = new UserFavoriteRepository(db);

        var fav1 = await repo.AddAsync(user.Id, new AddFavoriteRequest { TargetType = "swimmer", SwimmerId = swimmer1.Id });
        var fav2 = await repo.AddAsync(user.Id, new AddFavoriteRequest { TargetType = "swimmer", SwimmerId = swimmer2.Id });
        Assert.NotNull(fav1);
        Assert.NotNull(fav2);

        // Ставим primary на первый
        await repo.SetPrimaryAsync(user.Id, fav1!.Id);
        var listAfterFirst = await repo.GetForUserAsync(user.Id);
        Assert.True(listAfterFirst.First(f => f.Id == fav1.Id).IsPrimary);
        Assert.False(listAfterFirst.First(f => f.Id == fav2!.Id).IsPrimary);

        // Swap primary на второй
        await repo.SetPrimaryAsync(user.Id, fav2!.Id);
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
        var repo = new UserFavoriteRepository(db);
        var ok = await repo.SetPrimaryAsync(user.Id, fav.Id);

        Assert.True(ok); // гонка — no-op, не ошибка
    }

    // ── Тест: cascade-delete — удаление пловца убирает фавориты ──────────────

    [Fact]
    public async Task CascadeDelete_SwimmerDeleted_FavoritesRemoved()
    {
        await using var db = CreateDb(nameof(CascadeDelete_SwimmerDeleted_FavoritesRemoved));
        var (user, swimmer) = await SeedAsync(db);
        var repo = new UserFavoriteRepository(db);

        await repo.AddAsync(user.Id, new AddFavoriteRequest
        {
            TargetType = "swimmer",
            SwimmerId = swimmer.Id
        });

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
        var repo = new UserFavoriteRepository(db);

        var fav = await repo.AddAsync(user.Id, new AddFavoriteRequest
        {
            TargetType = "swimmer",
            SwimmerId = swimmer.Id
        });
        Assert.NotNull(fav);

        var ok = await repo.SetPrimaryAsync(user.Id + 999, fav!.Id);

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
