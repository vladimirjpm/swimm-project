using Microsoft.EntityFrameworkCore;
using Npgsql;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Справочник бассейнов (П4, <c>Sys_VenuePools</c> + <c>Sys_VenuePoolAliases</c>) в НАСТОЯЩЕМ
/// Postgres под рантайм-ролью swimm_rw: InMemory не видит CHECK длины, UNIQUE написания, каскад
/// и права роли. Места у теста свои (<c>utest-…</c>), удаляются в finally. Пропуск, если PG
/// недоступен или миграция AddVenuePools ещё не применена.
/// </summary>
[Collection(LiveDbCollection.Name)]
public class VenuePoolPgTests
{
    private const string Runtime =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm_rw;Password=swimm_rw_local_dev";

    private static async Task<SwimmDbContext?> TryCreate()
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql(Runtime).Options);
        try
        {
            if (!await db.Database.CanConnectAsync()) { await db.DisposeAsync(); return null; }
            await db.VenuePools.AnyAsync(); // таблицы нет — миграция не применена
            return db;
        }
        catch { await db.DisposeAsync(); return null; }
    }

    private static async Task Cleanup(SwimmDbContext db, string tag)
    {
        db.ChangeTracker.Clear();
        await db.VenuePools.Where(v => v.Name.StartsWith(tag)).ExecuteDeleteAsync(); // написания — каскадом
        Assert.False(await db.VenuePoolAliases.AnyAsync(a => a.Alias.StartsWith(tag)));
    }

    private static string Tag() => "utest-venue-" + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task Create_Lookup_Update_Delete_ByRuntimeRole()
    {
        await using var db = await TryCreate();
        if (db is null) return;

        var tag = Tag();
        var repo = new VenuePoolAdminRepository(db);
        var service = new VenuePoolService(db);
        try
        {
            var created = await repo.CreateAsync(
                new VenuePoolInputDto($"{tag} בריכת וייסגל", "25m", false, "из истории", $"{tag} בריכת וויסגל רחובות"),
                "utest-admin");
            Assert.True(created.Success, created.Error);

            var found = await service.LookupAsync($"{tag} בריכת וייסגל רחובות");
            Assert.Equal(VenuePoolLookupStatus.Found, found.Status);
            Assert.Equal("25m", found.PoolType);
            Assert.Equal("utest-admin", found.Entry!.ConfirmedBy);

            // Повторное сохранение с ТЕМ ЖЕ написанием: старые строки уходят раньше новых, UNIQUE не задет.
            var updated = await repo.UpdateAsync(created.Id,
                new VenuePoolInputDto($"{tag} בריכת וייסגל", null, true, null, $"{tag} בריכת וויסגל רחובות"),
                "utest-admin-2");
            Assert.True(updated.Success, updated.Error);

            db.ChangeTracker.Clear();
            var ambiguous = await service.LookupAsync($"{tag} בריכת וייסגל רחובות"); // «וו»→«ו», «יי»→«י»
            Assert.Equal(VenuePoolLookupStatus.Ambiguous, ambiguous.Status);
            Assert.Equal("utest-admin-2", ambiguous.Entry!.ConfirmedBy);
            Assert.Equal(2, await db.VenuePoolAliases.CountAsync(a => a.VenuePoolId == created.Id));

            Assert.True((await repo.DeleteAsync(created.Id)).Success);
            Assert.Equal(VenuePoolLookupStatus.NotInDirectory, (await service.LookupAsync($"{tag} בריכת וייסגל")).Status);
        }
        finally
        {
            await Cleanup(db, tag);
        }
    }

    [Fact]
    public async Task SameSpelling_ForTwoVenues_IsRefused()
    {
        await using var db = await TryCreate();
        if (db is null) return;

        var tag = Tag();
        var repo = new VenuePoolAdminRepository(db);
        try
        {
            Assert.True((await repo.CreateAsync(new VenuePoolInputDto($"{tag} A", "25m", false, null, $"{tag} общий"), "utest")).Success);
            var second = await repo.CreateAsync(new VenuePoolInputDto($"{tag} B", "50m", false, null, $"{tag} ״общий״"), "utest");
            Assert.False(second.Success);
            Assert.Contains("уже относится", second.Error);
        }
        finally
        {
            await Cleanup(db, tag);
        }
    }

    [Fact]
    public async Task Database_Constraints_HoldWithoutTheRepository()
    {
        await using var db = await TryCreate();
        if (db is null) return;

        var tag = Tag();
        try
        {
            // CHECK: однозначное место без длины.
            db.VenuePools.Add(new VenuePool { Name = $"{tag} no-length", PoolType = null, IsAmbiguous = false, ConfirmedBy = "utest" });
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, (ex.InnerException as PostgresException)?.SqlState);
            db.ChangeTracker.Clear();

            // CHECK: двусмысленное с длиной.
            db.VenuePools.Add(new VenuePool { Name = $"{tag} both", PoolType = "25m", IsAmbiguous = true, ConfirmedBy = "utest" });
            ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, (ex.InnerException as PostgresException)?.SqlState);
            db.ChangeTracker.Clear();

            // UNIQUE: одно нормализованное написание у двух мест.
            var key = $"{tag} key";
            db.VenuePools.Add(new VenuePool
            {
                Name = $"{tag} one", PoolType = "25m", ConfirmedBy = "utest",
                Aliases = [new VenuePoolAlias { Alias = $"{tag} one", NormalizedAlias = key }],
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            db.VenuePools.Add(new VenuePool
            {
                Name = $"{tag} two", PoolType = "50m", ConfirmedBy = "utest",
                Aliases = [new VenuePoolAlias { Alias = $"{tag} two", NormalizedAlias = key }],
            });
            ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, (ex.InnerException as PostgresException)?.SqlState);
        }
        finally
        {
            await Cleanup(db, tag);
        }
    }
}
