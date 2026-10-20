using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Напоминания через Web Push против НАСТОЯЩЕГО Postgres (docs/plans/web-push-reminders-plan.md):
/// то, чего InMemory не видит, — UNIQUE журнала доставки (главная защита от двойной отправки, §4),
/// UNIQUE адреса подписки, CHECK вида напоминания, каскад от группы. И прогон джоба целиком:
/// второй прогон — и «второй процесс» с пустой памятью — дубля не даёт.
///
/// Пропуск, если PG недоступен или миграция <c>AddWebPushReminders</c> не применена. Тест заводит
/// свой аккаунт utest-*, группу и подписку и в finally всё удаляет.
/// </summary>
[Collection(LiveDbCollection.Name)]
public class WebPushPgTests
{
    private const string Conn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private static SwimmDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseNpgsql(Conn, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3))
            .Options);

    private static async Task<SwimmDbContext?> TryCreateAsync()
    {
        var db = NewContext();
        try
        {
            if (!await db.Database.CanConnectAsync()
                || !(await db.Database.GetAppliedMigrationsAsync()).Any(m => m.EndsWith("_AddWebPushReminders")))
            {
                await db.DisposeAsync();
                return null;
            }
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            return null;
        }
    }

    [Fact]
    public async Task ReminderJob_SendsOnce_AcrossRuns_AndConstraintsHold()
    {
        await using var probe = await TryCreateAsync();
        if (probe == null) return; // PG недоступен или миграции нет — пропуск
        // Свой тестовый аккаунт: у живого могут быть свои группы и настройки, их прогон не должен задеть.
        var user = new AppUser { Email = $"utest-push-{Guid.NewGuid():N}@example.com", DisplayName = "utest push" };
        probe.AppUsers.Add(user);
        await probe.SaveChangesAsync();
        int? userId = user.Id;

        // Завтра 18:00 по Израилю, «сейчас» — 16:01: напоминание за 2 часа уже пора.
        var tomorrow = DateOnly.FromDateTime(IsraelTime.ToLocal(DateTime.UtcNow)).AddDays(1);
        var isoDay = tomorrow.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)tomorrow.DayOfWeek;
        var nowUtc = IsraelTime.ToUtc(tomorrow.ToDateTime(new TimeOnly(16, 1)))!.Value;

        var tag = Guid.NewGuid().ToString("N")[..8];
        var endpoint = $"https://fcm.googleapis.com/fcm/send/pg-test-{tag}";
        var group = new HubGroup
        {
            Name = "PG push test", Slug = $"pg-push-{tag}", OwnerUserId = userId.Value,
            TrainingSchedule = new GroupTrainingSchedule
            {
                Slots = [new GroupTrainingSlot { Day = isoDay, Start = "18:00", End = "19:00" }],
            }.ToJson(),
        };
        try
        {
            probe.Add(group);
            await probe.SaveChangesAsync();

            probe.HubGroupUserMembers.Add(new HubGroupUserMember { HubGroupId = group.Id, UserId = userId.Value });
            probe.UserPushSubscriptions.Add(new UserPushSubscription
            {
                UserId = userId.Value, Endpoint = endpoint, P256dh = "BKey", Auth = "auth",
            });
            await probe.SaveChangesAsync();

            var sender = new PushSubscriptionServiceTests.FakeSender();
            await using (var db = NewContext())
                await new TrainingReminderService(db, sender, NullLogger<TrainingReminderService>.Instance).RunAsync(nowUtc);
            await using (var db = NewContext())
                await new TrainingReminderService(db, sender, NullLogger<TrainingReminderService>.Instance).RunAsync(nowUtc.AddMinutes(5));

            Assert.Single(sender.Sent, s => s.Target.Endpoint == endpoint);
            await using (var db = NewContext())
            {
                var row = await db.ReminderDeliveries.AsNoTracking().SingleAsync(d => d.HubGroupId == group.Id);
                Assert.Equal((userId.Value, tomorrow, "18:00", ReminderResult.Sent, 1, 1),
                    (row.UserId, row.SessionDate, row.SessionStart, row.Result, row.Devices, row.Delivered));
                Assert.NotNull((await db.UserPushSubscriptions.AsNoTracking().SingleAsync(s => s.Endpoint == endpoint)).LastSuccessAt);
            }

            // Второй процесс вставляет ту же строку журнала — UNIQUE не пускает (§4 п.2).
            await using (var db = NewContext())
            {
                db.ReminderDeliveries.Add(new ReminderDelivery
                {
                    UserId = userId.Value, HubGroupId = group.Id, SessionDate = tomorrow, SessionStart = "18:00",
                    Kind = ReminderKind.Reminder,
                });
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }

            // Тот же браузер второй строкой — UNIQUE (Endpoint).
            await using (var db = NewContext())
            {
                db.UserPushSubscriptions.Add(new UserPushSubscription
                {
                    UserId = userId.Value, Endpoint = endpoint, P256dh = "BKey", Auth = "auth",
                });
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }

            // Неизвестный вид напоминания — CHECK.
            await using (var db = NewContext())
            {
                db.UserNotificationSettings.Add(new UserNotificationSettings { UserId = userId.Value, LeadKind = "3h" });
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }
        }
        finally
        {
            await using var cleanup = NewContext();
            // Каскад: участник и журнал доставки уходят с группой, подписка и настройки — с аккаунтом.
            await cleanup.HubGroups.Where(g => g.Id == group.Id).ExecuteDeleteAsync();
            await cleanup.AppUsers.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
        }
    }
}
