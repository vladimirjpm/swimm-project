using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// «Официальная группа — главная» (П4 плана docs/plans/hubgroup-club-subscription-plan.md):
/// копии клуба уходят из каталога (по ссылке работают), совпавшие имена получают « · community»
/// при одобрении, имя клуба с официальной группой неофициальной не сохранить, владелец копии
/// видит плашку. «Не в каталоге» считается на лету — сняли статус, копия вернулась, а имя нет.
/// </summary>
public class HubGroupOfficialPrimaryTests
{
    private static SwimmReadDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NullCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private sealed class NullEmail : IEmailSender
    {
        public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingAudit : IAdminAuditService
    {
        public List<(string Action, string? Summary)> Entries { get; } = [];
        public Task LogAsync(string action, string entityType, string? entityId = null, string? summary = null,
            object? details = null, CancellationToken ct = default)
        {
            Entries.Add((action, summary));
            return Task.CompletedTask;
        }
    }

    private sealed class SettingsStub : ISettingsService
    {
        public IReadOnlyList<AdminSetting> GetAll() => [];
        public AdminSetting? Get(string key) => null;
        public T GetValue<T>(string key, T fallback) => fallback;
        public bool Update(string key, string newValue) => true;
    }

    private const string ClubHe = "הפועל דולפין נתניה";
    private const string ClubEn = "Hapoel Dolphine Netanya";

    private sealed class World
    {
        public AppUser Owner = null!;
        public Club Club = null!, OtherClub = null!;
    }

    private static async Task<World> SeedAsync(SwimmDbContext db)
    {
        var w = new World
        {
            Owner = new AppUser { Email = "owner@example.com", DisplayName = "Owner", SecurityStamp = "s" },
            Club = new Club { Name = ClubHe, NameEn = ClubEn },
            OtherClub = new Club { Name = "מכבי חיפה", NameEn = "Maccabi Haifa" }
        };
        db.AddRange(w.Owner, w.Club, w.OtherClub);
        await db.SaveChangesAsync();
        return w;
    }

    private static async Task<HubGroup> GroupAsync(SwimmDbContext db, World w, string name, string? nameEn = null,
        bool official = false, Club? followsClub = null)
    {
        var g = new HubGroup
        {
            Name = name, NameEn = nameEn, Slug = Guid.NewGuid().ToString("N"), OwnerUserId = w.Owner.Id,
            IsOfficial = official, ClubId = official ? w.Club.Id : null
        };
        db.HubGroups.Add(g);
        await db.SaveChangesAsync();
        if (followsClub != null)
        {
            db.HubGroupClubSubscriptions.Add(new HubGroupClubSubscription { HubGroupId = g.Id, ClubId = followsClub.Id });
            await db.SaveChangesAsync();
        }
        return g;
    }

    private static HubGroupPublicRepository PublicRepo(SwimmReadDbContext db) => new(db, db, new SettingsStub());

    private static async Task<HashSet<string>> CatalogAsync(SwimmReadDbContext db) =>
        (await PublicRepo(db).GetGroupsAsync()).Select(g => g.Name).ToHashSet();

    // ── Нормализация имени ───────────────────────────────────────────────────

    [Theory]
    [InlineData("הַפּוֹעֵל  דולפין-נתניה", ClubHe)]           // огласовки, двойной пробел, дефис
    [InlineData("  Hapoel Dolphine-Netanya ", ClubEn)]        // регистр, крайние пробелы, дефис
    [InlineData("איל\"ן חיפה", "אילן חיפה")]                  // кавычка вместо гершаима
    [InlineData("איל״ן חיפה", "איל\"ן חיפה")]                 // гершаим против кавычки
    [InlineData("M.T.A", "m t a")]
    public void NormalizeName_SameClubName(string a, string b)
    {
        Assert.Equal(HubGroupClubRules.NormalizeName(b), HubGroupClubRules.NormalizeName(a));
    }

    [Theory]
    [InlineData("Dolphin Netanya Fans", ClubEn)]              // похожие имена — не наша забота
    [InlineData("הפועל דולפין נתניה · community", ClubHe)]    // суффикс снимает совпадение
    public void NormalizeName_DifferentNames(string a, string b)
    {
        Assert.NotEqual(HubGroupClubRules.NormalizeName(b), HubGroupClubRules.NormalizeName(a));
    }

    [Fact]
    public void ConflictsWith_EmptyNameNeverConflicts()
    {
        Assert.False(HubGroupClubRules.ConflictsWith(null, [ClubHe, null, ""]));
        Assert.False(HubGroupClubRules.ConflictsWith("  ", [ClubHe, "", null]));
    }

    [Fact]
    public void WithCommunitySuffix_FitsTheColumn()
    {
        Assert.Equal(ClubHe + HubGroupClubRules.CommunitySuffix, HubGroupClubRules.WithCommunitySuffix(ClubHe, 200));

        var longName = new string('א', 200);
        var renamed = HubGroupClubRules.WithCommunitySuffix(longName, 200);
        Assert.Equal(200, renamed.Length);
        Assert.EndsWith(HubGroupClubRules.CommunitySuffix, renamed);
    }

    // ── Каталог ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Catalog_HidesCopiesOfClubWithOfficialGroup_KeepsOfficialAndOthers()
    {
        await using var db = CreateDb(nameof(Catalog_HidesCopiesOfClubWithOfficialGroup_KeepsOfficialAndOthers));
        var w = await SeedAsync(db);
        await GroupAsync(db, w, "Official", official: true, followsClub: w.Club); // официальная подписана на свой клуб
        var copy = await GroupAsync(db, w, "Dolphin fans", followsClub: w.Club);
        await GroupAsync(db, w, "Haifa fans", followsClub: w.OtherClub);        // у Хайфы официальной нет
        await GroupAsync(db, w, "Masters");                                      // состав руками

        var catalog = await CatalogAsync(db);

        Assert.Equal(new HashSet<string> { "Official", "Haifa fans", "Masters" }, catalog);
        // По ссылке копия работает.
        Assert.NotNull(await PublicRepo(db).GetBySlugAsync(copy.Slug));
    }

    [Fact]
    public async Task Catalog_OfficialStatusRemoved_CopyReturns()
    {
        await using var db = CreateDb(nameof(Catalog_OfficialStatusRemoved_CopyReturns));
        var w = await SeedAsync(db);
        var official = await GroupAsync(db, w, "Official", official: true);
        await GroupAsync(db, w, "Dolphin fans", followsClub: w.Club);
        Assert.DoesNotContain("Dolphin fans", await CatalogAsync(db));

        official.IsOfficial = false;
        official.ClubId = null;
        await db.SaveChangesAsync();

        Assert.Contains("Dolphin fans", await CatalogAsync(db)); // считается на лету — колонки нет
    }

    // ── Одобрение: переименование и последствия ─────────────────────────────

    private static async Task<HubGroupClubRequest> RequestAsync(SwimmDbContext db, World w, HubGroup group)
    {
        var request = new HubGroupClubRequest { HubGroupId = group.Id, UserId = w.Owner.Id, ClubId = w.Club.Id };
        db.HubGroupClubRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    private static HubGroupClubRequestAdminService Approver(SwimmDbContext db, RecordingAudit? audit = null) =>
        new(db, new NullCache(), new NullEmail(), audit: audit);

    [Fact]
    public async Task Approve_RenamesAnyGroupNamedLikeTheClubOrOfficial_NotOthers()
    {
        await using var db = CreateDb(nameof(Approve_RenamesAnyGroupNamedLikeTheClubOrOfficial_NotOthers));
        var w = await SeedAsync(db);
        var future = await GroupAsync(db, w, "Dolphin Netanya Official", "Dolphins");
        var sameName = await GroupAsync(db, w, "הַפּוֹעֵל דולפין-נתניה");                  // имя клуба, без подписки
        var sameEn = await GroupAsync(db, w, "דולפינים", "hapoel dolphine netanya");         // латиница клуба
        var sameAsOfficial = await GroupAsync(db, w, "dolphin netanya official", followsClub: w.Club);
        var unrelated = await GroupAsync(db, w, "Dolphin Netanya Fans", followsClub: w.Club);
        var request = await RequestAsync(db, w, future);
        var audit = new RecordingAudit();

        Assert.True((await Approver(db, audit).ApproveAsync(request.Id, 0)).Success);

        async Task<HubGroup> Reload(HubGroup g) => await db.HubGroups.AsNoTracking().SingleAsync(x => x.Id == g.Id);
        Assert.Equal("הַפּוֹעֵל דולפין-נתניה" + HubGroupClubRules.CommunitySuffix, (await Reload(sameName)).Name);
        var en = await Reload(sameEn);
        Assert.Equal("דולפינים", en.Name);                                                    // иврит не совпал — не трогаем
        Assert.Equal("hapoel dolphine netanya" + HubGroupClubRules.CommunitySuffix, en.NameEn);
        Assert.EndsWith(HubGroupClubRules.CommunitySuffix, (await Reload(sameAsOfficial)).Name);
        Assert.Equal("Dolphin Netanya Fans", (await Reload(unrelated)).Name);                  // похожее не ловим
        Assert.Equal("Dolphin Netanya Official", (await Reload(future)).Name);                // официальную не трогаем

        // Slug прежний — «доступна по ссылке» не ломается.
        Assert.Equal(sameName.Slug, (await Reload(sameName)).Slug);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("hubgroup.official.approve", entry.Action);
        Assert.Contains("переименованы", entry.Summary);
    }

    [Fact]
    public async Task OfficialStatusRemoved_RenameStays()
    {
        await using var db = CreateDb(nameof(OfficialStatusRemoved_RenameStays));
        var w = await SeedAsync(db);
        var future = await GroupAsync(db, w, "Official");
        var copy = await GroupAsync(db, w, ClubHe, followsClub: w.Club);
        await Approver(db).ApproveAsync((await RequestAsync(db, w, future)).Id, 0);

        var tracked = await db.HubGroups.SingleAsync(g => g.Id == future.Id);
        tracked.IsOfficial = false;
        tracked.ClubId = null;
        await db.SaveChangesAsync();

        // В каталог вернулась, имя — нет: переименование разовое.
        Assert.Contains(ClubHe + HubGroupClubRules.CommunitySuffix, await CatalogAsync(db));
    }

    [Fact]
    public async Task PendingRequest_ShowsImpactBeforeApprove()
    {
        await using var db = CreateDb(nameof(PendingRequest_ShowsImpactBeforeApprove));
        var w = await SeedAsync(db);
        var future = await GroupAsync(db, w, "Official");
        await GroupAsync(db, w, "Dolphin fans", followsClub: w.Club);
        await GroupAsync(db, w, ClubEn);
        await RequestAsync(db, w, future);

        var row = Assert.Single(await Approver(db).GetAllAsync());

        Assert.Equal(["Dolphin fans"], row.ApproveImpact!.LeaveCatalog);
        var renamed = Assert.Single(row.ApproveImpact.Renamed);
        Assert.Contains(ClubEn + HubGroupClubRules.CommunitySuffix, renamed);
        Assert.Equal("Dolphin fans", (await db.HubGroups.SingleAsync(g => g.Name == "Dolphin fans")).Name); // ничего не записано
    }

    // ── Имя клуба занято официальной группой ─────────────────────────────────

    private static HubGroupInputDto Input(string name, string? nameEn = null) =>
        new() { Name = name, NameEn = nameEn, IsPublic = true };

    [Fact]
    public async Task Validate_ClubNameTakenOnceOfficialExists_SuffixAllowed()
    {
        await using var db = CreateDb(nameof(Validate_ClubNameTakenOnceOfficialExists_SuffixAllowed));
        var w = await SeedAsync(db);
        var core = new HubGroupCrudCore(db, new NullCache());

        // До официальной имя клуба разрешено.
        Assert.Null(await core.ValidateAsync(Input(ClubHe), "slug-1", excludeId: null));

        await GroupAsync(db, w, "Dolphin official", official: true);

        var error = await core.ValidateAsync(Input("הַפּוֹעֵל דולפין נתניה"), "slug-2", excludeId: null);
        Assert.NotNull(error);
        Assert.Contains("official group", error);
        Assert.Contains(HubGroupClubRules.CommunitySuffix.Trim(), error); // подсказывает рабочий вариант
        Assert.NotNull(await core.ValidateAsync(Input("Fans", ClubEn), "slug-3", excludeId: null));
        Assert.NotNull(await core.ValidateAsync(Input("dolphin official"), "slug-4", excludeId: null)); // имя официальной
        Assert.Null(await core.ValidateAsync(Input(ClubHe + HubGroupClubRules.CommunitySuffix), "slug-5", excludeId: null));
    }

    [Fact]
    public async Task Validate_OnlyChangedNameIsChecked_OfficialMayKeepClubName()
    {
        await using var db = CreateDb(nameof(Validate_OnlyChangedNameIsChecked_OfficialMayKeepClubName));
        var w = await SeedAsync(db);
        var official = await GroupAsync(db, w, ClubHe, ClubEn, official: true);
        // Имя получено ДО одобрения официальной и не переименовано (было до П4).
        var legacy = await GroupAsync(db, w, "Dolphins", ClubEn);
        var core = new HubGroupCrudCore(db, new NullCache());

        // Правка без смены имени — проходит; смена имени на имя клуба — нет.
        Assert.Null(await core.ValidateAsync(Input("Dolphins", ClubEn), legacy.Slug, legacy.Id));
        Assert.NotNull(await core.ValidateAsync(Input(ClubHe, ClubEn), legacy.Slug, legacy.Id));
        // Самой официальной имя клуба можно.
        Assert.Null(await core.ValidateAsync(Input(ClubHe, ClubEn), official.Slug, official.Id));
    }

    // ── Плашка в «My groups» и шапка копии ───────────────────────────────────

    [Fact]
    public async Task MyGroups_CopyOfClubWithOfficial_GetsNoticeAndLink()
    {
        await using var db = CreateDb(nameof(MyGroups_CopyOfClubWithOfficial_GetsNoticeAndLink));
        var w = await SeedAsync(db);
        var official = await GroupAsync(db, w, "Dolphin official", official: true, followsClub: w.Club);
        var copy = await GroupAsync(db, w, "Dolphin fans", followsClub: w.Club);
        var haifa = await GroupAsync(db, w, "Haifa fans", followsClub: w.OtherClub);
        var svc = new HubGroupUserService(db, new HubGroupCrudCore(db, new NullCache()), new SettingsStub());

        var rows = (await svc.GetMineAsync(w.Owner.Id)).ToDictionary(r => r.Id);

        Assert.True(rows[copy.Id].HiddenByOfficialGroup);
        Assert.Equal(official.Slug, rows[copy.Id].OfficialGroupSlug);
        Assert.StartsWith("Not in the catalog", rows[copy.Id].CatalogNotice);
        Assert.Contains("Dolphin official", rows[copy.Id].CatalogNotice);
        Assert.Equal(ClubHe, rows[copy.Id].FollowedClubName);

        Assert.False(rows[official.Id].HiddenByOfficialGroup); // сама себя не прячет
        Assert.False(rows[haifa.Id].HiddenByOfficialGroup);
        Assert.Equal("מכבי חיפה", rows[haifa.Id].FollowedClubName);
    }

    [Fact]
    public async Task PublicPage_CopyPointsToOfficial_OfficialPointsNowhere()
    {
        await using var db = CreateDb(nameof(PublicPage_CopyPointsToOfficial_OfficialPointsNowhere));
        var w = await SeedAsync(db);
        var official = await GroupAsync(db, w, "Dolphin official", official: true, followsClub: w.Club);
        var copy = await GroupAsync(db, w, "Dolphin fans", followsClub: w.Club);

        var copyPage = await PublicRepo(db).GetBySlugAsync(copy.Slug);
        var officialPage = await PublicRepo(db).GetBySlugAsync(official.Slug);

        Assert.Equal(w.Club.Id, copyPage!.FollowedClubId);
        Assert.Equal(ClubHe, copyPage.FollowedClubName);
        Assert.Equal(official.Slug, copyPage.OfficialGroupSlug);
        Assert.Equal("Dolphin official", copyPage.OfficialGroupName);
        Assert.Null(officialPage!.OfficialGroupSlug);
    }
}
