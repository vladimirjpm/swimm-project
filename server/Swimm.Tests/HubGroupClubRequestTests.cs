using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты заявок на официальный статус группы (фаза 8.7): подача (HubGroupUserService)
/// и одобрение/отклонение (HubGroupClubRequestAdminService). EF InMemory — partial unique
/// indexes Postgres (одна официальная группа на клуб, одна pending-заявка на группу)
/// этими тестами не эмулируются, они проверены вручную через psql (см. docs/tasks).
/// </summary>
public class HubGroupClubRequestTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static DbContextOptions<SwimmDbContext> BuildOptions(string name) =>
        new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static SwimmDbContext CreateDb(string name) => new(BuildOptions(name));

    private sealed class NoopCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult(default(T));
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public int InvalidateCount { get; private set; }
        public Task InvalidateAllAsync() { InvalidateCount++; return Task.CompletedTask; }
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];
        public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        {
            Sent.Add((toEmail, subject, htmlBody));
            return Task.CompletedTask;
        }
    }

    private sealed class StubSettingsService : ISettingsService
    {
        public IReadOnlyList<AdminSetting> GetAll() => [];
        public AdminSetting? Get(string key) => null;
        public T GetValue<T>(string key, T fallback) => fallback;
        public bool Update(string key, string newValue) => true;
    }

    private static async Task<(AppUser owner, Club club, HubGroup group)> SeedAsync(SwimmDbContext db, bool ownerIsCoach = false)
    {
        var owner = new AppUser { Email = "owner@example.com", DisplayName = "Owner", SecurityStamp = "stamp-1" };
        var club = new Club { Name = "Test Club" };
        db.AppUsers.Add(owner);
        db.Clubs.Add(club);
        await db.SaveChangesAsync();

        var group = new HubGroup
        {
            Name = "Test Group", Slug = "test-group", OwnerUserId = owner.Id, IsPublic = true
        };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();

        if (ownerIsCoach)
        {
            var coachRole = new AppRole { Name = "Coach" };
            db.AppRoles.Add(coachRole);
            await db.SaveChangesAsync();
            db.AppUserRoles.Add(new AppUserRole { UserId = owner.Id, RoleId = coachRole.Id });
            await db.SaveChangesAsync();
        }

        return (owner, club, group);
    }

    // ── HubGroupUserService.SubmitClubRequestAsync ──────────────────────────

    [Fact]
    public async Task SubmitClubRequest_HappyPath_CreatesPendingRequest()
    {
        await using var db = CreateDb(nameof(SubmitClubRequest_HappyPath_CreatesPendingRequest));
        var (owner, club, group) = await SeedAsync(db);
        var core = new HubGroupCrudCore(db, new NoopCacheService());
        var service = new HubGroupUserService(db, core, new StubSettingsService());

        var result = await service.SubmitClubRequestAsync(group.Id, owner.Id,
            new HubGroupClubRequestInputDto { ClubId = club.Id, Message = "please" });

        Assert.True(result.Success);
        var saved = await db.HubGroupClubRequests.SingleAsync();
        Assert.Equal(HubGroupClubRequestStatus.Pending, saved.Status);
        Assert.Equal(club.Id, saved.ClubId);
        Assert.Equal(owner.Id, saved.UserId);
    }

    [Fact]
    public async Task SubmitClubRequest_GroupAlreadyOfficial_Fails()
    {
        await using var db = CreateDb(nameof(SubmitClubRequest_GroupAlreadyOfficial_Fails));
        var (owner, club, group) = await SeedAsync(db);
        group.IsOfficial = true;
        group.ClubId = club.Id;
        await db.SaveChangesAsync();
        var core = new HubGroupCrudCore(db, new NoopCacheService());
        var service = new HubGroupUserService(db, core, new StubSettingsService());

        var result = await service.SubmitClubRequestAsync(group.Id, owner.Id,
            new HubGroupClubRequestInputDto { ClubId = club.Id });

        Assert.False(result.Success);
        Assert.Empty(db.HubGroupClubRequests);
    }

    [Fact]
    public async Task SubmitClubRequest_PendingRequestAlreadyExists_Fails()
    {
        await using var db = CreateDb(nameof(SubmitClubRequest_PendingRequestAlreadyExists_Fails));
        var (owner, club, group) = await SeedAsync(db);
        db.HubGroupClubRequests.Add(new HubGroupClubRequest { HubGroupId = group.Id, UserId = owner.Id, ClubId = club.Id });
        await db.SaveChangesAsync();
        var core = new HubGroupCrudCore(db, new NoopCacheService());
        var service = new HubGroupUserService(db, core, new StubSettingsService());

        var result = await service.SubmitClubRequestAsync(group.Id, owner.Id,
            new HubGroupClubRequestInputDto { ClubId = club.Id });

        Assert.False(result.Success);
        Assert.Single(db.HubGroupClubRequests);
    }

    [Fact]
    public async Task SubmitClubRequest_ClubAlreadyOfficialElsewhere_Fails()
    {
        await using var db = CreateDb(nameof(SubmitClubRequest_ClubAlreadyOfficialElsewhere_Fails));
        var (owner, club, group) = await SeedAsync(db);
        db.HubGroups.Add(new HubGroup
        {
            Name = "Other Group", Slug = "other-group", OwnerUserId = owner.Id,
            IsPublic = true, IsOfficial = true, ClubId = club.Id
        });
        await db.SaveChangesAsync();
        var core = new HubGroupCrudCore(db, new NoopCacheService());
        var service = new HubGroupUserService(db, core, new StubSettingsService());

        var result = await service.SubmitClubRequestAsync(group.Id, owner.Id,
            new HubGroupClubRequestInputDto { ClubId = club.Id });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task GetClubRequest_ReturnsLatestByCreatedAt()
    {
        await using var db = CreateDb(nameof(GetClubRequest_ReturnsLatestByCreatedAt));
        var (owner, club, group) = await SeedAsync(db);
        db.HubGroupClubRequests.Add(new HubGroupClubRequest
        {
            HubGroupId = group.Id, UserId = owner.Id, ClubId = club.Id,
            Status = HubGroupClubRequestStatus.Rejected, CreatedAt = DateTime.UtcNow.AddDays(-1)
        });
        db.HubGroupClubRequests.Add(new HubGroupClubRequest
        {
            HubGroupId = group.Id, UserId = owner.Id, ClubId = club.Id,
            Status = HubGroupClubRequestStatus.Pending, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var core = new HubGroupCrudCore(db, new NoopCacheService());
        var service = new HubGroupUserService(db, core, new StubSettingsService());

        var latest = await service.GetClubRequestAsync(group.Id);

        Assert.NotNull(latest);
        Assert.Equal(HubGroupClubRequestStatus.Pending, latest!.Status);
    }

    // ── HubGroupClubRequestAdminService.ApproveAsync / RejectAsync ──────────

    [Fact]
    public async Task Approve_HappyPath_MakesGroupOfficialGrantsCoachAndBumpsStamp()
    {
        await using var db = CreateDb(nameof(Approve_HappyPath_MakesGroupOfficialGrantsCoachAndBumpsStamp));
        var (owner, club, group) = await SeedAsync(db);
        db.AppRoles.Add(new AppRole { Name = "Coach" });
        var admin = new AppUser { Email = "admin@example.com", DisplayName = "Admin", SecurityStamp = "admin-stamp" };
        db.AppUsers.Add(admin);
        await db.SaveChangesAsync();
        var request = new HubGroupClubRequest { HubGroupId = group.Id, UserId = owner.Id, ClubId = club.Id };
        db.HubGroupClubRequests.Add(request);
        await db.SaveChangesAsync();

        var cache = new NoopCacheService();
        var email = new RecordingEmailSender();
        var service = new HubGroupClubRequestAdminService(db, cache, email);
        var stampBefore = owner.SecurityStamp;

        var result = await service.ApproveAsync(request.Id, admin.Id);

        Assert.True(result.Success);
        var updatedGroup = await db.HubGroups.SingleAsync(g => g.Id == group.Id);
        Assert.True(updatedGroup.IsOfficial);
        Assert.Equal(club.Id, updatedGroup.ClubId);

        var updatedRequest = await db.HubGroupClubRequests.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(HubGroupClubRequestStatus.Approved, updatedRequest.Status);
        Assert.Equal(admin.Id, updatedRequest.DecidedByUserId);
        Assert.NotNull(updatedRequest.DecidedAt);

        var updatedOwner = await db.AppUsers.SingleAsync(u => u.Id == owner.Id);
        Assert.NotEqual(stampBefore, updatedOwner.SecurityStamp);
        Assert.True(await db.AppUserRoles.AnyAsync(ur => ur.UserId == owner.Id));

        Assert.Equal(1, cache.InvalidateCount);
        Assert.Single(email.Sent);
        Assert.Contains("approved", email.Sent[0].Subject);
    }

    [Fact]
    public async Task Approve_OwnerAlreadyCoach_DoesNotDuplicateRole()
    {
        await using var db = CreateDb(nameof(Approve_OwnerAlreadyCoach_DoesNotDuplicateRole));
        var (owner, club, group) = await SeedAsync(db, ownerIsCoach: true);
        var request = new HubGroupClubRequest { HubGroupId = group.Id, UserId = owner.Id, ClubId = club.Id };
        db.HubGroupClubRequests.Add(request);
        await db.SaveChangesAsync();
        var service = new HubGroupClubRequestAdminService(db, new NoopCacheService(), new RecordingEmailSender());

        var result = await service.ApproveAsync(request.Id, 0);

        Assert.True(result.Success);
        Assert.Single(await db.AppUserRoles.Where(ur => ur.UserId == owner.Id).ToListAsync());
    }

    [Fact]
    public async Task Approve_UnknownAdminUserId_LeavesDecidedByNull()
    {
        await using var db = CreateDb(nameof(Approve_UnknownAdminUserId_LeavesDecidedByNull));
        var (owner, club, group) = await SeedAsync(db);
        var request = new HubGroupClubRequest { HubGroupId = group.Id, UserId = owner.Id, ClubId = club.Id };
        db.HubGroupClubRequests.Add(request);
        await db.SaveChangesAsync();
        var service = new HubGroupClubRequestAdminService(db, new NoopCacheService(), new RecordingEmailSender());

        // DevAdminBypass (Program.cs) отдаёт синтетический NameIdentifier="0", которого нет
        // в Sys_AppUsers — не должно падать на FK, просто оставляем поле пустым.
        var result = await service.ApproveAsync(request.Id, adminUserId: 0);

        Assert.True(result.Success);
        var updated = await db.HubGroupClubRequests.SingleAsync(r => r.Id == request.Id);
        Assert.Null(updated.DecidedByUserId);
    }

    [Fact]
    public async Task Approve_ClubAlreadyOfficialElsewhere_FailsAndLeavesRequestPending()
    {
        await using var db = CreateDb(nameof(Approve_ClubAlreadyOfficialElsewhere_FailsAndLeavesRequestPending));
        var (owner, club, group) = await SeedAsync(db);
        db.HubGroups.Add(new HubGroup
        {
            Name = "Other Group", Slug = "other-group", OwnerUserId = owner.Id,
            IsPublic = true, IsOfficial = true, ClubId = club.Id
        });
        var request = new HubGroupClubRequest { HubGroupId = group.Id, UserId = owner.Id, ClubId = club.Id };
        db.HubGroupClubRequests.Add(request);
        await db.SaveChangesAsync();
        var service = new HubGroupClubRequestAdminService(db, new NoopCacheService(), new RecordingEmailSender());

        var result = await service.ApproveAsync(request.Id, 0);

        Assert.False(result.Success);
        var unchanged = await db.HubGroupClubRequests.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(HubGroupClubRequestStatus.Pending, unchanged.Status);
        var unchangedGroup = await db.HubGroups.SingleAsync(g => g.Id == group.Id);
        Assert.False(unchangedGroup.IsOfficial);
    }

    [Fact]
    public async Task Approve_RequestNotPending_Fails()
    {
        await using var db = CreateDb(nameof(Approve_RequestNotPending_Fails));
        var (owner, club, group) = await SeedAsync(db);
        var request = new HubGroupClubRequest
        {
            HubGroupId = group.Id, UserId = owner.Id, ClubId = club.Id,
            Status = HubGroupClubRequestStatus.Rejected
        };
        db.HubGroupClubRequests.Add(request);
        await db.SaveChangesAsync();
        var service = new HubGroupClubRequestAdminService(db, new NoopCacheService(), new RecordingEmailSender());

        var result = await service.ApproveAsync(request.Id, 0);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Reject_HappyPath_SetsStatusAndSendsEmail()
    {
        await using var db = CreateDb(nameof(Reject_HappyPath_SetsStatusAndSendsEmail));
        var (owner, club, group) = await SeedAsync(db);
        var request = new HubGroupClubRequest { HubGroupId = group.Id, UserId = owner.Id, ClubId = club.Id };
        db.HubGroupClubRequests.Add(request);
        await db.SaveChangesAsync();
        var email = new RecordingEmailSender();
        var service = new HubGroupClubRequestAdminService(db, new NoopCacheService(), email);

        var result = await service.RejectAsync(request.Id, 0);

        Assert.True(result.Success);
        var updated = await db.HubGroupClubRequests.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(HubGroupClubRequestStatus.Rejected, updated.Status);
        Assert.NotNull(updated.DecidedAt);
        var unchangedGroup = await db.HubGroups.SingleAsync(g => g.Id == group.Id);
        Assert.False(unchangedGroup.IsOfficial);
        Assert.Single(email.Sent);
        Assert.Contains("rejected", email.Sent[0].Subject);
    }

    [Fact]
    public async Task Reject_RequestNotPending_Fails()
    {
        await using var db = CreateDb(nameof(Reject_RequestNotPending_Fails));
        var (owner, club, group) = await SeedAsync(db);
        var request = new HubGroupClubRequest
        {
            HubGroupId = group.Id, UserId = owner.Id, ClubId = club.Id,
            Status = HubGroupClubRequestStatus.Approved
        };
        db.HubGroupClubRequests.Add(request);
        await db.SaveChangesAsync();
        var service = new HubGroupClubRequestAdminService(db, new NoopCacheService(), new RecordingEmailSender());

        var result = await service.RejectAsync(request.Id, 0);

        Assert.False(result.Success);
    }
}
