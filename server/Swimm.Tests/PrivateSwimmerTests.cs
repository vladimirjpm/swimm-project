using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
/// Пловец группы (Р71): человек без аккаунта и без loglig, которого тренер заводит для дорожек и
/// тренировок. Существует только внутри своей группы — снаружи его нет нигде: ни страницы, ни
/// поиска, ни общего ответа страницы группы, ни избранного, ни чужих групп. Поэтому «Горбенко»
/// или имя с матом, заведённые тренером, никто снаружи не увидит.
/// </summary>
public class PrivateSwimmerTests
{
    private static SwimmReadDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class SettingsStub : ISettingsService
    {
        public IReadOnlyList<AdminSetting> GetAll() => [];
        public AdminSetting? Get(string key) => null;
        public T GetValue<T>(string key, T fallback) => fallback;
        public bool Update(string key, string newValue) => true;
    }

    private sealed class NullCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private sealed record FakeActor(int? UserId, string Name, string? IpAddress) : ICurrentActor;

    private static HubGroupAdminService Admin(SwimmDbContext db) =>
        new(db, new HubGroupCrudCore(db),
            new AdminAuditService(db, new FakeActor(1, "coach@example.com", null), NullLogger<AdminAuditService>.Instance),
            new SettingsStub());

    private sealed class World
    {
        public HubGroup Group = null!, Other = null!;
        public Swimmer Public = null!, Private = null!;
    }

    /// <summary>Группа с публичным пловцом и пловцом группы «Gorbenko» (заведён тренером).</summary>
    private static async Task<World> SeedAsync(SwimmReadDbContext db)
    {
        var owner = new AppUser { Email = "coach@example.com", DisplayName = "Coach", SecurityStamp = "s" };
        db.AppUsers.Add(owner);
        await db.SaveChangesAsync();
        var w = new World
        {
            Group = new HubGroup { Name = "Dolphin", Slug = "dolphin", OwnerUserId = owner.Id },
            Other = new HubGroup { Name = "Other", Slug = "other", OwnerUserId = owner.Id },
            Public = new Swimmer { LastName = "Cohen", FirstName = "Dan", BirthYear = 2000 },
        };
        db.AddRange(w.Group, w.Other, w.Public);
        await db.SaveChangesAsync();
        db.HubGroupMembers.Add(new HubGroupMember { HubGroupId = w.Group.Id, SwimmerId = w.Public.Id });
        await db.SaveChangesAsync();

        var added = await new HubGroupCrudCore(db).AddPrivateSwimmerAsync(w.Group.Id, " Anastasia ", "Gorbenko", "female", 2003);
        Assert.True(added.Success, added.Error);
        w.Private = await db.Swimmers.SingleAsync(s => s.Id == added.SwimmerId);
        return w;
    }

    // ── Правила ввода ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "Cohen", null, null, "First and last name are required")]
    [InlineData("Dan", "  ", null, null, "First and last name are required")]
    [InlineData("Dan", "Cohen", "other", null, "Gender must be male or female")]
    [InlineData("Dan", "Cohen", null, 1800, "Birth year must be between 1920 and 2026")]
    public void Normalize_RejectsBadInput(string first, string last, string? gender, int? year, string error)
    {
        Assert.Equal(error, PrivateSwimmerRules.Normalize(first, last, gender, year, 2026).Error);
    }

    [Fact]
    public void Normalize_TrimsAndCollapsesSpaces()
    {
        var r = PrivateSwimmerRules.Normalize("  Dan  Ben ", "Cohen\t", "Male", null, 2026);
        Assert.Null(r.Error);
        Assert.Equal("Dan Ben", r.First);
        Assert.Equal("Cohen", r.Last);
        Assert.Equal("male", r.Gender);
        Assert.Equal(0, r.BirthYear);
    }

    [Fact]
    public void Normalize_TooLongName()
    {
        Assert.NotNull(PrivateSwimmerRules.Normalize(new string('a', 51), "Cohen", null, null, 2026).Error);
    }

