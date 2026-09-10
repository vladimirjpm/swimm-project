using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Удаление группы (10.09.2026): перечень потерь для подтверждения
/// (<see cref="HubGroupAdminService.GetDeleteImpactAsync"/>) и аудит `hubgroup.delete`,
/// который пишет сам <see cref="HubGroupAdminService.DeleteAsync"/> — путей удаления три.
/// </summary>
public class HubGroupDeleteTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NoopCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult(default(T));
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private sealed record FakeActor(int? UserId, string Name, string? IpAddress) : ICurrentActor;

    private static HubGroupAdminService Service(SwimmDbContext db, ICurrentActor? actor = null) =>
        new(db, new HubGroupCrudCore(db, new NoopCacheService()),
            new AdminAuditService(db, actor ?? new FakeActor(7, "owner@example.com", null),
                NullLogger<AdminAuditService>.Instance));

    private static async Task<(AppUser owner, HubGroup group)> SeedGroupAsync(SwimmDbContext db, string name = "Dolphins")
    {
        var owner = new AppUser { Email = "owner@example.com", DisplayName = "Owner", SecurityStamp = "s" };
        db.AppUsers.Add(owner);
        await db.SaveChangesAsync();
        var group = new HubGroup { Name = name, NameEn = "Dolphins EN", Slug = "dolphins", OwnerUserId = owner.Id };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();
        return (owner, group);
    }

    [Fact]
    public async Task Impact_EmptyGroup_AllZero_NoContent()
    {
        await using var db = CreateDb(nameof(Impact_EmptyGroup_AllZero_NoContent));
        var (_, group) = await SeedGroupAsync(db);

        var impact = await Service(db).GetDeleteImpactAsync(group.Id);

        Assert.NotNull(impact);
        Assert.Equal("Dolphins", impact!.Name);
        Assert.Equal("Dolphins EN", impact.NameEn);
        Assert.Equal(0, impact.Swimmers + impact.TrainingSessions + impact.Media + impact.MediaPublications);
        Assert.False(impact.HasContent); // пустую удаляют одной кнопкой, без ввода имени
    }

    [Fact]
    public async Task Impact_CountsEverythingThatCascades_OnlyForThisGroup()
    {
        await using var db = CreateDb(nameof(Impact_CountsEverythingThatCascades_OnlyForThisGroup));
        var (owner, group) = await SeedGroupAsync(db);
        var other = new HubGroup { Name = "Other", Slug = "other", OwnerUserId = owner.Id };
        var club = new Club { Name = "Maccabi" };
        var member = new AppUser { Email = "m@example.com", DisplayName = "M", SecurityStamp = "s" };
        var swimmer = new Swimmer { FirstName = "A", LastName = "B" };
        db.AddRange(other, club, member, swimmer);
        await db.SaveChangesAsync();

        group.IsOfficial = true;
        group.ClubId = club.Id;
        db.HubGroupMembers.Add(new HubGroupMember { HubGroupId = group.Id, SwimmerId = swimmer.Id });
        db.HubGroupMembers.Add(new HubGroupMember { HubGroupId = other.Id, SwimmerId = swimmer.Id }); // чужая — не в счёт
        db.HubGroupUserMembers.Add(new HubGroupUserMember { HubGroupId = group.Id, UserId = member.Id });
        db.HubGroupAdmins.Add(new HubGroupAdmin { HubGroupId = group.Id, UserId = member.Id, GrantedByUserId = owner.Id });
        var session = new TrainingSession { HubGroupId = group.Id, ExternalTrainingId = "1", Date = DateTime.UtcNow, PoolType = "25m" };
        var otherSession = new TrainingSession { HubGroupId = other.Id, ExternalTrainingId = "2", Date = DateTime.UtcNow, PoolType = "25m" };
        db.TrainingSessions.AddRange(session, otherSession);
        await db.SaveChangesAsync();
        db.TrainingResults.AddRange(
            new TrainingResult { SessionId = session.Id, SwimmerId = swimmer.Id },
            new TrainingResult { SessionId = session.Id, SwimmerId = swimmer.Id },
            new TrainingResult { SessionId = otherSession.Id, SwimmerId = swimmer.Id });
        db.HubGroupMedia.AddRange(
            new HubGroupMedia { HubGroupId = group.Id, MediaType = "image", SourceType = "other", Url = "https://e.com/1.jpg", CreatedByUserId = owner.Id },
            new HubGroupMedia { HubGroupId = group.Id, TrainingId = session.Id, MediaType = "image", SourceType = "other", Url = "https://e.com/2.jpg", CreatedByUserId = owner.Id });
        var media = new UserMedia { UserId = member.Id, SwimmerId = swimmer.Id, Level = "swimmer", MediaType = "video", SourceType = "youtube", Url = "https://youtube.com/watch?v=1" };
        db.UserMedia.Add(media);
        await db.SaveChangesAsync();
        db.UserMediaPublications.Add(new UserMediaPublication { UserMediaId = media.Id, HubGroupId = group.Id });
        db.HubGroupClubRequests.Add(new HubGroupClubRequest { HubGroupId = group.Id, UserId = owner.Id, ClubId = club.Id });
        await db.SaveChangesAsync();

        var impact = (await Service(db).GetDeleteImpactAsync(group.Id))!;

        Assert.Equal(1, impact.Swimmers);
        Assert.Equal(1, impact.AccountMembers);
        Assert.Equal(1, impact.Admins);
        Assert.Equal(1, impact.TrainingSessions);
        Assert.Equal(2, impact.TrainingResults);
        Assert.Equal(2, impact.Media);
        Assert.Equal(1, impact.MediaPublications);
        Assert.True(impact.IsOfficial);
        Assert.Equal("Maccabi", impact.ClubName);
        Assert.True(impact.HasPendingClubRequest);
        Assert.True(impact.HasContent);
    }

    [Fact]
    public async Task Impact_UnknownGroup_Null()
    {
        await using var db = CreateDb(nameof(Impact_UnknownGroup_Null));

        Assert.Null(await Service(db).GetDeleteImpactAsync(999));
    }

    [Fact]
    public async Task Delete_WritesAuditWithActorAndLosses()
    {
        await using var db = CreateDb(nameof(Delete_WritesAuditWithActorAndLosses));
        var (_, group) = await SeedGroupAsync(db);
        db.HubGroupMembers.Add(new HubGroupMember { HubGroupId = group.Id, SwimmerId = 1 });
        db.TrainingSessions.Add(new TrainingSession { HubGroupId = group.Id, ExternalTrainingId = "1", Date = DateTime.UtcNow, PoolType = "25m" });
        await db.SaveChangesAsync();

        var result = await Service(db, new FakeActor(7, "owner@example.com", "10.0.0.1")).DeleteAsync(group.Id);

        Assert.True(result.Success);
        Assert.False(await db.HubGroups.AnyAsync(g => g.Id == group.Id));
        var audit = await db.AdminAudits.SingleAsync();
        Assert.Equal("hubgroup.delete", audit.Action);
        Assert.Equal(group.Id.ToString(), audit.EntityId);
        Assert.Equal(7, audit.ActorUserId); // владелец из панели, не только админ сайта
        Assert.Contains("«Dolphins»", audit.Summary);
        Assert.Contains("пловцов в составе 1", audit.Summary);
        Assert.Contains("тренировок 1", audit.Summary);
        Assert.Contains("\"TrainingSessions\":1", audit.DetailsJson);
    }

    [Fact]
    public async Task Delete_EmptyGroup_AuditSaysEmpty()
    {
        await using var db = CreateDb(nameof(Delete_EmptyGroup_AuditSaysEmpty));
        var (_, group) = await SeedGroupAsync(db);

        await Service(db).DeleteAsync(group.Id);

        Assert.EndsWith(": пустая", (await db.AdminAudits.SingleAsync()).Summary);
    }

    [Fact]
    public async Task Delete_UnknownGroup_FailsWithoutAudit()
    {
        await using var db = CreateDb(nameof(Delete_UnknownGroup_FailsWithoutAudit));

        var result = await Service(db).DeleteAsync(999);

        Assert.False(result.Success);
        Assert.Empty(db.AdminAudits);
    }
}
