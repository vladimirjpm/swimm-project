using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Заявка тренера против НАСТОЯЩЕГО Postgres (docs/plans/coach-access-request-plan.md): то, чего
/// InMemory не видит, — колонка <c>CoachRequestedAt</c> (миграция <c>AddHubGroupCoachRequest</c>),
/// UNIQUE (группа, аккаунт) у админов и участников, FK метки на пловца, и одобрение одной записью
/// под retry-стратегией приложения. Пропуск, если PG недоступен или миграция не применена.
///
/// Свои аккаунты utest-*, своя группа; в finally всё удаляется (каскад от группы уносит состав,
/// участников и админов).
/// </summary>
[Collection(LiveDbCollection.Name)]
public class CoachAccessRequestPgTests
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
                || !(await db.Database.GetAppliedMigrationsAsync()).Any(m => m.EndsWith("_AddHubGroupCoachRequest")))
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

    private sealed class SettingsStub : ISettingsService
    {
        public IReadOnlyList<AdminSetting> GetAll() => [];
        public AdminSetting? Get(string key) => null;
        public T GetValue<T>(string key, T fallback) => fallback;
        public bool Update(string key, string newValue) => true;
    }

    [Fact]
    public async Task RequestApproveDecline_OnPostgres()
    {
        await using var probe = await TryCreateAsync();
        if (probe == null) return; // PG недоступен или миграции нет — пропуск
        var swimmerId = await probe.Swimmers.Where(s => s.PrivateHubGroupId == null).OrderBy(s => s.Id).Select(s => (int?)s.Id).FirstOrDefaultAsync();
        if (swimmerId == null) return;

        var tag = Guid.NewGuid().ToString("N")[..10];
        var owner = new AppUser { Email = $"utest-coachreq-owner-{tag}@swimm.test", DisplayName = "utest owner", SecurityStamp = "s" };
        var coach = new AppUser { Email = $"utest-coachreq-coach-{tag}@swimm.test", DisplayName = "utest coach", SecurityStamp = "s" };
        var other = new AppUser { Email = $"utest-coachreq-other-{tag}@swimm.test", DisplayName = "utest other", SecurityStamp = "s" };
        probe.AppUsers.AddRange(owner, coach, other);
        await probe.SaveChangesAsync();
        var group = new HubGroup
        {
            Name = "PG coach request test", Slug = $"pg-coach-req-{tag}", OwnerUserId = owner.Id,
            IsPublic = true, JoinPolicy = HubGroupJoinPolicy.Approval,
        };
        probe.HubGroups.Add(group);
        await probe.SaveChangesAsync();
        probe.HubGroupMembers.Add(new HubGroupMember { HubGroupId = group.Id, SwimmerId = swimmerId.Value, Role = "coach" });
        probe.UserFavorites.Add(new UserFavorite { UserId = coach.Id, TargetType = "swimmer", SwimmerId = swimmerId.Value, IsPrimary = true });
        await probe.SaveChangesAsync();

        try
        {
            await using (var db = NewContext())
            {
                var svc = new HubGroupUserService(db, new HubGroupCrudCore(db), new SettingsStub());

                // Не участник, группа с одобрением: вступление заявкой + пометка тренера.
                var req = await svc.RequestCoachAccessAsync(group.Id, coach.Id);
                Assert.True(req.Success, req.Error);
                Assert.True(req.Created);
                Assert.False((await svc.RequestCoachAccessAsync(group.Id, coach.Id)).Created);

                // Второй — уже участник, просит позже: пометка без новой строки.
                Assert.True((await svc.JoinAsync(group.Id, other.Id)).Success);
                Assert.True((await svc.RequestCoachAccessAsync(group.Id, other.Id)).Created);

                var list = await svc.GetCoachRequestsAsync(group.Id);
                Assert.Equal([coach.Id, other.Id], list.Select(r => r.UserId).ToArray());
                Assert.True(list[0].MeSwimmerInGroup);
                Assert.Equal(HubGroupUserMemberStatus.Pending, list[0].MembershipStatus);

                Assert.True((await svc.ApproveCoachRequestAsync(group.Id, coach.Id, owner.Id)).Success);
                Assert.True((await svc.DeclineCoachRequestAsync(group.Id, other.Id)).Success);
            }

            // Читаем заново из базы, а не из трекера.
            await using var check = NewContext();
            var coachRow = await check.HubGroupUserMembers.AsNoTracking().SingleAsync(m => m.HubGroupId == group.Id && m.UserId == coach.Id);
            Assert.Equal(HubGroupUserMemberStatus.Active, coachRow.Status);
            Assert.Equal(swimmerId, coachRow.SwimmerId);
            Assert.Null(coachRow.CoachRequestedAt);
            Assert.True(await check.HubGroupAdmins.AnyAsync(a => a.HubGroupId == group.Id && a.UserId == coach.Id && a.GrantedByUserId == owner.Id));

            var otherRow = await check.HubGroupUserMembers.AsNoTracking().SingleAsync(m => m.HubGroupId == group.Id && m.UserId == other.Id);
            Assert.Null(otherRow.CoachRequestedAt);
            Assert.False(await check.HubGroupAdmins.AnyAsync(a => a.HubGroupId == group.Id && a.UserId == other.Id));

            // Права: одобренный стал админом группы, но админов не назначает (CanManageAdmins).
            var perms = await new HubGroupPermissionService(check).GetPermissionsAsync(group.Id, coach.Id, isAdmin: false);
            Assert.True(perms.IsGroupAdmin);
            Assert.True(perms.CanEdit);
            Assert.False(perms.CanManageAdmins);
        }
        finally
        {
            await using var clean = NewContext();
            await clean.HubGroups.Where(g => g.Id == group.Id).ExecuteDeleteAsync();
            var ids = new[] { owner.Id, coach.Id, other.Id };
            await clean.UserFavorites.Where(f => ids.Contains(f.UserId)).ExecuteDeleteAsync();
            await clean.AppUsers.Where(u => ids.Contains(u.Id)).ExecuteDeleteAsync();
        }
    }
}