    // ── Создание ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Add_CreatesLocalSwimmerOfThisGroup_InRoster()
    {
        await using var db = CreateDb(nameof(Add_CreatesLocalSwimmerOfThisGroup_InRoster));
        var w = await SeedAsync(db);

        Assert.Equal("Anastasia", w.Private.FirstName);
        Assert.Equal(PrivateSwimmerRules.Origin, w.Private.Origin);
        Assert.Equal(w.Group.Id, w.Private.PrivateHubGroupId);
        Assert.Null(w.Private.ClubId);
        Assert.True(await db.HubGroupMembers.AnyAsync(m => m.HubGroupId == w.Group.Id && m.SwimmerId == w.Private.Id
            && m.Source == HubGroupMemberSource.Private));
    }

    [Fact]
    public async Task Add_RespectsManualRosterQuota()
    {
        await using var db = CreateDb(nameof(Add_RespectsManualRosterQuota));
        var w = await SeedAsync(db);  // в составе уже 2 ручные строки

        var r = await new HubGroupCrudCore(db).AddPrivateSwimmerAsync(w.Group.Id, "Noa", "Levi", null, null, maxManual: 2);

        Assert.False(r.Success);
        Assert.Equal(2, await db.Swimmers.CountAsync());  // пловец не заведён
    }

    [Fact]
    public async Task OtherGroup_CannotAddOrLabelPrivateSwimmer()
    {
        await using var db = CreateDb(nameof(OtherGroup_CannotAddOrLabelPrivateSwimmer));
        var w = await SeedAsync(db);

        var add = await new HubGroupCrudCore(db).AddMemberAsync(w.Other.Id, w.Private.Id, "member");

        Assert.False(add.Success);
        Assert.False(await db.HubGroupMembers.AnyAsync(m => m.HubGroupId == w.Other.Id));
    }

    // ── Снаружи его нет ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GroupPage_SharedPayloadHidesPrivate_InsidersGetItSeparately()
    {
        await using var db = CreateDb(nameof(GroupPage_SharedPayloadHidesPrivate_InsidersGetItSeparately));
        var w = await SeedAsync(db);
        var repo = new HubGroupPublicRepository(db, db, new SettingsStub());

        var page = await repo.GetPageAsync(w.Group.Id, w.Group.Slug);
        var insiders = await repo.GetPrivateMembersAsync(w.Group.Id);
        var rosterIds = await repo.GetRosterSwimmerIdsAsync(w.Group.Slug);

        Assert.Equal([w.Public.Id], page!.Members.Select(m => m.SwimmerId));
        Assert.DoesNotContain(page.Standings, s => s.SwimmerId == w.Private.Id);
        Assert.Equal([w.Public.Id], rosterIds!);
        var p = Assert.Single(insiders);
        Assert.Equal(w.Private.Id, p.SwimmerId);
        Assert.True(p.IsPrivate);
        Assert.Equal("member", p.Role);
    }

    [Fact]
    public async Task SwimmerPage_404_AndSearchesSkipIt()
    {
        await using var db = CreateDb(nameof(SwimmerPage_404_AndSearchesSkipIt));
        var w = await SeedAsync(db);

        Assert.Null(await new ResultRepository(db, new NullCacheService()).GetSwimmerProfileAsync(w.Private.Id));
        Assert.NotNull(await new ResultRepository(db, new NullCacheService()).GetSwimmerProfileAsync(w.Public.Id));

        // InMemory не знает ILIKE — проверяем фильтр по построению запроса поиска тренера
        // косвенно: пустой запрос не ищет, а поиск по фамилии — Postgres-only. Остаётся карточка клуба.
        var club = new Club { Name = "Dolphin club" };
        db.Clubs.Add(club);
        await db.SaveChangesAsync();
        w.Private.ClubId = club.Id;
        w.Public.ClubId = club.Id;
        await db.SaveChangesAsync();

        var clubSwimmers = await Admin(db).GetClubSwimmersAsync(club.Id);
        Assert.Equal([w.Public.Id], clubSwimmers.Select(s => s.Id));
    }

    [Fact]
    public async Task Favorites_RefusePrivateSwimmer()
    {
        await using var db = CreateDb(nameof(Favorites_RefusePrivateSwimmer));
        var w = await SeedAsync(db);
        var owner = await db.AppUsers.SingleAsync();

        var result = await new UserFavoriteRepository(db, new SettingsStub()).AddAsync(owner.Id,
            new AddFavoriteRequest { TargetType = FavoritesRules.TargetSwimmer, SwimmerId = w.Private.Id });

        Assert.Equal(AddFavoriteStatus.NotFound, result.Status);
        Assert.Empty(db.UserFavorites);
    }

