using Microsoft.EntityFrameworkCore;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Разовые занятия и отмена (Р9 docs/plans/trainings-tab-redesign-plan.md) против НАСТОЯЩЕГО
/// Postgres: то, чего InMemory не видит — колонка <c>AsksRsvp</c> с DEFAULT, UNIQUE
/// (группа, дата, начало) у отмен, FK и каскад. Урок Р8: InMemory-тесты были зелёными, а база
/// падала. Пропуск, если PG недоступен или миграция <c>AddOneOffAndCancelledTrainings</c> ещё не
/// применена к рабочей базе (тогда колонки и таблицы нет — проверять нечего).
///
/// Тест ходит в рабочую локальную базу: заводит свою группу и в finally удаляет её (каскад уносит
/// тренировку, отмены и ответы).
/// </summary>
[Collection(LiveDbCollection.Name)]
public class TrainingOccurrencePgTests
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
    public async Task OneOffTraining_Cancel_Twice_Restore_AndAnswer_OnPostgres()
    {
        await using var probe = TryCreate();
        if (probe == null) return; // PG недоступен — пропуск
        if (!(await probe.Database.GetAppliedMigrationsAsync()).Any(m => m.EndsWith("_AddOneOffAndCancelledTrainings")))
            return; // миграция ещё не применена — пропуск
        var ownerId = await probe.AppUsers.OrderBy(u => u.Id).Select(u => (int?)u.Id).FirstOrDefaultAsync();
        if (ownerId == null) return;

        var tag = Guid.NewGuid().ToString("N")[..8];
        var group = new HubGroup { Name = "PG one-off test", Slug = $"pg-oneoff-{tag}", OwnerUserId = ownerId.Value };
        probe.Add(group);
        await probe.SaveChangesAsync();

        try
        {
            // Владелец — участник группы: ему положены кнопки ответа.
            probe.HubGroupUserMembers.Add(new HubGroupUserMember { HubGroupId = group.Id, UserId = ownerId.Value });
            await probe.SaveChangesAsync();

            var nowLocal = IsraelTime.ToLocal(DateTime.UtcNow);
            var date = DateOnly.FromDateTime(nowLocal).AddDays(3);
            var key = TrainingRsvpRules.SessionKey(date, "18:00");
            int sessionId;

            // Тренировка с галкой «Ask who's coming».
            await using (var db = NewContext())
            {
                var sessions = new TrainingSessionService(db, new HubGroupMediaService(db), new HubGroupTrainingRepository(db));
                var created = await sessions.CreateAsync(group.Id, ownerId.Value, new TrainingSessionInputDto
                {
                    Date = date.ToString("yyyy-MM-dd"), Start = "18:00", Title = "Underwater filming",
                    Type = "technique", AsksRsvp = true,
                });
                Assert.Null(created.Error);
                Assert.True(created.Session!.AsksRsvp);
                sessionId = created.Session.Id;
            }
            // Прежние тренировки (без галки) и колонка с DEFAULT: вторая запись без AsksRsvp — false.
            await using (var db = NewContext())
            {
                var plain = new TrainingSession
                {
                    HubGroupId = group.Id, ExternalTrainingId = $"ui-pg-{tag}", Date = DateTime.UtcNow.Date,
                    PoolType = "25m", Title = "Plain",
                };
                db.TrainingSessions.Add(plain);
                await db.SaveChangesAsync();
                Assert.False((await db.TrainingSessions.AsNoTracking().SingleAsync(s => s.Id == plain.Id)).AsksRsvp);
            }

            await using (var db = NewContext())
            {
                var rsvp = new TrainingRsvpService(db);

                // Разовое в Next, на него отвечают — ответ реально записывается.
                var next = await rsvp.GetNextAsync(group.Id, nowLocal);
                var listed = Assert.Single(next!, n => n.Id == key);
                Assert.Equal((true, sessionId, "Underwater filming"), (listed.OneOff, listed.TrainingId, listed.Title));

                var answered = await rsvp.SetAsync(group.Id, key, ownerId.Value, isManager: false,
                    new TrainingRsvpInputDto { Answer = "yes" }, nowLocal);
                Assert.Null(answered.Error);
                Assert.Equal(1, answered.Rsvp!.Yes);
                Assert.Equal(1, await db.HubGroupTrainingRsvps.CountAsync(r => r.HubGroupId == group.Id && r.SessionStart == "18:00"));

                // Отмена дважды: вторая — идемпотентна (одна строка, причина обновилась), UNIQUE не падает.
                var first = await rsvp.CancelAsync(group.Id, key, ownerId.Value, "Pool closed", nowLocal);
                var second = await rsvp.CancelAsync(group.Id, key, ownerId.Value, "Coach is ill", nowLocal);
                Assert.Null(first.Error);
                Assert.Null(second.Error);
                Assert.Equal((true, "Coach is ill", 1), (second.Rsvp!.Cancelled, second.Rsvp.CancelReason, second.Rsvp.Yes));
                Assert.Equal(1, await db.HubGroupTrainingCancellations.CountAsync(c => c.HubGroupId == group.Id));

                // Ответить на отменённое нельзя.
                var blocked = await rsvp.SetAsync(group.Id, key, ownerId.Value, isManager: false,
                    new TrainingRsvpInputDto { Answer = "no" }, nowLocal);
                Assert.Equal((400, "This training is cancelled."), (blocked.Status, blocked.Error));

                // Вернуть — ответ на месте, отвечать снова можно.
                var restored = await rsvp.RestoreAsync(group.Id, key, ownerId.Value, nowLocal);
                Assert.Equal((false, 1, true), (restored.Rsvp!.Cancelled, restored.Rsvp.Yes, restored.Rsvp.CanAnswer));
                Assert.Equal(0, await db.HubGroupTrainingCancellations.CountAsync(c => c.HubGroupId == group.Id));
            }
        }
        finally
        {
            await using var cleanup = NewContext();
            // Каскад: тренировки, отмены и ответы уходят вместе с группой.
            await cleanup.HubGroups.Where(g => g.Id == group.Id).ExecuteDeleteAsync();
        }
    }
}
