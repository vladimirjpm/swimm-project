using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты <see cref="MySwimsRepository"/> (агрегат «My media v3»: заплывы favorite-пловцов
/// за сезон + PB + медиа + реакции). EF InMemory — по образцу UserMediaRepositoryTests/
/// UserFavoriteRepositoryTests.
/// </summary>
public class MySwimsRepositoryTests
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

    private static async Task<(Style style, Club club)> SeedRefsAsync(SwimmDbContext db)
    {
        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "TestClub", NameEn = "TestClub" };
        db.Styles.Add(style);
        db.Clubs.Add(club);
        await db.SaveChangesAsync();
        return (style, club);
    }

    private static async Task<Competition> SeedCompetitionAsync(SwimmDbContext db, string name = "Cup", string date = "01/10/2025")
    {
        var comp = new Competition { Name = name, Date = date, PoolType = "50m" };
        db.Competitions.Add(comp);
        await db.SaveChangesAsync();
        return comp;
    }

    private static ResultRecord NewResult(
        Swimmer swimmer, Competition comp, Style style, Club club,
        DateTime competitionDate, string distance = "50", int? timeMs = 30000,
        int? position = 1, bool timeFail = false, int? relayId = null)
    {
        return new ResultRecord
        {
            SwimmerId = swimmer.Id,
            CompetitionId = comp.Id,
            ClubId = club.Id,
            StyleId = style.Id,
            Distance = distance,
            Gender = "male",
            // CompetitionDate — timestamp WITHOUT time zone, Kind обязан быть Unspecified (footgun задания).
            CompetitionDate = DateTime.SpecifyKind(competitionDate, DateTimeKind.Unspecified),
            Position = position,
            TimeMillisecond = timeMs,
            TimeOriginal = timeMs != null ? "00:30.00" : "DNF",
            TimeFail = timeFail,
            RelayId = relayId,
            InternationalPoints = 500,
        };
    }

    private static async Task AddFavoriteAsync(SwimmDbContext db, AppUser user, Swimmer swimmer)
    {
        db.UserFavorites.Add(new UserFavorite
        {
            UserId = user.Id,
            SwimmerId = swimmer.Id,
            TargetType = "swimmer",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    // ── Пустой набор favorites → пустой ответ ────────────────────────────────

    [Fact]
    public async Task GetMySwims_NoFavorites_ReturnsEmptyResponse()
    {
        await using var db = CreateDb(nameof(GetMySwims_NoFavorites_ReturnsEmptyResponse));
        var user = NewUser("u1@example.com");
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        var repo = new MySwimsRepository(db);

        var response = await repo.GetMySwimsAsync(user.Id, season: null);

        Assert.Empty(response.Swimmers);
        Assert.Empty(response.Seasons);
        Assert.Empty(response.Swims);
    }

    // ── Только favorites-пловцы и только выбранный сезон ─────────────────────

    [Fact]
    public async Task GetMySwims_TwoFavorites_OnlyTheirSwimsAndOnlySelectedSeason()
    {
        await using var db = CreateDb(nameof(GetMySwims_TwoFavorites_OnlyTheirSwimsAndOnlySelectedSeason));
        var user = NewUser("u2@example.com");
        var swimmer1 = NewSwimmer("Иванов", "Иван");
        var swimmer2 = NewSwimmer("Петров", "Пётр");
        var other = NewSwimmer("Чужой", "Пловец");
        db.AppUsers.Add(user);
        db.Swimmers.AddRange(swimmer1, swimmer2, other);
        await db.SaveChangesAsync();
        var (style, club) = await SeedRefsAsync(db);
        var comp = await SeedCompetitionAsync(db);

        await AddFavoriteAsync(db, user, swimmer1);
        await AddFavoriteAsync(db, user, swimmer2);

        // Сезон 2025: граница 2025-08-31 (сезон 2024) → 2025-09-01 (сезон 2025).
        var inSeasonBoundary = NewResult(swimmer1, comp, style, club, new DateTime(2025, 9, 1));
        var outOfSeasonBoundary = NewResult(swimmer2, comp, style, club, new DateTime(2025, 8, 31));
        var otherSwimmerResult = NewResult(other, comp, style, club, new DateTime(2025, 9, 15));
        db.Results.AddRange(inSeasonBoundary, outOfSeasonBoundary, otherSwimmerResult);
        await db.SaveChangesAsync();

        var repo = new MySwimsRepository(db);
        var response = await repo.GetMySwimsAsync(user.Id, season: 2025);

        Assert.Equal(2, response.Swimmers.Count);
        var swim = Assert.Single(response.Swims);
        Assert.Equal(inSeasonBoundary.Id, swim.ResultId);
        Assert.DoesNotContain(response.Swims, s => s.ResultId == outOfSeasonBoundary.Id);
        Assert.DoesNotContain(response.Swims, s => s.ResultId == otherSwimmerResult.Id);
    }

    // ── Чужой заплыв никогда не попадает ──────────────────────────────────────

    [Fact]
    public async Task GetMySwims_SwimmerNotInFavorites_NeverIncluded()
    {
        await using var db = CreateDb(nameof(GetMySwims_SwimmerNotInFavorites_NeverIncluded));
        var user = NewUser("u3@example.com");
        var swimmer = NewSwimmer("Иванов", "Иван");
        var stranger = NewSwimmer("Чужой", "Пловец");
        db.AppUsers.Add(user);
        db.Swimmers.AddRange(swimmer, stranger);
        await db.SaveChangesAsync();
        var (style, club) = await SeedRefsAsync(db);
        var comp = await SeedCompetitionAsync(db);
        await AddFavoriteAsync(db, user, swimmer);

        var strangerResult = NewResult(stranger, comp, style, club, new DateTime(2025, 10, 1));
        db.Results.Add(strangerResult);
        await db.SaveChangesAsync();

        var repo = new MySwimsRepository(db);
        var response = await repo.GetMySwimsAsync(user.Id, season: 2025);

        Assert.Empty(response.Swims);
    }

    // ── Эстафета: IsRelay=true, не участвует в PB ─────────────────────────────

    [Fact]
    public async Task GetMySwims_RelayResult_MarkedAsRelayAndExcludedFromPb()
    {
        await using var db = CreateDb(nameof(GetMySwims_RelayResult_MarkedAsRelayAndExcludedFromPb));
        var user = NewUser("u4@example.com");
        var swimmer = NewSwimmer("Иванов", "Иван");
        db.AppUsers.Add(user);
        db.Swimmers.Add(swimmer);
        await db.SaveChangesAsync();
        var (style, club) = await SeedRefsAsync(db);
        var comp = await SeedCompetitionAsync(db);
        await AddFavoriteAsync(db, user, swimmer);

        // Эстафетный заплыв с временем ЛУЧШЕ, чем индивидуальный ниже — не должен стать PB.
        var relay = NewResult(swimmer, comp, style, club, new DateTime(2025, 10, 1), timeMs: 20000, relayId: 1);
        var individual = NewResult(swimmer, comp, style, club, new DateTime(2025, 10, 2), timeMs: 30000);
        db.Results.AddRange(relay, individual);
        await db.SaveChangesAsync();

        var repo = new MySwimsRepository(db);
        var response = await repo.GetMySwimsAsync(user.Id, season: 2025);

        var relaySwim = response.Swims.Single(s => s.ResultId == relay.Id);
        var individualSwim = response.Swims.Single(s => s.ResultId == individual.Id);
        Assert.True(relaySwim.IsRelay);
        Assert.False(relaySwim.IsPb);
        // Индивидуальный — единственный не-relay заплыв на (style, distance) → это его личный рекорд.
        Assert.True(individualSwim.IsPb);
    }

    // ── PB: лучший по TimeMillisecond получает IsPb=true, худший — false; TimeFail не PB ──

    [Fact]
    public async Task GetMySwims_TwoSwimsSameStyleDistance_BestGetsPbWorstDoesNot()
    {
        await using var db = CreateDb(nameof(GetMySwims_TwoSwimsSameStyleDistance_BestGetsPbWorstDoesNot));
        var user = NewUser("u5@example.com");
        var swimmer = NewSwimmer("Иванов", "Иван");
        db.AppUsers.Add(user);
        db.Swimmers.Add(swimmer);
        await db.SaveChangesAsync();
        var (style, club) = await SeedRefsAsync(db);
        var comp = await SeedCompetitionAsync(db);
        await AddFavoriteAsync(db, user, swimmer);

        var best = NewResult(swimmer, comp, style, club, new DateTime(2025, 10, 1), timeMs: 29000);
        var worst = NewResult(swimmer, comp, style, club, new DateTime(2025, 10, 2), timeMs: 30000);
        var failed = NewResult(swimmer, comp, style, club, new DateTime(2025, 10, 3), timeMs: null, timeFail: true);
        db.Results.AddRange(best, worst, failed);
        await db.SaveChangesAsync();

        var repo = new MySwimsRepository(db);
        var response = await repo.GetMySwimsAsync(user.Id, season: 2025);

        Assert.True(response.Swims.Single(s => s.ResultId == best.Id).IsPb);
        Assert.False(response.Swims.Single(s => s.ResultId == worst.Id).IsPb);
        Assert.False(response.Swims.Single(s => s.ResultId == failed.Id).IsPb);
    }

    // ── Медиа: level=result → Media, level=competition → CompetitionMedia, level=swimmer → UnlinkedMedia ──

    [Fact]
    public async Task GetMySwims_Media_SplitByLevel_AndOnlyOwnUserMedia()
    {
        await using var db = CreateDb(nameof(GetMySwims_Media_SplitByLevel_AndOnlyOwnUserMedia));
        var user = NewUser("u6@example.com");
        var otherUser = NewUser("other6@example.com");
        var swimmer = NewSwimmer("Иванов", "Иван");
        db.AppUsers.AddRange(user, otherUser);
        db.Swimmers.Add(swimmer);
        await db.SaveChangesAsync();
        var (style, club) = await SeedRefsAsync(db);
        var comp = await SeedCompetitionAsync(db);
        await AddFavoriteAsync(db, user, swimmer);

        var result = NewResult(swimmer, comp, style, club, new DateTime(2025, 10, 1));
        db.Results.Add(result);
        await db.SaveChangesAsync();

        var resultMedia = new UserMedia { UserId = user.Id, SwimmerId = swimmer.Id, Level = "result", ResultId = result.Id, MediaType = "video", SourceType = "youtube", Url = "https://youtube.com/watch?v=1" };
        var competitionMedia = new UserMedia { UserId = user.Id, SwimmerId = swimmer.Id, Level = "competition", CompetitionId = comp.Id, MediaType = "video", SourceType = "youtube", Url = "https://youtube.com/watch?v=2" };
        var swimmerMedia = new UserMedia { UserId = user.Id, SwimmerId = swimmer.Id, Level = "swimmer", MediaType = "video", SourceType = "youtube", Url = "https://youtube.com/watch?v=3" };
        var otherUsersMedia = new UserMedia { UserId = otherUser.Id, SwimmerId = swimmer.Id, Level = "result", ResultId = result.Id, MediaType = "video", SourceType = "youtube", Url = "https://youtube.com/watch?v=4" };
        db.UserMedia.AddRange(resultMedia, competitionMedia, swimmerMedia, otherUsersMedia);
        await db.SaveChangesAsync();

        var repo = new MySwimsRepository(db);
        var response = await repo.GetMySwimsAsync(user.Id, season: 2025);

        var swim = Assert.Single(response.Swims);
        var swimMedia = Assert.Single(swim.Media);
        Assert.Equal(resultMedia.Id, swimMedia.Id);

        var compMedia = Assert.Single(response.CompetitionMedia);
        Assert.Equal(competitionMedia.Id, compMedia.Id);

        var unlinked = Assert.Single(response.UnlinkedMedia);
        Assert.Equal(swimmerMedia.Id, unlinked.Id);

        // Медиа другого юзера нигде не встречается.
        Assert.DoesNotContain(swim.Media, m => m.Id == otherUsersMedia.Id);
        Assert.DoesNotContain(response.CompetitionMedia, m => m.Id == otherUsersMedia.Id);
        Assert.DoesNotContain(response.UnlinkedMedia, m => m.Id == otherUsersMedia.Id);
    }

    // ── Реакции: congrats двух юзеров → CongratsCount=2, MyCheer только у своего; лайк на медиа ──

    [Fact]
    public async Task GetMySwims_Reactions_CongratsCountAndMyCheer_LikesCountAndMyLike()
    {
        await using var db = CreateDb(nameof(GetMySwims_Reactions_CongratsCountAndMyCheer_LikesCountAndMyLike));
        var user = NewUser("u7@example.com");
        var otherUser = NewUser("other7@example.com");
        var swimmer = NewSwimmer("Иванов", "Иван");
        db.AppUsers.AddRange(user, otherUser);
        db.Swimmers.Add(swimmer);
        await db.SaveChangesAsync();
        var (style, club) = await SeedRefsAsync(db);
        var comp = await SeedCompetitionAsync(db);
        await AddFavoriteAsync(db, user, swimmer);

        var result = NewResult(swimmer, comp, style, club, new DateTime(2025, 10, 1));
        db.Results.Add(result);
        await db.SaveChangesAsync();

        var media = new UserMedia { UserId = user.Id, SwimmerId = swimmer.Id, Level = "result", ResultId = result.Id, MediaType = "video", SourceType = "youtube", Url = "https://youtube.com/watch?v=1" };
        db.UserMedia.Add(media);
        await db.SaveChangesAsync();

        db.UserReactions.AddRange(
            new UserReaction { UserId = user.Id, Kind = "congrats", ResultId = result.Id },
            new UserReaction { UserId = otherUser.Id, Kind = "congrats", ResultId = result.Id },
            new UserReaction { UserId = user.Id, Kind = "like", MediaId = media.Id });
        await db.SaveChangesAsync();

        var repo = new MySwimsRepository(db);
        var response = await repo.GetMySwimsAsync(user.Id, season: 2025);

        var swim = Assert.Single(response.Swims);
        Assert.Equal(2, swim.CongratsCount);
        Assert.True(swim.MyCheer);

        var swimMedia = Assert.Single(swim.Media);
        Assert.Equal(1, swimMedia.LikesCount);
        Assert.True(swimMedia.MyLike);
    }

    // ── Эстафета видна НЕ-владельцу строки (по членству RelayMembers) ──────────

    [Fact]
    public async Task GetMySwims_RelayMember_SurfacesForNonOwnerFavorite()
    {
        await using var db = CreateDb(nameof(GetMySwims_RelayMember_SurfacesForNonOwnerFavorite));
        var user = NewUser("relaymember@example.com");
        var owner = NewSwimmer("Owner", "Mia");   // первая нога = владелец строки, НЕ в favorites
        var fav = NewSwimmer("Fav", "Sabina");    // нога эстафеты, в favorites
        db.AppUsers.Add(user);
        db.Swimmers.AddRange(owner, fav);
        await db.SaveChangesAsync();
        var (style, club) = await SeedRefsAsync(db);
        var comp = await SeedCompetitionAsync(db);
        await AddFavoriteAsync(db, user, fav);

        var relay = new Relay { TeamName = "Team", SwimmersName = "Mia, Sabina" };
        db.Relays.Add(relay);
        await db.SaveChangesAsync();
        relay.Members.Add(new RelayMember { RelayId = relay.Id, SwimmerId = owner.Id, LegOrder = 1 });
        relay.Members.Add(new RelayMember { RelayId = relay.Id, SwimmerId = fav.Id, LegOrder = 2 });
        await db.SaveChangesAsync();

        var relayResult = NewResult(owner, comp, style, club, new DateTime(2025, 10, 1),
            distance: "4X50", relayId: relay.Id);
        db.Results.Add(relayResult);
        await db.SaveChangesAsync();

        var repo = new MySwimsRepository(db);
        var response = await repo.GetMySwimsAsync(user.Id, season: 2025);

        // Эстафета пришла, хотя фаворит — только нога, а не владелец строки.
        var swim = Assert.Single(response.Swims);
        Assert.Equal(relayResult.Id, swim.ResultId);
        Assert.True(swim.IsRelay);
        Assert.Contains(fav.Id, swim.MemberSwimmerIds);
        Assert.Contains(owner.Id, swim.MemberSwimmerIds);
        // Владелец строки в чипы (favorites) не попадает — он не в избранном.
        Assert.DoesNotContain(response.Swimmers, s => s.Id == owner.Id);
    }
}
