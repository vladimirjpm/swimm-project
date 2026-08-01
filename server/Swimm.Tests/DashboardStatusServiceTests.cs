using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сводка «Здоровье данных» для дашборда /Admin (docs/plans/admin-dashboard-health-2-plan.md):
/// счётчики по всем блокам (пловцы/клубы/соревнования/результаты/рекорды/медиа/юзеры-группы/
/// система) + кэш на 2 минуты. Фейки дедуп-сервисов — простые классы-стабы (Moq в проекте нет).
/// </summary>
public class DashboardStatusServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private sealed class FakeSwimmerDedupService(SwimmerDedupReport report) : ISwimmerDedupService
    {
        public Task<SwimmerDedupReport> FindCandidatesAsync(CancellationToken ct = default) => Task.FromResult(report);
        public Task<SwimmerOrphanCleanupReport> DeleteOrphansAsync(IReadOnlyCollection<int>? ids, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeClubDedupService(ClubDedupReport report) : IClubDedupService
    {
        public Task<ClubDedupReport> FindCandidatesAsync(CancellationToken ct = default) => Task.FromResult(report);
    }

    private static SwimmerDedupReport SwimmerReport(int orphans, int sure, int unsure)
    {
        var report = new SwimmerDedupReport();
        for (var i = 0; i < orphans; i++)
            report.Orphans.Add(new SwimmerOrphan(i, "N", 2000, "isr", null));
        for (var i = 0; i < sure; i++)
            report.Candidates.Add(new SwimmerDedupCandidate(1, "A", null, 1, 2, "B", null, 1, 2000, "F", 0, Sure: true));
        for (var i = 0; i < unsure; i++)
            report.Candidates.Add(new SwimmerDedupCandidate(1, "A", null, 1, 2, "B", null, 1, 2000, "F", 2, Sure: false));
        return report;
    }

    private static ClubDedupReport ClubReport(int sure, int unsure)
    {
        var report = new ClubDedupReport();
        for (var i = 0; i < sure; i++)
            report.Candidates.Add(new ClubDedupCandidate(1, "A", null, 1, 2, "B", null, 1, "name", 0, Sure: true));
        for (var i = 0; i < unsure; i++)
            report.Candidates.Add(new ClubDedupCandidate(1, "A", null, 1, 2, "B", null, 1, "name", 0, Sure: false));
        return report;
    }

    private static Swimmer S(string? logligStatus, int? logligId = null) =>
        new() { LastName = "L", FirstName = "F", BirthYear = 2000, LogligIdStatus = logligStatus, LogligId = logligId };

    private static DiscoveredCompetition D(int orgCompId, string status, string name = "Comp") =>
        new() { OrgCompId = orgCompId, Name = name, DateStart = DateTime.UtcNow, DateEnd = DateTime.UtcNow, Status = status };

    private static DashboardStatusService CreateService(
        SwimmDbContext db,
        SwimmerDedupReport? swimmerReport = null,
        ClubDedupReport? clubReport = null,
        IMemoryCache? cache = null) =>
        new(db,
            new FakeSwimmerDedupService(swimmerReport ?? SwimmerReport(0, 0, 0)),
            new FakeClubDedupService(clubReport ?? ClubReport(0, 0)),
            cache ?? new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task GetStatusAsync_CombinesDedupReportsWithSureUnsureCounts()
    {
        await using var db = CreateDb(nameof(GetStatusAsync_CombinesDedupReportsWithSureUnsureCounts));
        var service = CreateService(db,
            swimmerReport: SwimmerReport(orphans: 3, sure: 2, unsure: 5),
            clubReport: ClubReport(sure: 1, unsure: 4));

        var result = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(2, result.Swimmers.SureCandidates);
        Assert.Equal(5, result.Swimmers.UnsureCandidates);
        Assert.Equal(3, result.Swimmers.Orphans);
        Assert.Equal(1, result.Clubs.SureCandidates);
        Assert.Equal(4, result.Clubs.UnsureCandidates);
    }

    [Fact]
    public async Task GetStatusAsync_CountsLogligByStatus_NullIsUnlinked()
    {
        await using var db = CreateDb(nameof(GetStatusAsync_CountsLogligByStatus_NullIsUnlinked));
        db.Swimmers.AddRange(
            S("Verified", 1), S("Verified", 2), S("Verified", 3),
            S("Suggested", 4),
            S("Rejected", 5), S("Rejected", 6),
            S(null), S(null), S(null), S(null));
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var result = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(new DashboardLogligStatus(Verified: 3, Suggested: 1, Rejected: 2, Unlinked: 4), result.Swimmers.Loglig);
    }

    [Fact]
    public async Task GetStatusAsync_CountsDiscoveryByMatch_SameLogicAsDiscoveryList()
    {
        // Совпадает с /Admin/Discovery: матч по имени+дате считается импортом, даже если Status
        // в БД всё ещё "new" (пайплайн импорта его не выставляет). Ignored — отдельно, не
        // смешивается с New. Ручная пометка Imported засчитывается, даже если матч не найден.
        await using var db = CreateDb(nameof(GetStatusAsync_CountsDiscoveryByMatch_SameLogicAsDiscoveryList));
        var matchedDate = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc);
        db.Competitions.Add(new Competition { Name = "ליגה מס 4", Date = "03/07/2026", PoolType = "25m" });
        db.DiscoveredCompetitions.AddRange(
            // матчится по имени+дате, но Status всё ещё "new" -> Imported
            new DiscoveredCompetition
            {
                OrgCompId = 1, Name = "ליגה מס 4", Status = DiscoveredCompetitionStatus.New,
                DateStart = matchedDate, DateEnd = matchedDate
            },
            // не матчится, Status "new" -> New
            D(2, DiscoveredCompetitionStatus.New, name: "Другое соревнование"),
            // Ignored, не матчится -> Ignored (не сливается с New)
            D(3, DiscoveredCompetitionStatus.Ignored, name: "Игнор"),
            // помечено вручную Imported, матча нет -> всё равно Imported
            D(4, DiscoveredCompetitionStatus.Imported, name: "Вручную помечено"));
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var result = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(2, result.Competitions.DiscoveryImported);
        Assert.Equal(1, result.Competitions.DiscoveryNew);
        Assert.Equal(1, result.Competitions.DiscoveryIgnored);
    }

    [Fact]
    public async Task GetStatusAsync_EmptyDb_ReturnsAllZeroes()
    {
        await using var db = CreateDb(nameof(GetStatusAsync_EmptyDb_ReturnsAllZeroes));
        var service = CreateService(db);

        var result = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(0, result.Swimmers.Total);
        Assert.Equal(0, result.Clubs.Total);
        Assert.Equal(0, result.Competitions.Total);
        Assert.Equal(0, result.Results.Total);
        Assert.Empty(result.RecordSets);
        Assert.Equal(0, result.Media.Total);
        Assert.Equal(0, result.UsersGroups.UsersTotal);
        Assert.Null(result.System.LastImportAt);
        Assert.Null(result.System.LastImportApproved);
        Assert.Null(result.System.LastMediaCheckAt);
        Assert.Null(result.System.LastDiscoverySeenAt);
        Assert.Equal(new DashboardLogligStatus(0, 0, 0, 0), result.Swimmers.Loglig);
    }

    [Fact]
    public async Task GetStatusAsync_Swimmers_CountsOriginAndNoResults()
    {
        await using var db = CreateDb(nameof(GetStatusAsync_Swimmers_CountsOriginAndNoResults));

        var isrWithOrgId = new Swimmer { LastName = "A", FirstName = "A", BirthYear = 2000, Origin = "isr", SwimmerOrgId = "111" };
        var isrNoOrgId = new Swimmer { LastName = "B", FirstName = "B", BirthYear = 2000, Origin = "isr", SwimmerOrgId = null };
        var local = new Swimmer { LastName = "C", FirstName = "C", BirthYear = 2000, Origin = "local" };
        var synth = new Swimmer { LastName = "D", FirstName = "D", BirthYear = 2000, Origin = "isr", SwimmerOrgId = "SYNTH-1" };
        var relayOnly = new Swimmer { LastName = "E", FirstName = "E", BirthYear = 2000, Origin = "isr", SwimmerOrgId = "222" };
        db.Swimmers.AddRange(isrWithOrgId, isrNoOrgId, local, synth, relayOnly);
        await db.SaveChangesAsync();

        var club = new Club { Name = "Club" };
        var style = new Style { Name = "Freestyle" };
        db.Clubs.Add(club);
        db.Styles.Add(style);
        var comp = new Competition { Name = "Comp", Date = "01/01/2026", PoolType = "25m" };
        db.Competitions.Add(comp);
        await db.SaveChangesAsync();

        // isrWithOrgId имеет результат
        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = isrWithOrgId.Id, ClubId = club.Id, StyleId = style.Id,
            CompetitionDate = DateTime.UtcNow
        });

        // relayOnly — только эстафета, без Results
        var relay = new Relay();
        db.Relays.Add(relay);
        await db.SaveChangesAsync();
        db.RelayMembers.Add(new RelayMember { RelayId = relay.Id, SwimmerId = relayOnly.Id, LegOrder = 1 });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var result = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(5, result.Swimmers.Total);
        Assert.Equal(4, result.Swimmers.OriginIsr); // isrWithOrgId, isrNoOrgId, synth, relayOnly
        Assert.Equal(1, result.Swimmers.OriginLocal);
        Assert.Equal(1, result.Swimmers.Synthetic);
        Assert.Equal(1, result.Swimmers.NoOrgId); // isrNoOrgId
        // NoResults: isrNoOrgId, local (synth исключена; isrWithOrgId — есть результат; relayOnly — есть RelayMember)
        Assert.Equal(2, result.Swimmers.NoResults);
    }

    [Fact]
    public async Task GetStatusAsync_Clubs_CountsNoSwimmersAndNoCountry()
    {
        await using var db = CreateDb(nameof(GetStatusAsync_Clubs_CountsNoSwimmersAndNoCountry));

        var country = new Country { CountryCode = "ISR", CountryName = "Israel" };
        db.Countries.Add(country);
        await db.SaveChangesAsync();

        var clubWithSwimmer = new Club { Name = "Club1", CountryId = country.Id };
        var clubWithResultOnly = new Club { Name = "Club2", CountryId = country.Id };
        var clubEmpty = new Club { Name = "Club3", CountryId = country.Id };
        var clubNoCountry = new Club { Name = "Club4" };
        var clubPseudo = new Club { Name = "USA", IsPseudo = true };
        db.Clubs.AddRange(clubWithSwimmer, clubWithResultOnly, clubEmpty, clubNoCountry, clubPseudo);
        await db.SaveChangesAsync();

        var style = new Style { Name = "Freestyle" };
        db.Styles.Add(style);
        var comp = new Competition { Name = "Comp", Date = "01/01/2026", PoolType = "25m" };
        db.Competitions.Add(comp);
        var swimmer = new Swimmer { LastName = "A", FirstName = "A", BirthYear = 2000, ClubId = clubWithSwimmer.Id };
        db.Swimmers.Add(swimmer);
        await db.SaveChangesAsync();

        // clubWithResultOnly: клуб с результатом, но без Swimmer.ClubId — НЕ должен попасть в NoSwimmers
        var resultSwimmer = new Swimmer { LastName = "B", FirstName = "B", BirthYear = 2000 };
        db.Swimmers.Add(resultSwimmer);
        await db.SaveChangesAsync();
        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = resultSwimmer.Id, ClubId = clubWithResultOnly.Id, StyleId = style.Id,
            CompetitionDate = DateTime.UtcNow
        });

        db.HubGroupClubRequests.Add(new HubGroupClubRequest
        {
            HubGroupId = 0, UserId = 0, ClubId = clubEmpty.Id, Status = HubGroupClubRequestStatus.Pending
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var result = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(5, result.Clubs.Total);
        Assert.Equal(1, result.Clubs.Pseudo);
        // NoSwimmers: clubEmpty и clubNoCountry (clubWithResultOnly и clubWithSwimmer исключены; pseudo исключён)
        Assert.Equal(2, result.Clubs.NoSwimmers);
        // NoCountry: только clubNoCountry (pseudo исключён)
        Assert.Equal(1, result.Clubs.NoCountry);
        Assert.Equal(1, result.Clubs.ClubRequestsPending);
    }

    [Fact]
    public async Task GetStatusAsync_Competitions_CountsWithResultsErrorsAndNoOrgCompId()
    {
        await using var db = CreateDb(nameof(GetStatusAsync_Competitions_CountsWithResultsErrorsAndNoOrgCompId));

        var compWithResults = new Competition { Name = "C1", Date = "01/01/2026", PoolType = "25m", OrgCompId = 1 };
        var compEmpty = new Competition { Name = "C2", Date = "01/01/2026", PoolType = "25m" };
        db.Competitions.AddRange(compWithResults, compEmpty);
        var club = new Club { Name = "Club" };
        var style = new Style { Name = "Freestyle" };
        var swimmer = new Swimmer { LastName = "A", FirstName = "A", BirthYear = 2000 };
        db.Clubs.Add(club);
        db.Styles.Add(style);
        db.Swimmers.Add(swimmer);
        await db.SaveChangesAsync();

        db.Results.Add(new ResultRecord
        {
            CompetitionId = compWithResults.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            CompetitionDate = DateTime.UtcNow
        });

        db.DiscoveredCompetitions.Add(new DiscoveredCompetition
        {
            OrgCompId = 99, Name = "Errored", DateStart = DateTime.UtcNow, DateEnd = DateTime.UtcNow,
            Status = DiscoveredCompetitionStatus.New, LastError = "timeout"
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var result = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(2, result.Competitions.Total);
        Assert.Equal(1, result.Competitions.WithResults);
        Assert.Equal(1, result.Competitions.DiscoveryErrors);
        Assert.Equal(1, result.Competitions.NoOrgCompId); // compEmpty
    }

    [Fact]
    public async Task GetStatusAsync_Results_CountsTimeFailFkAnomaliesAndEmptyRelays()
    {
        await using var db = CreateDb(nameof(GetStatusAsync_Results_CountsTimeFailFkAnomaliesAndEmptyRelays));

        var club = new Club { Name = "Club" };
        var style = new Style { Name = "Freestyle" };
        var swimmer = new Swimmer { LastName = "A", FirstName = "A", BirthYear = 2000 };
        var comp = new Competition { Name = "Comp", Date = "01/01/2026", PoolType = "25m" };
        db.Clubs.Add(club);
        db.Styles.Add(style);
        db.Swimmers.Add(swimmer);
        db.Competitions.Add(comp);
        await db.SaveChangesAsync();

        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            CompetitionDate = DateTime.UtcNow, TimeFail = true
        });
        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            CompetitionDate = DateTime.UtcNow, TimeFail = false
        });
        // FK-аномалия: несуществующий SwimmerId (InMemory не проверяет FK)
        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = 99999, ClubId = club.Id, StyleId = style.Id,
            CompetitionDate = DateTime.UtcNow
        });

        var relayWithMembers = new Relay();
        var relayEmpty = new Relay();
        db.Relays.AddRange(relayWithMembers, relayEmpty);
        await db.SaveChangesAsync();
        db.RelayMembers.Add(new RelayMember { RelayId = relayWithMembers.Id, SwimmerId = swimmer.Id, LegOrder = 1 });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var result = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(3, result.Results.Total);
        Assert.Equal(1, result.Results.TimeFail);
        Assert.Equal(1, result.Results.FkAnomalies);
        Assert.Equal(1, result.Results.EmptyRelays);
    }

    [Fact]
    public async Task GetStatusAsync_RecordSets_GroupsByRegion()
    {
        await using var db = CreateDb(nameof(GetStatusAsync_RecordSets_GroupsByRegion));

        var oldest = DateTime.UtcNow.AddDays(-10);
        var newest = DateTime.UtcNow;

        db.Records.AddRange(
            new Swimm.Domain.Entities.Record { RegionType = "world", RegionCode = "", Category = "open", AgeKey = "", Gender = "male", PoolType = "50m", Style = "freestyle", Distance = "50m", Time = "21.08", UpdatedAt = oldest },
            new Swimm.Domain.Entities.Record { RegionType = "world", RegionCode = "", Category = "open", AgeKey = "", Gender = "female", PoolType = "50m", Style = "freestyle", Distance = "50m", Time = "23.08", UpdatedAt = newest },
            new Swimm.Domain.Entities.Record { RegionType = "country", RegionCode = "ISR", Category = "open", AgeKey = "", Gender = "male", PoolType = "50m", Style = "freestyle", Distance = "50m", Time = "22.00", UpdatedAt = newest });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var result = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(2, result.RecordSets.Count);
        var world = Assert.Single(result.RecordSets, r => r.RegionType == "world" && r.RegionCode == "");
        Assert.Equal(2, world.Count);
        Assert.Equal(newest, world.LastUpdatedAt);
        var isr = Assert.Single(result.RecordSets, r => r.RegionType == "country" && r.RegionCode == "ISR");
        Assert.Equal(1, isr.Count);
    }

    [Fact]
    public async Task GetStatusAsync_Media_CountsVideoPhotoAndModerationPending()
    {
        await using var db = CreateDb(nameof(GetStatusAsync_Media_CountsVideoPhotoAndModerationPending));

        var user = new AppUser { Email = "u@x.com", DisplayName = "U" };
        var swimmer = new Swimmer { LastName = "A", FirstName = "A", BirthYear = 2000 };
        db.AppUsers.Add(user);
        db.Swimmers.Add(swimmer);
        await db.SaveChangesAsync();

        var video = new UserMedia { UserId = user.Id, SwimmerId = swimmer.Id, Level = "swimmer", MediaType = "video", SourceType = "youtube", Url = "https://youtube.com/watch?v=1" };
        var photo = new UserMedia { UserId = user.Id, SwimmerId = swimmer.Id, Level = "swimmer", MediaType = "image", SourceType = "other", Url = "https://example.com/1.jpg" };
        db.UserMedia.AddRange(video, photo);
        await db.SaveChangesAsync();

        var group = new HubGroup { Name = "G", Slug = "g", OwnerUserId = user.Id };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();

        db.UserMediaPublications.AddRange(
            new UserMediaPublication { UserMediaId = photo.Id, HubGroupId = group.Id, Status = UserMediaPublicationStatus.Pending },
            new UserMediaPublication { UserMediaId = video.Id, HubGroupId = group.Id, Status = UserMediaPublicationStatus.Approved });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var result = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(2, result.Media.Total);
        Assert.Equal(1, result.Media.Video);
        Assert.Equal(1, result.Media.Photo);
        Assert.Equal(1, result.Media.ModerationPending);
    }

    [Fact]
    public async Task GetStatusAsync_UsersGroups_CountsActive7dDeactivatedAndPending()
    {
        await using var db = CreateDb(nameof(GetStatusAsync_UsersGroups_CountsActive7dDeactivatedAndPending));

        var activeUser = new AppUser { Email = "a@x.com", DisplayName = "A", LastSeenAt = DateTime.UtcNow.AddDays(-1) };
        var staleUser = new AppUser { Email = "b@x.com", DisplayName = "B", LastSeenAt = DateTime.UtcNow.AddDays(-8) };
        var deactivatedUser = new AppUser { Email = "c@x.com", DisplayName = "C", IsActive = false };
        db.AppUsers.AddRange(activeUser, staleUser, deactivatedUser);
        await db.SaveChangesAsync();

        var officialGroup = new HubGroup { Name = "G1", Slug = "g1", OwnerUserId = activeUser.Id, IsOfficial = true };
        var regularGroup = new HubGroup { Name = "G2", Slug = "g2", OwnerUserId = activeUser.Id };
        db.HubGroups.AddRange(officialGroup, regularGroup);
        await db.SaveChangesAsync();

        db.HubGroupUserMembers.AddRange(
            new HubGroupUserMember { HubGroupId = regularGroup.Id, UserId = staleUser.Id, Status = HubGroupUserMemberStatus.Pending },
            new HubGroupUserMember { HubGroupId = regularGroup.Id, UserId = activeUser.Id, Status = HubGroupUserMemberStatus.Active });
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var result = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(3, result.UsersGroups.UsersTotal);
        Assert.Equal(1, result.UsersGroups.Active7d); // staleUser не считается (8 дней назад)
        Assert.Equal(1, result.UsersGroups.Deactivated);
        Assert.Equal(2, result.UsersGroups.GroupsTotal);
        Assert.Equal(1, result.UsersGroups.GroupsOfficial);
        Assert.Equal(1, result.UsersGroups.JoinRequestsPending);
    }

    [Fact]
    public async Task GetStatusAsync_System_CountsLastImportAndAuditActions7d()
    {
        await using var db = CreateDb(nameof(GetStatusAsync_System_CountsLastImportAndAuditActions7d));

        var comp = new Competition { Name = "Comp", Date = "01/01/2026", PoolType = "25m" };
        db.Competitions.Add(comp);
        await db.SaveChangesAsync();

        db.ImportHistory.Add(new ImportHistory
        {
            CompetitionId = comp.Id, ImportFileName = "old.pdf", ImportDate = DateTime.UtcNow.AddDays(-5), Approved = false
        });
        db.ImportHistory.Add(new ImportHistory
        {
            CompetitionId = comp.Id, ImportFileName = "new.pdf", ImportDate = DateTime.UtcNow, Approved = true
        });

        db.AdminAudits.Add(new AdminAudit { ActorName = "admin", Action = "x", EntityType = "Y", CreatedAt = DateTime.UtcNow.AddDays(-1) });
        db.AdminAudits.Add(new AdminAudit { ActorName = "admin", Action = "x", EntityType = "Y", CreatedAt = DateTime.UtcNow.AddDays(-8) }); // не считается
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var result = await service.GetStatusAsync(CancellationToken.None);

        Assert.NotNull(result.System.LastImportAt);
        Assert.True(result.System.LastImportApproved);
        Assert.Equal(1, result.System.AuditActions7d);
    }

    [Fact]
    public async Task GetStatusAsync_UsesCache_SecondCallDoesNotReflectDbChanges()
    {
        await using var db = CreateDb(nameof(GetStatusAsync_UsesCache_SecondCallDoesNotReflectDbChanges));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(db, cache: cache);

        var first = await service.GetStatusAsync(CancellationToken.None);
        Assert.Equal(0, first.Swimmers.Total);

        db.Swimmers.Add(new Swimmer { LastName = "A", FirstName = "A", BirthYear = 2000 });
        await db.SaveChangesAsync();

        var second = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(0, second.Swimmers.Total); // из кэша, БД изменилась, но число не изменилось
    }

    // ── Дубль клубного зачёта (K6) ──────────────────────────────────────────

    private static Competition Champ(string date, string pool, string name = "Champ", int? eventId = null, int? day = null) =>
        new() { Name = name, Date = date, PoolType = pool, IsChampionship = true, EventId = eventId, DayNumber = day };

    [Fact]
    public async Task DuplicateStandings_TwoWinterChampionshipsOfSameGroup_AreCounted()
    {
        // Двух зимних чемпионатов одной группы за сезон быть не может: если такое есть,
        // ошибка во флагах соревнования (IsChampionship / PoolType), а не в зачёте.
        await using var db = CreateDb(nameof(DuplicateStandings_TwoWinterChampionshipsOfSameGroup_AreCounted));
        var kids = new Category { Key = "results-kids-team", Name = "Kids", DisplayOrder = 1 };
        var a = Champ("15/02/2026", "25m", "A");
        var b = Champ("20/02/2026", "25m", "B");
        db.AddRange(kids, a, b);
        await db.SaveChangesAsync();
        db.AddRange(
            new CategoryCompetition { CategoryId = kids.Id, CompetitionId = a.Id },
            new CategoryCompetition { CategoryId = kids.Id, CompetitionId = b.Id });
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetStatusAsync(CancellationToken.None);

        Assert.Equal(1, result.Competitions.DuplicateStandings);
    }

    [Fact]
    public async Task DuplicateStandings_WinterAndSummer_AreNotDuplicates()
    {
        // Норма: у группы за сезон ОДИН зимний (25м) и ОДИН летний (50м).
        await using var db = CreateDb(nameof(DuplicateStandings_WinterAndSummer_AreNotDuplicates));
        var kids = new Category { Key = "results-kids-team", Name = "Kids", DisplayOrder = 1 };
        var winter = Champ("15/02/2026", "25m", "W");
        var summer = Champ("20/07/2026", "50m", "S");
        db.AddRange(kids, winter, summer);
        await db.SaveChangesAsync();
        db.AddRange(
            new CategoryCompetition { CategoryId = kids.Id, CompetitionId = winter.Id },
            new CategoryCompetition { CategoryId = kids.Id, CompetitionId = summer.Id });
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetStatusAsync(CancellationToken.None);

        Assert.Equal(0, result.Competitions.DuplicateStandings);
    }

    [Fact]
    public async Task DuplicateStandings_DaysOfOneEvent_AreNotDuplicates()
    {
        // Зачётная единица — СОБЫТИЕ целиком; три дня чемпионата это один зачёт.
        await using var db = CreateDb(nameof(DuplicateStandings_DaysOfOneEvent_AreNotDuplicates));
        var kids = new Category { Key = "results-kids-team", Name = "Kids", DisplayOrder = 1 };
        var ev = new CompetitionEvent { Name = "Champ" };
        db.AddRange(kids, ev);
        await db.SaveChangesAsync();
        var d1 = Champ("15/02/2026", "25m", "D1", ev.Id, 1);
        var d2 = Champ("16/02/2026", "25m", "D2", ev.Id, 2);
        db.AddRange(d1, d2);
        await db.SaveChangesAsync();
        db.AddRange(
            new CategoryCompetition { CategoryId = kids.Id, CompetitionId = d1.Id },
            new CategoryCompetition { CategoryId = kids.Id, CompetitionId = d2.Id });
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetStatusAsync(CancellationToken.None);

        Assert.Equal(0, result.Competitions.DuplicateStandings);
    }

    [Fact]
    public async Task DuplicateStandings_CustomCategory_IsIgnored()
    {
        // Кастомная категория (result-maccabiah) зачёта не образует вовсе.
        await using var db = CreateDb(nameof(DuplicateStandings_CustomCategory_IsIgnored));
        var custom = new Category { Key = "result-maccabiah", Name = "Maccabiah", DisplayOrder = 9 };
        var a = Champ("15/02/2026", "25m", "A");
        var b = Champ("20/02/2026", "25m", "B");
        db.AddRange(custom, a, b);
        await db.SaveChangesAsync();
        db.AddRange(
            new CategoryCompetition { CategoryId = custom.Id, CompetitionId = a.Id },
            new CategoryCompetition { CategoryId = custom.Id, CompetitionId = b.Id });
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetStatusAsync(CancellationToken.None);

        Assert.Equal(0, result.Competitions.DuplicateStandings);
    }
}
