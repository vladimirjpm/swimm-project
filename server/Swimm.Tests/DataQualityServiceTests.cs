using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Deep-link выборки «здоровье данных» (T3b, docs/tasks/dashboard-deeplinks-lists-sonnet.md) —
/// предикаты должны совпадать с DashboardStatusServiceTests (счётчики на дашборде): позитив,
/// негатив (исключения синтетики/псевдоклубов/approved) и кап 200.
/// </summary>
public class DataQualityServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    [Fact]
    public async Task GetSwimmerQuality_NoOrgId_MatchesDashboardPredicate()
    {
        await using var db = CreateDb(nameof(GetSwimmerQuality_NoOrgId_MatchesDashboardPredicate));
        var isrNoOrgId = new Swimmer { LastName = "B", FirstName = "B", BirthYear = 2000, Origin = "isr", SwimmerOrgId = null };
        var isrWithOrgId = new Swimmer { LastName = "A", FirstName = "A", BirthYear = 2000, Origin = "isr", SwimmerOrgId = "1" };
        var local = new Swimmer { LastName = "C", FirstName = "C", BirthYear = 2000, Origin = "local", SwimmerOrgId = null };
        db.Swimmers.AddRange(isrNoOrgId, isrWithOrgId, local);
        await db.SaveChangesAsync();

        var result = await new DataQualityService(db).GetSwimmerQualityAsync("no-org-id");

        Assert.Equal(1, result.Total);
        var row = Assert.Single(result.Items);
        Assert.Equal(isrNoOrgId.Id, row.Id);
    }

    [Fact]
    public async Task GetSwimmerQuality_NoResults_ExcludesSynthAndRelayOnlyAndWithResults()
    {
        await using var db = CreateDb(nameof(GetSwimmerQuality_NoResults_ExcludesSynthAndRelayOnlyAndWithResults));
        var noResults = new Swimmer { LastName = "A", FirstName = "A", BirthYear = 2000 };
        var synth = new Swimmer { LastName = "B", FirstName = "B", BirthYear = 2000, SwimmerOrgId = "SYNTH-1" };
        var withResult = new Swimmer { LastName = "C", FirstName = "C", BirthYear = 2000 };
        var relayOnly = new Swimmer { LastName = "D", FirstName = "D", BirthYear = 2000 };
        db.Swimmers.AddRange(noResults, synth, withResult, relayOnly);
        var club = new Club { Name = "Club" };
        var style = new Style { Name = "Freestyle" };
        var comp = new Competition { Name = "Comp", Date = "01/01/2026", PoolType = "25m" };
        db.Clubs.Add(club); db.Styles.Add(style); db.Competitions.Add(comp);
        await db.SaveChangesAsync();

        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = withResult.Id, ClubId = club.Id, StyleId = style.Id,
            CompetitionDate = DateTime.UtcNow
        });
        var relay = new Relay();
        db.Relays.Add(relay);
        await db.SaveChangesAsync();
        db.RelayMembers.Add(new RelayMember { RelayId = relay.Id, SwimmerId = relayOnly.Id, LegOrder = 1 });
        await db.SaveChangesAsync();

        var result = await new DataQualityService(db).GetSwimmerQualityAsync("no-results");

        Assert.Equal(1, result.Total);
        Assert.Equal(noResults.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task GetSwimmerQuality_UnknownFilter_ReturnsEmpty()
    {
        await using var db = CreateDb(nameof(GetSwimmerQuality_UnknownFilter_ReturnsEmpty));
        db.Swimmers.Add(new Swimmer { LastName = "A", FirstName = "A", BirthYear = 2000, Origin = "isr", SwimmerOrgId = null });
        await db.SaveChangesAsync();

        var result = await new DataQualityService(db).GetSwimmerQualityAsync("bogus");

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetClubQuality_NoSwimmers_ExcludesPseudoAndSynthAndWithResultOnly()
    {
        await using var db = CreateDb(nameof(GetClubQuality_NoSwimmers_ExcludesPseudoAndSynthAndWithResultOnly));
        var empty = new Club { Name = "Club1" };
        var pseudo = new Club { Name = "USA", IsPseudo = true };
        var synth = new Club { Name = "SYNTH club" };
        var withResultOnly = new Club { Name = "Club2" };
        db.Clubs.AddRange(empty, pseudo, synth, withResultOnly);
        var style = new Style { Name = "Freestyle" };
        var comp = new Competition { Name = "Comp", Date = "01/01/2026", PoolType = "25m" };
        var swimmer = new Swimmer { LastName = "A", FirstName = "A", BirthYear = 2000 };
        db.Styles.Add(style); db.Competitions.Add(comp); db.Swimmers.Add(swimmer);
        await db.SaveChangesAsync();
        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = withResultOnly.Id, StyleId = style.Id,
            CompetitionDate = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await new DataQualityService(db).GetClubQualityAsync("no-swimmers");

        Assert.Equal(1, result.Total);
        Assert.Equal(empty.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task GetClubQuality_NoCountry_ExcludesPseudo()
    {
        await using var db = CreateDb(nameof(GetClubQuality_NoCountry_ExcludesPseudo));
        var country = new Country { CountryCode = "ISR", CountryName = "Israel" };
        db.Countries.Add(country);
        await db.SaveChangesAsync();
        var noCountry = new Club { Name = "Club1" };
        var withCountry = new Club { Name = "Club2", CountryId = country.Id };
        var pseudoNoCountry = new Club { Name = "USA", IsPseudo = true };
        db.Clubs.AddRange(noCountry, withCountry, pseudoNoCountry);
        await db.SaveChangesAsync();

        var result = await new DataQualityService(db).GetClubQualityAsync("no-country");

        Assert.Equal(1, result.Total);
        Assert.Equal(noCountry.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task GetResultAnomalies_FkAndEmptyRelays()
    {
        await using var db = CreateDb(nameof(GetResultAnomalies_FkAndEmptyRelays));
        var club = new Club { Name = "Club" };
        var style = new Style { Name = "Freestyle" };
        var swimmer = new Swimmer { LastName = "A", FirstName = "A", BirthYear = 2000 };
        var comp = new Competition { Name = "Comp", Date = "01/01/2026", PoolType = "25m" };
        db.Clubs.Add(club); db.Styles.Add(style); db.Swimmers.Add(swimmer); db.Competitions.Add(comp);
        await db.SaveChangesAsync();

        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = 99999, ClubId = club.Id, StyleId = style.Id,
            CompetitionDate = DateTime.UtcNow
        });
        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            CompetitionDate = DateTime.UtcNow
        });
        var relayEmpty = new Relay();
        var relayWithMembers = new Relay();
        db.Relays.AddRange(relayEmpty, relayWithMembers);
        await db.SaveChangesAsync();
        db.RelayMembers.Add(new RelayMember { RelayId = relayWithMembers.Id, SwimmerId = swimmer.Id, LegOrder = 1 });
        await db.SaveChangesAsync();

        var result = await new DataQualityService(db).GetResultAnomaliesAsync();

        Assert.Equal(1, result.FkAnomalies.Total);
        Assert.Equal(99999, Assert.Single(result.FkAnomalies.Items).SwimmerId);
        Assert.Equal(1, result.EmptyRelays.Total);
        Assert.Equal(relayEmpty.Id, Assert.Single(result.EmptyRelays.Items).RelayId);
    }

    [Fact]
    public async Task GetResultAnomalies_NoGender_ExcludesRelays()
    {
        // Пол пуст у личного результата (смешанный заплыв, пол пловца неизвестен) — это
        // дыра в данных. У эстафеты пола нет по определению — её в список тащить незачем.
        await using var db = CreateDb(nameof(GetResultAnomalies_NoGender_ExcludesRelays));
        var club = new Club { Name = "Club" };
        var style = new Style { Name = "Freestyle" };
        var swimmer = new Swimmer { LastName = "A", FirstName = "A", BirthYear = 2012 };
        var comp = new Competition { Name = "Comp", Date = "01/01/2026", PoolType = "25m" };
        db.Clubs.Add(club); db.Styles.Add(style); db.Swimmers.Add(swimmer); db.Competitions.Add(comp);
        var relay = new Relay();
        db.Relays.Add(relay);
        await db.SaveChangesAsync();

        var noGender = new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            Gender = "", Distance = "200", CompetitionDate = DateTime.UtcNow
        };
        db.Results.Add(noGender);
        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            Gender = "male", Distance = "50", CompetitionDate = DateTime.UtcNow
        });
        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            RelayId = relay.Id, Gender = "", Distance = "4X50", CompetitionDate = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await new DataQualityService(db).GetResultAnomaliesAsync();

        Assert.Equal(1, result.NoGender.Total);
        var row = Assert.Single(result.NoGender.Items);
        Assert.Equal(noGender.Id, row.ResultId);
        Assert.Equal("A A", row.SwimmerName);
        Assert.Equal("200", row.Distance);
    }

    [Fact]
    public async Task GetResultAnomalies_ExactDuplicates_OnlyIdenticalRows()
    {
        // И10: одну дорожку в одном заплыве занимает один пловец один раз, поэтому полное
        // совпадение — всегда след импорта. Повтор дисциплины с РАЗНЫМ временем законен
        // (предварительные/финал) и находкой быть не должен.
        await using var db = CreateDb(nameof(GetResultAnomalies_ExactDuplicates_OnlyIdenticalRows));
        var club = new Club { Name = "Club" };
        var style = new Style { Name = "Freestyle" };
        var swimmer = new Swimmer { LastName = "Коэн", FirstName = "Таль", BirthYear = 2012 };
        var comp = new Competition { Name = "Meet", Date = "01/01/2026", PoolType = "25m" };
        db.AddRange(club, style, swimmer, comp);
        await db.SaveChangesAsync();

        ResultRecord Row(string time, int heat, int lane) => new()
        {
            CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            Distance = "50", Gender = "male", Heat = heat, Lane = lane,
            TimeOriginal = time, CompetitionDate = new DateTime(2026, 1, 1)
        };

        // Точный дубль (три копии одной строки).
        db.Results.AddRange(Row("00:30.00", 1, 4), Row("00:30.00", 1, 4), Row("00:30.00", 1, 4));
        // Законный повтор: та же дисциплина, другой заплыв и другое время — не находка.
        db.Results.Add(Row("00:29.50", 2, 5));
        await db.SaveChangesAsync();

        var result = await new DataQualityService(db).GetResultAnomaliesAsync();

        Assert.Equal(1, result.ExactDuplicates.Total);
        var row = Assert.Single(result.ExactDuplicates.Items);
        Assert.Equal(3, row.Copies);
        Assert.Equal("Коэн Таль", row.SwimmerName);
        Assert.Equal("00:30.00", row.Time);
        Assert.Equal(1, row.Heat);
        Assert.Equal(4, row.Lane);
    }

    [Fact]
    public async Task GetModerationPending_OnlyPendingStatus()
    {
        await using var db = CreateDb(nameof(GetModerationPending_OnlyPendingStatus));
        var user = new AppUser { Email = "u@x.com", DisplayName = "U" };
        var swimmer = new Swimmer { LastName = "A", FirstName = "A", BirthYear = 2000 };
        db.AppUsers.Add(user); db.Swimmers.Add(swimmer);
        await db.SaveChangesAsync();
        var pendingMedia = new UserMedia { UserId = user.Id, SwimmerId = swimmer.Id, Level = "swimmer", MediaType = "video", SourceType = "youtube", Url = "https://youtube.com/1" };
        var approvedMedia = new UserMedia { UserId = user.Id, SwimmerId = swimmer.Id, Level = "swimmer", MediaType = "image", SourceType = "other", Url = "https://example.com/1.jpg" };
        db.UserMedia.AddRange(pendingMedia, approvedMedia);
        var group = new HubGroup { Name = "G", Slug = "g", OwnerUserId = user.Id };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();
        db.UserMediaPublications.AddRange(
            new UserMediaPublication { UserMediaId = pendingMedia.Id, HubGroupId = group.Id, Status = UserMediaPublicationStatus.Pending },
            new UserMediaPublication { UserMediaId = approvedMedia.Id, HubGroupId = group.Id, Status = UserMediaPublicationStatus.Approved });
        await db.SaveChangesAsync();

        var result = await new DataQualityService(db).GetModerationPendingAsync();

        Assert.Equal(1, result.Total);
        Assert.Equal(pendingMedia.Url, Assert.Single(result.Items).Url);
    }

    [Fact]
    public async Task GetPendingJoinRequests_OnlyPendingStatus()
    {
        await using var db = CreateDb(nameof(GetPendingJoinRequests_OnlyPendingStatus));
        var owner = new AppUser { Email = "o@x.com", DisplayName = "O" };
        var pendingUser = new AppUser { Email = "p@x.com", DisplayName = "P" };
        var activeUser = new AppUser { Email = "a@x.com", DisplayName = "A" };
        db.AppUsers.AddRange(owner, pendingUser, activeUser);
        await db.SaveChangesAsync();
        var group = new HubGroup { Name = "G", Slug = "g", OwnerUserId = owner.Id };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();
        db.HubGroupUserMembers.AddRange(
            new HubGroupUserMember { HubGroupId = group.Id, UserId = pendingUser.Id, Status = HubGroupUserMemberStatus.Pending },
            new HubGroupUserMember { HubGroupId = group.Id, UserId = activeUser.Id, Status = HubGroupUserMemberStatus.Active });
        await db.SaveChangesAsync();

        var result = await new DataQualityService(db).GetPendingJoinRequestsAsync();

        Assert.Equal(1, result.Total);
        Assert.Equal(pendingUser.Email, Assert.Single(result.Items).Email);
    }

    [Fact]
    public async Task GetSwimmerQuality_CapsAt200ButTotalReflectsAll()
    {
        await using var db = CreateDb(nameof(GetSwimmerQuality_CapsAt200ButTotalReflectsAll));
        for (var i = 0; i < 250; i++)
            db.Swimmers.Add(new Swimmer { LastName = "S" + i, FirstName = "F", BirthYear = 2000, Origin = "isr", SwimmerOrgId = null });
        await db.SaveChangesAsync();

        var result = await new DataQualityService(db).GetSwimmerQualityAsync("no-org-id");

        Assert.Equal(250, result.Total);
        Assert.Equal(200, result.Items.Count);
        Assert.True(result.Total > result.Items.Count);
    }
}
