using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Смена клуба в карточке (Р95) сбрасывает профиль пловца <c>swimmer-profile:{id}</c> — шапку
/// страницы пловца, где и виден клуб. Метки строк при записи проверяет
/// <c>CacheRowTagsTests.SwimmerMovedToAnotherClub_DropsItsRow_AndBothClubs</c>; здесь — что
/// настоящий профиль, собранный из живого Postgres, этими метками действительно сбрасывается.
///
/// Профиль строится из базы (только чтение; нет базы — пропуск), запись идёт в InMemory-зеркало с
/// тем же id пловца, чей перехватчик сохранения сбрасывает метки в тот же кэш — как в
/// <see cref="CacheNarrowedPagesTests"/>. ⚠ Без базы тест молча зеленеет.
/// </summary>
[Collection(LiveDbCollection.Name)]
public class SwimmerClubChangeCachePgTests
{
    private const string Conn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private static SwimmReadDbContext? TryRead()
    {
        var db = new SwimmReadDbContext(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseNpgsql(Conn)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .AddInterceptors(new CacheDependencyInterceptor())
            .Options);
        try { if (db.Database.CanConnect()) return db; } catch { /* нет базы */ }
        db.Dispose();
        return null;
    }

    [Fact]
    public async Task ClubChange_DropsSwimmerProfile()
    {
        await using var read = TryRead();
        if (read is null) return; // PG недоступен — пропуск

        var two = await read.Swimmers
            .Where(s => s.ClubId != null && s.PrivateHubGroupId == null)
            .OrderBy(s => s.Id).Select(s => new { s.Id, ClubId = s.ClubId!.Value })
            .Take(2).ToListAsync();
        if (two.Count < 2) return;
        var (moved, other) = (two[0], two[1]);
        var newClub = await read.Clubs.Where(c => c.Id != moved.ClubId && c.MergedIntoId == null && !c.IsPseudo)
            .OrderBy(c => c.Id).Select(c => c.Id).FirstAsync();

        var cache = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()));
        var repo = new ResultRepository(read, cache);
        Assert.NotNull(await repo.GetSwimmerProfileAsync(moved.Id));
        Assert.NotNull(await repo.GetSwimmerProfileAsync(other.Id));
        Assert.Contains(cache.Snapshot(), e => e.Key == $"swimmer-profile:{moved.Id}");

        // Зеркало: тот же пловец в InMemory, смена клуба как у пересчёта (SaveChanges по карточке).
        var name = nameof(ClubChange_DropsSwimmerProfile);
        using (var seed = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options))
        {
            seed.Clubs.AddRange(new Club { Id = moved.ClubId, Name = "old" }, new Club { Id = newClub, Name = "new" });
            seed.Swimmers.Add(new Swimmer { Id = moved.Id, LastName = "S", ClubId = moved.ClubId });
            seed.SaveChanges();
        }
        await using (var mirror = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>()
                         .UseInMemoryDatabase(name).AddInterceptors(new CacheInvalidationInterceptor(cache)).Options))
        {
            (await mirror.Swimmers.SingleAsync(s => s.Id == moved.Id)).ClubId = newClub;
            await mirror.SaveChangesAsync();
        }

        Assert.DoesNotContain(cache.Snapshot(), e => e.Key == $"swimmer-profile:{moved.Id}");
    }
}