    [Fact]
    public async Task Dedup_NeverProposesPrivateSwimmer()
    {
        await using var db = CreateDb(nameof(Dedup_NeverProposesPrivateSwimmer));
        var w = await SeedAsync(db);
        // Настоящая Горбенко в справочнике — тот самый случай «завёл себе Горбенко».
        db.Swimmers.Add(new Swimmer { LastName = "Gorbenko", FirstName = "Anastasia", BirthYear = 2003, Gender = "female" });
        await db.SaveChangesAsync();

        var report = await new SwimmerDedupService(db).FindCandidatesAsync();

        Assert.DoesNotContain(report.Candidates,
            c => c.CanonicalId == w.Private.Id || c.DuplicateId == w.Private.Id);
    }

    // ── Удаление ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveFromRoster_DeletesPrivateSwimmerWithTrainingTimes()
    {
        await using var db = CreateDb(nameof(RemoveFromRoster_DeletesPrivateSwimmerWithTrainingTimes));
        var w = await SeedAsync(db);
        var session = new TrainingSession { HubGroupId = w.Group.Id, ExternalTrainingId = "1", Date = DateTime.UtcNow, PoolType = "25m" };
        db.TrainingSessions.Add(session);
        await db.SaveChangesAsync();
        db.TrainingResults.Add(new TrainingResult { SessionId = session.Id, SwimmerId = w.Private.Id, Distance = "50", Gender = "female", TimeOriginal = "0:40.00" });
        await db.SaveChangesAsync();
        var memberId = await db.HubGroupMembers.Where(m => m.SwimmerId == w.Private.Id).Select(m => m.Id).SingleAsync();

        var result = await new HubGroupCrudCore(db).RemoveMemberAsync(w.Group.Id, memberId);

        Assert.True(result.Success);
        db.ChangeTracker.Clear();
        Assert.False(await db.Swimmers.AnyAsync(s => s.Id == w.Private.Id));
        Assert.Empty(db.TrainingResults);
        Assert.True(await db.Swimmers.AnyAsync(s => s.Id == w.Public.Id));
    }

    /// <summary>
    /// «Неактивен» вместо удаления (Влад, 29.09.2026): строка скрыта — пловца нет в составе для
    /// своих и на дорожках, но он и его времена целы; Restore возвращает. Обычного ручного
    /// пловца так не прячут — его удаляют.
    /// </summary>
    [Fact]
    public async Task Deactivate_HidesFromInsiders_KeepsSwimmer_RestoreBringsBack()
    {
        await using var db = CreateDb(nameof(Deactivate_HidesFromInsiders_KeepsSwimmer_RestoreBringsBack));
        var w = await SeedAsync(db);
        var core = new HubGroupCrudCore(db);
        var subs = new HubGroupClubSubscriptionService(db, core);
        var repo = new HubGroupPublicRepository(db, db, new SettingsStub());
        var privateRow = await db.HubGroupMembers.SingleAsync(m => m.SwimmerId == w.Private.Id);
        var publicRow = await db.HubGroupMembers.SingleAsync(m => m.SwimmerId == w.Public.Id);

        Assert.True((await subs.SetExcludedAsync(w.Group.Id, privateRow.Id, true)).Success);
        Assert.Empty(await repo.GetPrivateMembersAsync(w.Group.Id));
        Assert.True(await db.Swimmers.AnyAsync(s => s.Id == w.Private.Id));

        Assert.True((await subs.SetExcludedAsync(w.Group.Id, privateRow.Id, false)).Success);
        Assert.Single(await repo.GetPrivateMembersAsync(w.Group.Id));

        Assert.False((await subs.SetExcludedAsync(w.Group.Id, publicRow.Id, true)).Success);
    }

    [Fact]
    public async Task DeleteImpact_CountsPrivateSwimmers()
    {
        await using var db = CreateDb(nameof(DeleteImpact_CountsPrivateSwimmers));
        var w = await SeedAsync(db);

        var impact = (await Admin(db).GetDeleteImpactAsync(w.Group.Id))!;

        Assert.Equal(2, impact.Swimmers);
        Assert.Equal(1, impact.PrivateSwimmers);
    }
}
