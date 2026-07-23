using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты <see cref="ReactionRepository"/>: идемпотентные тогглы cheer/like, видимость
/// медиа для лайка (своё / approved public / approved members для члена группы).
/// EF InMemory — по образцу UserMediaRepositoryTests/UserMediaPublicationServiceTests.
/// </summary>
public class ReactionRepositoryTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static AppUser NewUser(string email) =>
        new() { Email = email, DisplayName = email, SecurityStamp = Guid.NewGuid().ToString("N") };

    private static Swimmer NewSwimmer(string last, string first) =>
        new() { LastName = last, FirstName = first, LastNameEn = last, FirstNameEn = first, BirthYear = 2000 };

    private static async Task<ResultRecord> SeedResultAsync(SwimmDbContext db)
    {
        var swimmer = NewSwimmer("Иванов", "Иван");
        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "TestClub", NameEn = "TestClub" };
        var comp = new Competition { Name = "Cup", Date = "01/10/2025", PoolType = "50m" };
        db.Swimmers.Add(swimmer);
        db.Styles.Add(style);
        db.Clubs.Add(club);
        db.Competitions.Add(comp);
        await db.SaveChangesAsync();

        var result = new ResultRecord
        {
            SwimmerId = swimmer.Id,
            CompetitionId = comp.Id,
            ClubId = club.Id,
            StyleId = style.Id,
            Distance = "50",
            Gender = "male",
            CompetitionDate = DateTime.SpecifyKind(new DateTime(2025, 10, 1), DateTimeKind.Unspecified),
            Position = 1,
            TimeMillisecond = 30000,
            TimeOriginal = "00:30.00",
        };
        db.Results.Add(result);
        await db.SaveChangesAsync();
        return result;
    }

    private static UserMedia NewMedia(AppUser owner, Swimmer swimmer, string visibility = "private") => new()
    {
        UserId = owner.Id,
        SwimmerId = swimmer.Id,
        Level = "swimmer",
        MediaType = "video",
        SourceType = "youtube",
        Url = "https://www.youtube.com/watch?v=abc123",
        Visibility = visibility,
    };

    // ── Cheer: on → Count=1,Mine=true; повторный on — идемпотентно; off → Count=0,Mine=false ──

    [Fact]
    public async Task SetCheer_OnExistingResult_ReturnsCountOneAndMineTrue()
    {
        await using var db = CreateDb(nameof(SetCheer_OnExistingResult_ReturnsCountOneAndMineTrue));
        var user = NewUser("u1@example.com");
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        var result = await SeedResultAsync(db);
        var repo = new ReactionRepository(db);

        var state = await repo.SetCheerAsync(user.Id, result.Id, on: true);

        Assert.NotNull(state);
        Assert.Equal(1, state!.Count);
        Assert.True(state.Mine);
    }

    [Fact]
    public async Task SetCheer_RepeatedOn_IsIdempotent()
    {
        await using var db = CreateDb(nameof(SetCheer_RepeatedOn_IsIdempotent));
        var user = NewUser("u2@example.com");
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        var result = await SeedResultAsync(db);
        var repo = new ReactionRepository(db);

        await repo.SetCheerAsync(user.Id, result.Id, on: true);
        var state = await repo.SetCheerAsync(user.Id, result.Id, on: true);

        Assert.Equal(1, state!.Count);
        Assert.True(state.Mine);
    }

    [Fact]
    public async Task SetCheer_Off_ReturnsCountZeroAndMineFalse()
    {
        await using var db = CreateDb(nameof(SetCheer_Off_ReturnsCountZeroAndMineFalse));
        var user = NewUser("u3@example.com");
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        var result = await SeedResultAsync(db);
        var repo = new ReactionRepository(db);

        await repo.SetCheerAsync(user.Id, result.Id, on: true);
        var state = await repo.SetCheerAsync(user.Id, result.Id, on: false);

        Assert.Equal(0, state!.Count);
        Assert.False(state.Mine);
    }

    [Fact]
    public async Task SetCheer_UnknownResultId_ReturnsNull()
    {
        await using var db = CreateDb(nameof(SetCheer_UnknownResultId_ReturnsNull));
        var user = NewUser("u4@example.com");
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        var repo = new ReactionRepository(db);

        var state = await repo.SetCheerAsync(user.Id, resultId: 999999, on: true);

        Assert.Null(state);
    }

    // ── Like: своё медиа → ok; чужое приватное без публикаций → null ────────

    [Fact]
    public async Task SetLike_OwnMedia_ReturnsOk()
    {
        await using var db = CreateDb(nameof(SetLike_OwnMedia_ReturnsOk));
        var owner = NewUser("owner5@example.com");
        var swimmer = NewSwimmer("Иванов", "Иван");
        db.AppUsers.Add(owner);
        db.Swimmers.Add(swimmer);
        await db.SaveChangesAsync();
        var media = NewMedia(owner, swimmer);
        db.UserMedia.Add(media);
        await db.SaveChangesAsync();
        var repo = new ReactionRepository(db);

        var state = await repo.SetLikeAsync(owner.Id, media.Id, on: true);

        Assert.NotNull(state);
        Assert.Equal(1, state!.Count);
        Assert.True(state.Mine);
    }

    [Fact]
    public async Task SetLike_ForeignPrivateMediaWithoutPublications_ReturnsNull()
    {
        await using var db = CreateDb(nameof(SetLike_ForeignPrivateMediaWithoutPublications_ReturnsNull));
        var owner = NewUser("owner6@example.com");
        var stranger = NewUser("stranger6@example.com");
        var swimmer = NewSwimmer("Иванов", "Иван");
        db.AppUsers.AddRange(owner, stranger);
        db.Swimmers.Add(swimmer);
        await db.SaveChangesAsync();
        var media = NewMedia(owner, swimmer);
        db.UserMedia.Add(media);
        await db.SaveChangesAsync();
        var repo = new ReactionRepository(db);

        var state = await repo.SetLikeAsync(stranger.Id, media.Id, on: true);

        Assert.Null(state);
    }

    // ── Like: чужое медиа с approved public публикацией → ok для любого юзера ──

    [Fact]
    public async Task SetLike_ForeignMediaWithApprovedPublicPublication_ReturnsOk()
    {
        await using var db = CreateDb(nameof(SetLike_ForeignMediaWithApprovedPublicPublication_ReturnsOk));
        var owner = NewUser("owner7@example.com");
        var stranger = NewUser("stranger7@example.com");
        var swimmer = NewSwimmer("Иванов", "Иван");
        db.AppUsers.AddRange(owner, stranger);
        db.Swimmers.Add(swimmer);
        await db.SaveChangesAsync();
        var media = NewMedia(owner, swimmer);
        db.UserMedia.Add(media);
        await db.SaveChangesAsync();
        var group = new HubGroup { Name = "G", Slug = "g-" + Guid.NewGuid().ToString("N"), OwnerUserId = owner.Id, IsPublic = true };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();
        db.UserMediaPublications.Add(new UserMediaPublication
        {
            UserMediaId = media.Id,
            HubGroupId = group.Id,
            Level = UserMediaPublicationLevel.Public,
            Status = UserMediaPublicationStatus.Approved,
        });
        await db.SaveChangesAsync();
        var repo = new ReactionRepository(db);

        var state = await repo.SetLikeAsync(stranger.Id, media.Id, on: true);

        Assert.NotNull(state);
        Assert.True(state!.Mine);
    }

    // ── Like: approved members публикация — член группы → ok, не член → null ──

    [Fact]
    public async Task SetLike_ForeignMediaWithApprovedMembersPublication_GroupMember_ReturnsOk()
    {
        await using var db = CreateDb(nameof(SetLike_ForeignMediaWithApprovedMembersPublication_GroupMember_ReturnsOk));
        var owner = NewUser("owner8@example.com");
        var member = NewUser("member8@example.com");
        var swimmer = NewSwimmer("Иванов", "Иван");
        db.AppUsers.AddRange(owner, member);
        db.Swimmers.Add(swimmer);
        await db.SaveChangesAsync();
        var media = NewMedia(owner, swimmer);
        db.UserMedia.Add(media);
        var group = new HubGroup { Name = "G", Slug = "g-" + Guid.NewGuid().ToString("N"), OwnerUserId = owner.Id, IsPublic = true };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();
        db.HubGroupUserMembers.Add(new HubGroupUserMember { HubGroupId = group.Id, UserId = member.Id, Status = HubGroupUserMemberStatus.Active });
        db.UserMediaPublications.Add(new UserMediaPublication
        {
            UserMediaId = media.Id,
            HubGroupId = group.Id,
            Level = UserMediaPublicationLevel.Members,
            Status = UserMediaPublicationStatus.Approved,
        });
        await db.SaveChangesAsync();
        var repo = new ReactionRepository(db);

        var state = await repo.SetLikeAsync(member.Id, media.Id, on: true);

        Assert.NotNull(state);
        Assert.True(state!.Mine);
    }

    [Fact]
    public async Task SetLike_ForeignMediaWithApprovedMembersPublication_NonMember_ReturnsNull()
    {
        await using var db = CreateDb(nameof(SetLike_ForeignMediaWithApprovedMembersPublication_NonMember_ReturnsNull));
        var owner = NewUser("owner9@example.com");
        var stranger = NewUser("stranger9@example.com");
        var swimmer = NewSwimmer("Иванов", "Иван");
        db.AppUsers.AddRange(owner, stranger);
        db.Swimmers.Add(swimmer);
        await db.SaveChangesAsync();
        var media = NewMedia(owner, swimmer);
        db.UserMedia.Add(media);
        var group = new HubGroup { Name = "G", Slug = "g-" + Guid.NewGuid().ToString("N"), OwnerUserId = owner.Id, IsPublic = true };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();
        db.UserMediaPublications.Add(new UserMediaPublication
        {
            UserMediaId = media.Id,
            HubGroupId = group.Id,
            Level = UserMediaPublicationLevel.Members,
            Status = UserMediaPublicationStatus.Approved,
        });
        await db.SaveChangesAsync();
        var repo = new ReactionRepository(db);

        var state = await repo.SetLikeAsync(stranger.Id, media.Id, on: true);

        Assert.Null(state);
    }
}
