using Microsoft.EntityFrameworkCore;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// «Who's in it» (Р8 docs/plans/trainings-tab-redesign-plan.md) против НАСТОЯЩЕГО Postgres: check
/// <c>CK_HubGroupMedia_AnchorMembersOnly</c> InMemory не видит — до миграции
/// <c>AllowSwimmerTagOnTrainingMedia</c> метка на видео тренировки падала в базе с 500, хотя все
/// InMemory-тесты были зелёными (поймано на приёмке 01.10.2026). Пропуск, если PG недоступен.
///
/// Тест ходит в рабочую локальную базу: заводит свою группу, тренировку и пловца и в finally
/// удаляет их (каскад уносит медиа и тренировку).
/// </summary>
[Collection(LiveDbCollection.Name)]
public class TrainingMediaTagPgTests
{
    private const string Conn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private static SwimmDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseNpgsql(Conn, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3))
            .Options);

    private static SwimmDbContext? TryCreate()
    {
        var db = NewContext();
        try { if (!db.Database.CanConnect()) { db.Dispose(); return null; } return db; }
        catch { db.Dispose(); return null; }
    }

    [Fact]
    public async Task TagOnTrainingLink_IsStored_ButAPublicGalleryAnchorIsStillRejected()
    {
        await using var probe = TryCreate();
        if (probe == null) return; // PG недоступен — пропуск
        var ownerId = await probe.AppUsers.OrderBy(u => u.Id).Select(u => (int?)u.Id).FirstOrDefaultAsync();
        if (ownerId == null) return;

        var tag = Guid.NewGuid().ToString("N")[..8];
        var group = new HubGroup { Name = "PG media tag test", Slug = $"pg-mtag-{tag}", OwnerUserId = ownerId.Value };
        var swimmer = new Swimmer { LastName = "PgTag", FirstName = $"Swimmer {tag}", BirthYear = 1980, Origin = "local" };
        probe.AddRange(group, swimmer);
        await probe.SaveChangesAsync();

        try
        {
            probe.HubGroupMembers.Add(new HubGroupMember { HubGroupId = group.Id, SwimmerId = swimmer.Id });
            var session = new TrainingSession
            {
                HubGroupId = group.Id, ExternalTrainingId = $"ui-pg-{tag}", Date = DateTime.UtcNow.Date, PoolType = "25m",
                Title = "Underwater filming", Type = TrainingSessionType.Technique,
            };
            probe.TrainingSessions.Add(session);
            await probe.SaveChangesAsync();

            await using (var db = NewContext())
            {
                var service = new TrainingSessionService(db, new HubGroupMediaService(db), new HubGroupTrainingRepository(db));
                var added = await service.AddMediaAsync(group.Id, session.Id, ownerId.Value, isManager: true,
                    new TrainingSessionMediaInputDto
                    {
                        MediaType = "video", SourceType = "youtube", Url = "https://youtu.be/pgtag", SwimmerId = swimmer.Id,
                    });

                Assert.Null(added.Error);
                Assert.Equal(swimmer.Id, added.Media!.Swimmer!.Id);
                Assert.Equal(swimmer.Id, (await db.HubGroupMedia.AsNoTracking().SingleAsync(m => m.HubGroupId == group.Id)).SwimmerId);

                // Снять метку и поставить снова — та же проверка базы.
                Assert.Null((await service.SetMediaSwimmerAsync(group.Id, session.Id, added.Media.Id, ownerId.Value,
                    new TrainingMediaSwimmerInputDto { SwimmerId = null })).Error);
                Assert.Null((await service.SetMediaSwimmerAsync(group.Id, session.Id, added.Media.Id, ownerId.Value,
                    new TrainingMediaSwimmerInputDto { SwimmerId = swimmer.Id })).Error);
            }

            // Публичная галерея (TrainingId null) с якорем-пловцом — по-прежнему запрещена базой.
            await using (var db = NewContext())
            {
                db.HubGroupMedia.Add(new HubGroupMedia
                {
                    HubGroupId = group.Id, Visibility = HubGroupMediaVisibility.Public, SwimmerId = swimmer.Id,
                    MediaType = "video", SourceType = "youtube", Url = "https://youtu.be/pgpublic", CreatedAt = DateTime.UtcNow,
                });
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }
        }
        finally
        {
            await using var cleanup = NewContext();
            await cleanup.HubGroups.Where(g => g.Id == group.Id).ExecuteDeleteAsync();
            await cleanup.Swimmers.Where(s => s.Id == swimmer.Id).ExecuteDeleteAsync();
        }
    }
}
