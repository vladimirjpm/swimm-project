using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Подписка группы на клуб и пересборка состава (docs/plans/hubgroup-club-subscription-plan.md П2):
/// правила <see cref="HubGroupClubRules"/>, сервис <see cref="HubGroupClubSubscriptionService"/>,
/// скрытые владельцем клубные пловцы у читателей состава, склейки и импорт.
///
/// EF InMemory; <see cref="SwimmReadDbContext"/> наследует SwimmDbContext, поэтому один контекст
/// годится и публичному репозиторию, и сервисам записи. Даты — от «сейчас»: окно «пловцы клуба»
/// считается от текущего сезона, и тест не должен протухнуть через год.
/// </summary>
public class HubGroupClubSubscriptionTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static SwimmReadDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class FakeCache : ICacheService
    {
        public int Invalidations { get; private set; }
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() { Invalidations++; return Task.CompletedTask; }
    }

    private sealed class SettingsStub : ISettingsService
    {
        public IReadOnlyList<AdminSetting> GetAll() => [];
        public AdminSetting? Get(string key) => null;
        public T GetValue<T>(string key, T fallback) => fallback;
        public bool Update(string key, string newValue) => true;
    }

    private static HubGroupClubSubscriptionService Service(SwimmDbContext db, FakeCache? cache = null) =>
        new(db, new HubGroupCrudCore(db, cache ?? new FakeCache()));

    private static HubGroupCrudCore Core(SwimmDbContext db) => new(db, new FakeCache());

    /// <summary>Дата внутри ТЕКУЩЕГО сезона.</summary>
    private static DateTime CurrentSeason => SeasonMath.StartOf(SeasonMath.StartYearOf(DateTime.UtcNow)).AddDays(10);

    /// <summary>Дата внутри ПРОШЛОГО сезона — ещё в окне.</summary>
    private static DateTime PreviousSeason => SeasonMath.StartOf(SeasonMath.StartYearOf(DateTime.UtcNow) - 1).AddDays(30);

    /// <summary>Позапрошлый сезон — уже вне окна.</summary>
    private static DateTime TooOld => SeasonMath.StartOf(SeasonMath.StartYearOf(DateTime.UtcNow) - 1).AddDays(-30);

    private sealed class World
    {
        public Club A = null!, B = null!;
        public Competition Comp = null!;
        public Style Style = null!;
        public HubGroup Group = null!;
    }

    private static async Task<World> SeedWorldAsync(SwimmDbContext db)
    {
        var w = new World
        {
            A = new Club { Name = "הפועל דולפין נתניה", NameEn = "Hapoel Dolphine Netanya" },
            B = new Club { Name = "מכבי חיפה", NameEn = "Maccabi Haifa" },
            Comp = new Competition { Name = "Meet", Date = "01/06/2026", PoolType = "25m" },
            Style = new Style { Name = "Freestyle" },
            // Владелец нужен форме группы: GetByIdAsync подтягивает Owner (обязательная связь).
            Group = new HubGroup
            {
                Name = "Dolphins fans", Slug = "dolphins-fans",
                Owner = new AppUser { Email = "owner@example.com", DisplayName = "Owner", SecurityStamp = "s" }
            }
        };
        db.AddRange(w.A, w.B, w.Comp, w.Style, w.Group);
        await db.SaveChangesAsync();
        return w;
    }

    private static async Task<Swimmer> SwimmerAsync(SwimmDbContext db, World w, string last, Club club, DateTime date, int? relayId = null)
    {
        var s = new Swimmer { LastName = last, FirstName = "F", LastNameEn = last, FirstNameEn = "F", BirthYear = 2012 };
        db.Swimmers.Add(s);
        await db.SaveChangesAsync();
        await ResultAsync(db, w, s, club, date, relayId);
        return s;
    }

    private static async Task ResultAsync(SwimmDbContext db, World w, Swimmer s, Club club, DateTime date, int? relayId = null)
    {
        db.Results.Add(new ResultRecord
        {
            SwimmerId = s.Id, ClubId = club.Id, CompetitionId = w.Comp.Id, StyleId = w.Style.Id,
            Distance = "50", Gender = "male", CompetitionDate = date, RelayId = relayId
        });
        await db.SaveChangesAsync();
    }

    private static Task<List<HubGroupMember>> RowsAsync(SwimmDbContext db, int groupId) =>
        db.HubGroupMembers.AsNoTracking().Where(m => m.HubGroupId == groupId).ToListAsync();

    private static async Task<HubGroupMember> RowOfAsync(SwimmDbContext db, int groupId, Swimmer s) =>
        await db.HubGroupMembers.AsNoTracking().SingleAsync(m => m.HubGroupId == groupId && m.SwimmerId == s.Id);

    // ── Правила: окно активности ─────────────────────────────────────────────

    [Theory]
    [InlineData("2026-09-10", "2025-09-01")] // 1 сентября начался 2026/27 — окно с начала 2025/26
    [InlineData("2026-08-31", "2024-09-01")] // ещё 2025/26 — окно с начала 2024/25
    [InlineData("2026-01-15", "2024-09-01")]
    public void ActivitySince_IsStartOfPreviousSeason(string now, string expected)
    {
        Assert.Equal(DateTime.Parse(expected), HubGroupClubRules.ActivitySince(DateTime.Parse(now)));
    }

    // ── Правила: план пересборки ─────────────────────────────────────────────

    private static HubGroupClubRules.MemberRow Row(int id, int swimmerId, string source, bool excluded = false) =>
        new(id, swimmerId, source, excluded);

    [Fact]
    public void PlanSync_InsertsMissing_DeletesClubOutsideSet_KeepsManualAndExcluded()
    {
        var plan = HubGroupClubRules.PlanSync(
            [
                Row(1, 10, HubGroupMemberSource.Manual),       // ручной вне набора — не трогать
                Row(2, 20, HubGroupMemberSource.Club),         // клубный в наборе — оставить
                Row(3, 30, HubGroupMemberSource.Club),         // клубный вне набора — удалить
                Row(4, 40, HubGroupMemberSource.Club, true),   // скрытый в наборе — НЕ возвращать и не дублировать
                Row(5, 50, HubGroupMemberSource.Manual),       // ручной в наборе — остаётся ручным
            ],
            [20, 40, 50, 60]);

        Assert.Equal([60], plan.InsertSwimmerIds);
        Assert.Equal([3], plan.DeleteRowIds);
    }

    [Fact]
    public void PlanSync_EmptySet_IsUnsubscribe_RemovesAllClubRows_IncludingExcluded()
    {
        var plan = HubGroupClubRules.PlanSync(
            [Row(1, 10, HubGroupMemberSource.Manual), Row(2, 20, HubGroupMemberSource.Club), Row(3, 30, HubGroupMemberSource.Club, true)],
            []);

        Assert.Empty(plan.InsertSwimmerIds);
        Assert.Equal([2, 3], plan.DeleteRowIds.OrderBy(x => x));
    }

    // ── Правила: склейка двух строк одной группы ─────────────────────────────

    [Theory]
    [InlineData("manual", false, "club", true, "manual", false)] // ручная побеждает — и видима
    [InlineData("club", true, "manual", false, "manual", false)]
    [InlineData("club", false, "club", true, "club", true)]      // обе клубные — скрыта, если хоть одна
    [InlineData("club", false, "club", false, "club", false)]
    [InlineData("manual", false, "manual", false, "manual", false)]
    public void MergeRows_ManualWins_ExcludedIfEitherClub(
        string aSource, bool aExcluded, string bSource, bool bExcluded, string source, bool excluded)
    {
        Assert.Equal((source, excluded), HubGroupClubRules.MergeRows((aSource, aExcluded), (bSource, bExcluded)));
    }

    // ── Подписка и пересборка ────────────────────────────────────────────────

    [Fact]
    public async Task Subscribe_AddsClubSwimmers_FromCurrentAndPreviousSeason_IndividualOnly()
    {
        await using var db = CreateDb(nameof(Subscribe_AddsClubSwimmers_FromCurrentAndPreviousSeason_IndividualOnly));
        var w = await SeedWorldAsync(db);
        var current = await SwimmerAsync(db, w, "Current", w.A, CurrentSeason);
        var previous = await SwimmerAsync(db, w, "Previous", w.A, PreviousSeason);
        await SwimmerAsync(db, w, "Old", w.A, TooOld);                     // ушёл давно
        await SwimmerAsync(db, w, "OtherClub", w.B, CurrentSeason);        // чужой клуб
        await SwimmerAsync(db, w, "RelayShadow", w.A, CurrentSeason, 777); // пловец-тень эстафеты
        var cache = new FakeCache();

        var result = await Service(db, cache).SubscribeAsync(w.Group.Id, w.A.Id, userId: 5);

        Assert.True(result.Success);
        Assert.Equal(w.A.Id, result.Subscription!.ClubId);
        Assert.Equal(2, result.Sync.Added);
        var rows = await RowsAsync(db, w.Group.Id);
        Assert.Equal(new[] { current.Id, previous.Id }.Order(), rows.Select(r => r.SwimmerId).Order());
        Assert.All(rows, r => Assert.Equal(HubGroupMemberSource.Club, r.Source));
        Assert.Equal(5, (await db.HubGroupClubSubscriptions.SingleAsync()).CreatedByUserId);
        Assert.True(cache.Invalidations > 0); // публичный кэш групп обязан сброситься
    }

    [Fact]
    public async Task Sync_AddsNewcomer_RemovesLeaver()
    {
        await using var db = CreateDb(nameof(Sync_AddsNewcomer_RemovesLeaver));
        var w = await SeedWorldAsync(db);
        var stays = await SwimmerAsync(db, w, "Stays", w.A, CurrentSeason);
        var leaves = await SwimmerAsync(db, w, "Leaves", w.A, CurrentSeason);
        var svc = Service(db);
        await svc.SubscribeAsync(w.Group.Id, w.A.Id, null);

        // Новичок выступил за клуб; «ушедшего» результаты переехали в другой клуб (протокол поправили).
        var newcomer = await SwimmerAsync(db, w, "Newcomer", w.A, CurrentSeason);
        foreach (var r in db.Results.Where(r => r.SwimmerId == leaves.Id)) r.ClubId = w.B.Id;
        await db.SaveChangesAsync();

        var sync = await svc.SyncClubsAsync([w.A.Id]);

        Assert.Equal(new HubGroupClubSyncResult(1, 1, 1), sync);
        Assert.Equal(new[] { stays.Id, newcomer.Id }.Order(), (await RowsAsync(db, w.Group.Id)).Select(r => r.SwimmerId).Order());
    }

    [Fact]
    public async Task ManualWins_SubscribeKeepsManual_UnsubscribeKeepsManual()
    {
        await using var db = CreateDb(nameof(ManualWins_SubscribeKeepsManual_UnsubscribeKeepsManual));
        var w = await SeedWorldAsync(db);
        var coach = await SwimmerAsync(db, w, "Coach", w.A, CurrentSeason);
        var clubOnly = await SwimmerAsync(db, w, "ClubOnly", w.A, CurrentSeason);
        Assert.True((await Core(db).AddMemberAsync(w.Group.Id, coach.Id, "coach")).Success);
        var svc = Service(db);

        await svc.SubscribeAsync(w.Group.Id, w.A.Id, null);
        var coachRow = await RowOfAsync(db, w.Group.Id, coach);
        Assert.Equal(HubGroupMemberSource.Manual, coachRow.Source);
        Assert.Equal("coach", coachRow.Role);
        Assert.Equal(HubGroupMemberSource.Club, (await RowOfAsync(db, w.Group.Id, clubOnly)).Source);

        var removed = await svc.UnsubscribeAsync(w.Group.Id);

        Assert.Equal(1, removed!.Removed);
        var left = Assert.Single(await RowsAsync(db, w.Group.Id));
        Assert.Equal(coach.Id, left.SwimmerId);
        Assert.Empty(await db.HubGroupClubSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task Unsubscribe_WithoutSubscription_ReturnsNull()
    {
        await using var db = CreateDb(nameof(Unsubscribe_WithoutSubscription_ReturnsNull));
        var w = await SeedWorldAsync(db);

        Assert.Null(await Service(db).UnsubscribeAsync(w.Group.Id));
    }

    [Fact]
    public async Task Subscribe_OtherClub_ReplacesSubscription_OneGroupOneClub()
    {
        await using var db = CreateDb(nameof(Subscribe_OtherClub_ReplacesSubscription_OneGroupOneClub));
        var w = await SeedWorldAsync(db);
        await SwimmerAsync(db, w, "FromA", w.A, CurrentSeason);
        var fromB = await SwimmerAsync(db, w, "FromB", w.B, CurrentSeason);
        var svc = Service(db);
        await svc.SubscribeAsync(w.Group.Id, w.A.Id, null);

        var result = await svc.SubscribeAsync(w.Group.Id, w.B.Id, null);

        Assert.Equal(new HubGroupClubSyncResult(1, 1, 1), result.Sync);
        Assert.Equal(w.B.Id, (await db.HubGroupClubSubscriptions.SingleAsync()).ClubId);
        Assert.Equal(fromB.Id, Assert.Single(await RowsAsync(db, w.Group.Id)).SwimmerId);
    }

    [Fact]
    public async Task Subscribe_MergedClub_FollowsCanonical()
    {
        await using var db = CreateDb(nameof(Subscribe_MergedClub_FollowsCanonical));
        var w = await SeedWorldAsync(db);
        var dup = new Club { Name = "Hapoel Dolphin", MergedIntoId = w.A.Id };
        db.Clubs.Add(dup);
        await db.SaveChangesAsync();
        var s = await SwimmerAsync(db, w, "S", w.A, CurrentSeason);

        var result = await Service(db).SubscribeAsync(w.Group.Id, dup.Id, null);

        Assert.Equal(w.A.Id, result.Subscription!.ClubId);
        Assert.Equal(s.Id, Assert.Single(await RowsAsync(db, w.Group.Id)).SwimmerId);
    }

    [Fact]
    public async Task Subscribe_UnknownClubOrGroup_Fails()
    {
        await using var db = CreateDb(nameof(Subscribe_UnknownClubOrGroup_Fails));
        var w = await SeedWorldAsync(db);
        var svc = Service(db);

        Assert.Equal("Club not found.", (await svc.SubscribeAsync(w.Group.Id, 12345, null)).Error);
        Assert.Equal("Group not found.", (await svc.SubscribeAsync(12345, w.A.Id, null)).Error);
        Assert.Empty(await db.HubGroupClubSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task SyncClubs_TouchesOnlyGroupsSubscribedToThoseClubs()
    {
        await using var db = CreateDb(nameof(SyncClubs_TouchesOnlyGroupsSubscribedToThoseClubs));
        var w = await SeedWorldAsync(db);
        var other = new HubGroup { Name = "Haifa fans", Slug = "haifa-fans" };
        db.HubGroups.Add(other);
        await db.SaveChangesAsync();
        var svc = Service(db);
        await svc.SubscribeAsync(w.Group.Id, w.A.Id, null);
        await svc.SubscribeAsync(other.Id, w.B.Id, null);

        var newA = await SwimmerAsync(db, w, "NewA", w.A, CurrentSeason);
        await SwimmerAsync(db, w, "NewB", w.B, CurrentSeason);

        var sync = await svc.SyncClubsAsync([w.A.Id]);

        Assert.Equal(1, sync.Groups);
        Assert.Equal(newA.Id, Assert.Single(await RowsAsync(db, w.Group.Id)).SwimmerId);
        Assert.Empty(await RowsAsync(db, other.Id)); // импорт клуба A чужой состав не трогает
    }

    // ── Предпросмотр подписки (П3: блок «Roster from club» в «My groups») ─────

    private static async Task<HubGroup> AddGroupAsync(SwimmDbContext db, string slug, bool official = false, Club? club = null)
    {
        var g = new HubGroup { Name = slug, Slug = slug, IsOfficial = official, ClubId = club?.Id };
        db.HubGroups.Add(g);
        await db.SaveChangesAsync();
        return g;
    }

    [Fact]
    public async Task Preview_NoOfficialNoFollowers_WarnsAboutFuture_NoHint()
    {
        await using var db = CreateDb(nameof(Preview_NoOfficialNoFollowers_WarnsAboutFuture_NoHint));
        var w = await SeedWorldAsync(db);
        await SwimmerAsync(db, w, "S1", w.A, CurrentSeason);
        await SwimmerAsync(db, w, "S2", w.A, PreviousSeason);

        var preview = await Service(db).PreviewAsync(w.Group.Id, w.A.Id);

        Assert.Equal(2, preview!.SwimmerCount);
        Assert.StartsWith("This club has no official group yet.", preview.Warning);
        Assert.Null(preview.Hint);
        Assert.Empty(preview.FollowingGroups);
        Assert.Empty(await db.HubGroupClubSubscriptions.ToListAsync()); // предпросмотр ничего не пишет
    }

    [Fact]
    public async Task Preview_AnotherGroupFollows_HintsToJoinTheBiggest()
    {
        await using var db = CreateDb(nameof(Preview_AnotherGroupFollows_HintsToJoinTheBiggest));
        var w = await SeedWorldAsync(db);
        await SwimmerAsync(db, w, "S1", w.A, CurrentSeason);
        var small = await AddGroupAsync(db, "small-fans");
        var big = await AddGroupAsync(db, "big-fans");
        var svc = Service(db);
        await svc.SubscribeAsync(big.Id, w.A.Id, null);
        await svc.SubscribeAsync(small.Id, w.A.Id, null);
        // «small» — меньше: одного пловца владелец скрыл.
        await svc.SetExcludedAsync(small.Id, (await db.HubGroupMembers.FirstAsync(m => m.HubGroupId == small.Id)).Id, true);

        var preview = await svc.PreviewAsync(w.Group.Id, w.A.Id);

        Assert.Equal(2, preview!.FollowingGroups.Count);
        Assert.Equal(big.Id, preview.HintGroup!.Id);
        Assert.Contains("A group already follows this club", preview.Hint);
        Assert.Contains("big-fans", preview.Hint);
    }

    [Fact]
    public async Task Preview_ClubHasOfficialGroup_WarnsLinkOnly_PointsToOfficial()
    {
        await using var db = CreateDb(nameof(Preview_ClubHasOfficialGroup_WarnsLinkOnly_PointsToOfficial));
        var w = await SeedWorldAsync(db);
        var official = await AddGroupAsync(db, "dolphin-official", official: true, club: w.A);
        var follower = await AddGroupAsync(db, "dolphin-fans");
        await Service(db).SubscribeAsync(follower.Id, w.A.Id, null);

        var preview = await Service(db).PreviewAsync(w.Group.Id, w.A.Id);

        Assert.Equal(official.Id, preview!.OfficialGroup!.Id);
        Assert.Contains("already has an official group", preview.Warning);
        Assert.Contains("dolphin-official", preview.Warning);
        Assert.Equal(official.Id, preview.HintGroup!.Id); // звать — в официальную, не в подписанную
        Assert.StartsWith("Join the official group", preview.Hint);
    }

    [Fact]
    public async Task Preview_OwnOfficialClub_NothingToWarnAbout()
    {
        await using var db = CreateDb(nameof(Preview_OwnOfficialClub_NothingToWarnAbout));
        var w = await SeedWorldAsync(db);
        w.Group.IsOfficial = true;
        w.Group.ClubId = w.A.Id;
        await db.SaveChangesAsync();

        var preview = await Service(db).PreviewAsync(w.Group.Id, w.A.Id);

        Assert.True(preview!.IsOwnOfficialClub);
        Assert.Null(preview.Warning);
        Assert.Null(preview.Hint);
        Assert.Null(preview.OfficialGroup);
    }

    [Fact]
    public async Task Preview_MergedClub_ShowsCanonical_UnknownClub_Null()
    {
        await using var db = CreateDb(nameof(Preview_MergedClub_ShowsCanonical_UnknownClub_Null));
        var w = await SeedWorldAsync(db);
        var dup = new Club { Name = "Hapoel Dolphin", MergedIntoId = w.A.Id };
        db.Clubs.Add(dup);
        await db.SaveChangesAsync();

        var preview = await Service(db).PreviewAsync(w.Group.Id, dup.Id);

        Assert.Equal(w.A.Id, preview!.ClubId);
        Assert.Equal(w.A.Name, preview.ClubName);
        Assert.Null(await Service(db).PreviewAsync(w.Group.Id, 12345));
    }

    // ── Скрытые владельцем клубные пловцы ────────────────────────────────────

    [Fact]
    public async Task Excluded_NotReturnedBySync_AndHiddenFromEveryReader()
    {
        await using var db = CreateDb(nameof(Excluded_NotReturnedBySync_AndHiddenFromEveryReader));
        var w = await SeedWorldAsync(db);
        var hidden = await SwimmerAsync(db, w, "Hidden", w.A, CurrentSeason);
        var visible = await SwimmerAsync(db, w, "Visible", w.A, CurrentSeason);
        var svc = Service(db);
        await svc.SubscribeAsync(w.Group.Id, w.A.Id, null);
        var row = await RowOfAsync(db, w.Group.Id, hidden);

        Assert.True((await svc.SetExcludedAsync(w.Group.Id, row.Id, true)).Success);
        var sync = await svc.SyncGroupAsync(w.Group.Id);

        Assert.Equal(0, sync.Added + sync.Removed);
        var after = await RowOfAsync(db, w.Group.Id, hidden); // одна строка, всё ещё скрыт
        Assert.True(after.IsExcluded);

        // Публичный путь: ростер, счётчик каталога.
        var pub = new HubGroupPublicRepository(db, db, new SettingsStub());
        Assert.Equal([visible.Id], await pub.GetRosterSwimmerIdsAsync(w.Group.Slug));
        Assert.Equal(1, Assert.Single(await pub.GetGroupsAsync()).MemberCount);

        // Админка и «My groups»: счётчик — видимые, форма — все, со скрытым флагом.
        var admin = new HubGroupAdminService(db, Core(db), Mock.Of<IAdminAuditService>());
        Assert.Equal(1, Assert.Single(await admin.GetAllAsync()).MemberCount);
        var form = await admin.GetByIdAsync(w.Group.Id);
        Assert.True(form!.Members.Single(m => m.SwimmerId == hidden.Id).IsExcluded);
        Assert.Equal(w.A.Id, form.ClubSubscription!.ClubId);
        Assert.Equal(1, (await admin.GetDeleteImpactAsync(w.Group.Id))!.Swimmers);
    }

    [Fact]
    public async Task Excluded_CanBeRestored()
    {
        await using var db = CreateDb(nameof(Excluded_CanBeRestored));
        var w = await SeedWorldAsync(db);
        var s = await SwimmerAsync(db, w, "S", w.A, CurrentSeason);
        var svc = Service(db);
        await svc.SubscribeAsync(w.Group.Id, w.A.Id, null);
        var row = await RowOfAsync(db, w.Group.Id, s);
        await svc.SetExcludedAsync(w.Group.Id, row.Id, true);

        Assert.True((await svc.SetExcludedAsync(w.Group.Id, row.Id, false)).Success);

        Assert.False((await RowOfAsync(db, w.Group.Id, s)).IsExcluded);
    }

    [Fact]
    public async Task SetExcluded_ManualRow_Refused()
    {
        await using var db = CreateDb(nameof(SetExcluded_ManualRow_Refused));
        var w = await SeedWorldAsync(db);
        var s = await SwimmerAsync(db, w, "S", w.B, CurrentSeason);
        await Core(db).AddMemberAsync(w.Group.Id, s.Id, "member");
        var row = await RowOfAsync(db, w.Group.Id, s);

        var result = await Service(db).SetExcludedAsync(w.Group.Id, row.Id, true);

        Assert.False(result.Success);
        Assert.False((await RowOfAsync(db, w.Group.Id, s)).IsExcluded);
    }

    [Fact]
    public async Task SetExcluded_MemberOfAnotherGroup_NotFound()
    {
        await using var db = CreateDb(nameof(SetExcluded_MemberOfAnotherGroup_NotFound));
        var w = await SeedWorldAsync(db);
        var s = await SwimmerAsync(db, w, "S", w.A, CurrentSeason);
        var svc = Service(db);
        await svc.SubscribeAsync(w.Group.Id, w.A.Id, null);
        var row = await RowOfAsync(db, w.Group.Id, s);

        // IDOR: id строки чужой группы по адресу своей группы не проходит.
        Assert.Equal("Member not found.", (await svc.SetExcludedAsync(w.Group.Id + 1, row.Id, true)).Error);
    }

    // ── Ручные правки состава рядом с подпиской ──────────────────────────────

    [Fact]
    public async Task RemoveMember_ClubRow_Hides_InsteadOfDeleting()
    {
        await using var db = CreateDb(nameof(RemoveMember_ClubRow_Hides_InsteadOfDeleting));
        var w = await SeedWorldAsync(db);
        var s = await SwimmerAsync(db, w, "S", w.A, CurrentSeason);
        var svc = Service(db);
        await svc.SubscribeAsync(w.Group.Id, w.A.Id, null);
        var row = await RowOfAsync(db, w.Group.Id, s);

        Assert.True((await Core(db).RemoveMemberAsync(w.Group.Id, row.Id)).Success);
        await svc.SyncGroupAsync(w.Group.Id);

        Assert.True((await RowOfAsync(db, w.Group.Id, s)).IsExcluded); // удалённого вернула бы пересборка
    }

    [Fact]
    public async Task RemoveMember_ManualInSubscribedClub_BecomesHiddenClub()
    {
        await using var db = CreateDb(nameof(RemoveMember_ManualInSubscribedClub_BecomesHiddenClub));
        var w = await SeedWorldAsync(db);
        var s = await SwimmerAsync(db, w, "S", w.A, CurrentSeason);
        await Core(db).AddMemberAsync(w.Group.Id, s.Id, "member");
        var svc = Service(db);
        await svc.SubscribeAsync(w.Group.Id, w.A.Id, null);
        var row = await RowOfAsync(db, w.Group.Id, s);
        Assert.Equal(HubGroupMemberSource.Manual, row.Source);

        await Core(db).RemoveMemberAsync(w.Group.Id, row.Id);
        await svc.SyncGroupAsync(w.Group.Id);

        var after = await RowOfAsync(db, w.Group.Id, s);
        Assert.Equal(HubGroupMemberSource.Club, after.Source);
        Assert.True(after.IsExcluded);
    }

    [Fact]
    public async Task RemoveMember_ManualOutsideClub_Deleted()
    {
        await using var db = CreateDb(nameof(RemoveMember_ManualOutsideClub_Deleted));
        var w = await SeedWorldAsync(db);
        var s = await SwimmerAsync(db, w, "S", w.B, CurrentSeason);
        await Core(db).AddMemberAsync(w.Group.Id, s.Id, "member");
        await Service(db).SubscribeAsync(w.Group.Id, w.A.Id, null);
        var row = await RowOfAsync(db, w.Group.Id, s);

        await Core(db).RemoveMemberAsync(w.Group.Id, row.Id);

        Assert.Empty(await RowsAsync(db, w.Group.Id));
    }

    [Fact]
    public async Task AddMember_ExistingHiddenClubRow_BecomesManualAndVisible()
    {
        await using var db = CreateDb(nameof(AddMember_ExistingHiddenClubRow_BecomesManualAndVisible));
        var w = await SeedWorldAsync(db);
        var s = await SwimmerAsync(db, w, "S", w.A, CurrentSeason);
        var svc = Service(db);
        await svc.SubscribeAsync(w.Group.Id, w.A.Id, null);
        await svc.SetExcludedAsync(w.Group.Id, (await RowOfAsync(db, w.Group.Id, s)).Id, true);

        var result = await Core(db).AddMemberAsync(w.Group.Id, s.Id, "captain");
        await svc.UnsubscribeAsync(w.Group.Id);

        Assert.True(result.Success);
        var row = await RowOfAsync(db, w.Group.Id, s); // пережил отписку — ручной
        Assert.Equal(HubGroupMemberSource.Manual, row.Source);
        Assert.False(row.IsExcluded);
        Assert.Equal("captain", row.Role);
    }

    [Fact]
    public async Task AddMember_ExistingManualRow_StillDuplicateError()
    {
        await using var db = CreateDb(nameof(AddMember_ExistingManualRow_StillDuplicateError));
        var w = await SeedWorldAsync(db);
        var s = await SwimmerAsync(db, w, "S", w.B, CurrentSeason);
        await Core(db).AddMemberAsync(w.Group.Id, s.Id, "member");

        var again = await Core(db).AddMemberAsync(w.Group.Id, s.Id, "member");

        Assert.False(again.Success);
        Assert.Single(await RowsAsync(db, w.Group.Id));
    }

    // ── Склейки ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SwimmerMerge_ManualDuplicateWinsOverClubCanonical()
    {
        await using var db = CreateDb(nameof(SwimmerMerge_ManualDuplicateWinsOverClubCanonical));
        var w = await SeedWorldAsync(db);
        var canon = await SwimmerAsync(db, w, "Canon", w.A, CurrentSeason);
        var dup = new Swimmer { LastName = "Canon", FirstName = "F", BirthYear = 2012 };
        db.Swimmers.Add(dup);
        await db.SaveChangesAsync();
        db.HubGroupMembers.AddRange(
            new HubGroupMember { HubGroupId = w.Group.Id, SwimmerId = canon.Id, Source = HubGroupMemberSource.Club, IsExcluded = true },
            new HubGroupMember { HubGroupId = w.Group.Id, SwimmerId = dup.Id, Source = HubGroupMemberSource.Manual, Role = "coach" });
        await db.SaveChangesAsync();

        var report = await new SwimmerMergeService(db).MergeAsync([new SwimmerMergePair(canon.Id, dup.Id)], dryRun: false);

        Assert.Equal("merged", report.Pairs.Single().Status);
        var row = Assert.Single(await RowsAsync(db, w.Group.Id));
        Assert.Equal(canon.Id, row.SwimmerId);
        Assert.Equal(HubGroupMemberSource.Manual, row.Source);
        Assert.False(row.IsExcluded);
        Assert.Equal("coach", row.Role);
    }

    [Fact]
    public async Task SwimmerMerge_BothClub_HiddenIfEitherHidden()
    {
        await using var db = CreateDb(nameof(SwimmerMerge_BothClub_HiddenIfEitherHidden));
        var w = await SeedWorldAsync(db);
        var canon = await SwimmerAsync(db, w, "Canon", w.A, CurrentSeason);
        var dup = new Swimmer { LastName = "Canon", FirstName = "F", BirthYear = 2012 };
        db.Swimmers.Add(dup);
        await db.SaveChangesAsync();
        db.HubGroupMembers.AddRange(
            new HubGroupMember { HubGroupId = w.Group.Id, SwimmerId = canon.Id, Source = HubGroupMemberSource.Club },
            new HubGroupMember { HubGroupId = w.Group.Id, SwimmerId = dup.Id, Source = HubGroupMemberSource.Club, IsExcluded = true });
        await db.SaveChangesAsync();

        await new SwimmerMergeService(db).MergeAsync([new SwimmerMergePair(canon.Id, dup.Id)], dryRun: false);

        var row = Assert.Single(await RowsAsync(db, w.Group.Id));
        Assert.Equal(HubGroupMemberSource.Club, row.Source);
        Assert.True(row.IsExcluded);
    }

    private sealed class NoStandings : IClubStandingService
    {
        public Task<int> RebuildForCompetitionAsync(int competitionId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RebuildAllAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RebuildForClubAsync(int clubId, CancellationToken ct = default) => Task.FromResult(0);
    }

    [Fact]
    public async Task ClubMerge_MovesSubscriptionToCanonical_AndResyncs()
    {
        await using var db = CreateDb(nameof(ClubMerge_MovesSubscriptionToCanonical_AndResyncs));
        var w = await SeedWorldAsync(db);
        var dupClub = new Club { Name = "Hapoel Dolphin" };
        db.Clubs.Add(dupClub);
        await db.SaveChangesAsync();
        var fromDup = await SwimmerAsync(db, w, "FromDup", dupClub, CurrentSeason);
        var fromCanon = await SwimmerAsync(db, w, "FromCanon", w.A, CurrentSeason);
        var svc = Service(db);
        await svc.SubscribeAsync(w.Group.Id, dupClub.Id, null);
        Assert.Equal(fromDup.Id, Assert.Single(await RowsAsync(db, w.Group.Id)).SwimmerId);

        var report = await new ClubMergeService(db, new FakeCache(), new NoStandings(), svc)
            .MergeAsync([new ClubMergePair(w.A.Id, dupClub.Id)], dryRun: false);

        Assert.Equal("merged", report.Pairs.Single().Status);
        Assert.Equal(w.A.Id, (await db.HubGroupClubSubscriptions.SingleAsync()).ClubId);
        // Канон теперь несёт пловцов обоих — подписанная группа получила и «родного» канону.
        Assert.Equal(new[] { fromDup.Id, fromCanon.Id }.Order(), (await RowsAsync(db, w.Group.Id)).Select(r => r.SwimmerId).Order());
    }

    // ── Импорт ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_NewSwimmerOfSubscribedClub_JoinsRoster()
    {
        await using var db = CreateDb(nameof(Import_NewSwimmerOfSubscribedClub_JoinsRoster));
        var w = await SeedWorldAsync(db);
        var svc = Service(db);
        await svc.SubscribeAsync(w.Group.Id, w.A.Id, null);
        Assert.Empty(await RowsAsync(db, w.Group.Id));

        var item = new
        {
            country = "ISR",
            competition = "Autumn meet",
            date = DateTime.UtcNow.AddDays(-1).ToString("dd/MM/yyyy"),
            event_style_name = "Freestyle",
            event_style_len = "50",
            event_style_gender = "male",
            pool_type = "25m",
            position = 1, heat = 1, lane = 1,
            last_name = "Newcomer", first_name = "Tal", birth_year = 2013,
            club = w.A.Name,
            time = "00:30.00"
        };
        var json = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { item })));

        var result = await new JsonImportService(db, new FakeCache(), clubSync: svc).ImportAsync(json);

        var row = Assert.Single(await RowsAsync(db, w.Group.Id));
        Assert.Equal(HubGroupMemberSource.Club, row.Source);
        Assert.Equal("Newcomer", (await db.Swimmers.SingleAsync(s => s.Id == row.SwimmerId)).LastName);
        Assert.Contains(result.DiagnosticLog, l => l.StartsWith("Группы с подпиской на клуб"));
    }
}
