using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Удаление аккаунта на НАСТОЯЩЕМ Postgres (docs/plans/account-deletion-plan.md, У4): всё держится
/// на каскадах и SET NULL базы (миграция AccountDeletionSetNullUserRefs) и на порядке удаления
/// группы с тренировками — InMemory ни того, ни другого не видит. Пропуск, если PG недоступен.
/// Тест заводит своих пользователей, пловцов и группы и подчищает их в finally.
/// </summary>
[Collection(LiveDbCollection.Name)]
public class AccountDeletionPgTests
{
    private const string Conn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private const string Password = "correct horse battery staple";

    private static SwimmDbContext? TryCreate()
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql(Conn).Options);
        try { if (!db.Database.CanConnect()) { db.Dispose(); return null; } return db; }
        catch { db.Dispose(); return null; }
    }

    private sealed record FakeActor(int? UserId, string Name, string? IpAddress) : ICurrentActor;

    private static AccountDeletionService Service(SwimmDbContext db, ICurrentActor actor)
    {
        var audit = new AdminAuditService(db, actor, NullLogger<AdminAuditService>.Instance);
        var groups = new HubGroupAdminService(db, new HubGroupCrudCore(db), audit, Mock.Of<ISettingsService>());
        var localAuth = new LocalAuthService(db, new Argon2PasswordHasher(), Mock.Of<IEmailSender>());
        return new AccountDeletionService(db, groups, localAuth, audit,
            NullLogger<AccountDeletionService>.Instance);
    }

    /// <summary>Временные строки теста — чтобы finally убрал всё, что не удалил сам тест.</summary>
    private sealed class Scratch
    {
        public readonly List<int> Users = new();
        public readonly List<int> Groups = new();
        public readonly List<int> Swimmers = new();
        public readonly string Tag = Guid.NewGuid().ToString("N")[..10];
    }

    private static async Task<AppUser> NewUserAsync(SwimmDbContext db, Scratch s, string tag, bool withPassword)
    {
        var user = new AppUser { Email = $"utest-del-{tag}-{s.Tag}@example.com", DisplayName = $"utest del {tag}" };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        s.Users.Add(user.Id);
        if (withPassword)
        {
            var hasher = new Argon2PasswordHasher();
            db.UserLocalCredentials.Add(new UserLocalCredential
            {
                UserId = user.Id, PasswordHash = hasher.Hash(Password), PasswordAlgorithm = hasher.Algorithm, EmailConfirmed = true,
            });
            await db.SaveChangesAsync();
        }
        return user;
    }

    private static async Task<HubGroup> NewGroupAsync(SwimmDbContext db, Scratch s, AppUser owner, string tag)
    {
        var group = new HubGroup { Name = $"utest del {tag}", Slug = $"utest-del-{tag}-{s.Tag}", OwnerUserId = owner.Id, IsTest = true };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();
        s.Groups.Add(group.Id);
        return group;
    }

    private static async Task<Swimmer> NewSwimmerAsync(SwimmDbContext db, Scratch s, int? privateGroupId = null)
    {
        var swimmer = new Swimmer { LastName = $"Utestdel{s.Tag}", FirstName = "Dan", BirthYear = 2000, Gender = "male" };
        if (privateGroupId is int gid)
        {
            swimmer.Origin = PrivateSwimmerRules.Origin;
            swimmer.PrivateHubGroupId = gid;
        }
        db.Swimmers.Add(swimmer);
        await db.SaveChangesAsync();
        s.Swimmers.Add(swimmer.Id);
        return swimmer;
    }

    private static async Task CleanupAsync(SwimmDbContext db, Scratch s)
    {
        db.ChangeTracker.Clear();
        db.TrainingSessions.RemoveRange(await db.TrainingSessions.Where(x => s.Groups.Contains(x.HubGroupId)).ToListAsync());
        await db.SaveChangesAsync();
        db.HubGroups.RemoveRange(await db.HubGroups.Where(g => s.Groups.Contains(g.Id)).ToListAsync());
        await db.SaveChangesAsync();
        db.AppUsers.RemoveRange(await db.AppUsers.Where(u => s.Users.Contains(u.Id)).ToListAsync());
        await db.SaveChangesAsync();
        db.Swimmers.RemoveRange(await db.Swimmers.Where(x => s.Swimmers.Contains(x.Id)).ToListAsync());
        await db.SaveChangesAsync();
        var userIds = s.Users.Select(id => id.ToString()).ToList();
        var groupIds = s.Groups.Select(id => id.ToString()).ToList();
        await db.AdminAudits
            .Where(a => (a.EntityType == "AppUser" && userIds.Contains(a.EntityId!))
                        || (a.EntityType == "HubGroup" && groupIds.Contains(a.EntityId!))
                        || (a.ActorUserId != null && s.Users.Contains(a.ActorUserId.Value)))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task DeleteSelf_RemovesPersonalData_KeepsContentInOthersGroupsWithoutAuthor()
    {
        await using var db = TryCreate();
        if (db == null) return; // PG недоступен — пропуск
        var s = new Scratch();
        try
        {
            var me = await NewUserAsync(db, s, "me", withPassword: true);
            var owner = await NewUserAsync(db, s, "owner", withPassword: false);
            var theirs = await NewGroupAsync(db, s, owner, "theirs");
            var swimmer = await NewSwimmerAsync(db, s);

            db.UserFavorites.Add(new UserFavorite { UserId = me.Id, TargetType = "swimmer", SwimmerId = swimmer.Id });
            var media = new UserMedia { UserId = me.Id, SwimmerId = swimmer.Id, Level = "swimmer", MediaType = "video", SourceType = "youtube", Url = $"https://youtube.com/watch?v={s.Tag}" };
            db.UserMedia.Add(media);
            db.UserPushSubscriptions.Add(new UserPushSubscription { UserId = me.Id, Endpoint = $"https://push.example.com/{s.Tag}", P256dh = "p", Auth = "a" });
            db.HubGroupUserMembers.Add(new HubGroupUserMember { HubGroupId = theirs.Id, UserId = me.Id });
            // Я выдал админство владельцу? Нет — владелец выдал мне; а я добавил другого участника.
            db.HubGroupAdmins.Add(new HubGroupAdmin { HubGroupId = theirs.Id, UserId = me.Id, GrantedByUserId = owner.Id });
            var other = await NewUserAsync(db, s, "other", withPassword: false);
            db.HubGroupUserMembers.Add(new HubGroupUserMember { HubGroupId = theirs.Id, UserId = other.Id, AddedByUserId = me.Id });
            db.HubGroupAdmins.Add(new HubGroupAdmin { HubGroupId = theirs.Id, UserId = other.Id, GrantedByUserId = me.Id });
            var link = new HubGroupMedia { HubGroupId = theirs.Id, MediaType = "image", SourceType = "other", Url = $"https://e.com/{s.Tag}.jpg", CreatedByUserId = me.Id };
            db.HubGroupMedia.Add(link);
            db.HubGroupTrainingRsvps.Add(new HubGroupTrainingRsvp { HubGroupId = theirs.Id, UserId = me.Id, SessionDate = new DateOnly(2026, 10, 1), SessionStart = "07:00" });
            swimmer.LogligIdSuggestedByUserId = me.Id;
            await db.SaveChangesAsync();
            db.UserMediaPublications.Add(new UserMediaPublication { UserMediaId = media.Id, HubGroupId = theirs.Id, Status = UserMediaPublicationStatus.Approved });
            db.UserReactions.Add(new UserReaction { UserId = owner.Id, Kind = "like", MediaId = media.Id }); // чужая реакция на моё медиа
            await db.SaveChangesAsync();

            var svc = Service(db, new FakeActor(me.Id, me.Email, "10.0.0.1"));
            // Старая строка аудита, где я актор, — email в ней должен исчезнуть (Р5).
            db.AdminAudits.Add(new AdminAudit { ActorUserId = me.Id, ActorName = me.Email, IpAddress = "10.0.0.1", Action = "hubgroup.media-add", EntityType = "HubGroup", EntityId = theirs.Id.ToString(), Summary = "x" });
            await db.SaveChangesAsync();

            var impact = (await svc.GetImpactAsync(me.Id))!;
            Assert.Empty(impact.BlockReasons);
            Assert.True(impact.HasPassword);
            Assert.Equal(1, impact.Favorites);
            Assert.Equal(1, impact.Media);
            Assert.Equal(1, impact.PublishedMedia);
            Assert.Equal(1, impact.GroupMemberships);
            Assert.Equal(1, impact.GroupAdminships);
            Assert.Equal(1, impact.PushDevices);
            Assert.Equal(1, impact.GroupContentKept);
            Assert.Empty(impact.GroupsDeleted);

            var result = await svc.DeleteSelfAsync(me.Id,
                new AccountDeletionRequest { ConfirmEmail = me.Email.ToUpperInvariant(), Password = Password }, authTimeUtc: null);
            Assert.Equal(204, result.Status);

            db.ChangeTracker.Clear();
            Assert.False(await db.AppUsers.AnyAsync(u => u.Id == me.Id));
            Assert.False(await db.UserFavorites.AnyAsync(f => f.UserId == me.Id));
            Assert.False(await db.UserMedia.AnyAsync(m => m.UserId == me.Id));
            Assert.False(await db.UserMediaPublications.AnyAsync(p => p.UserMediaId == media.Id));
            Assert.False(await db.UserReactions.AnyAsync(r => r.MediaId == media.Id));
            Assert.False(await db.UserPushSubscriptions.AnyAsync(p => p.UserId == me.Id));
            Assert.False(await db.HubGroupUserMembers.AnyAsync(m => m.UserId == me.Id));
            Assert.False(await db.HubGroupAdmins.AnyAsync(a => a.UserId == me.Id));
            Assert.False(await db.HubGroupTrainingRsvps.AnyAsync(r => r.UserId == me.Id));
            Assert.False(await db.UserLocalCredentials.AnyAsync(c => c.UserId == me.Id));

            // Чужая группа и то, что я в неё добавил, — на месте, без автора (Р4, SET NULL).
            Assert.True(await db.HubGroups.AnyAsync(g => g.Id == theirs.Id));
            Assert.Null((await db.HubGroupMedia.SingleAsync(m => m.Id == link.Id)).CreatedByUserId);
            Assert.Null((await db.HubGroupUserMembers.SingleAsync(m => m.HubGroupId == theirs.Id && m.UserId == other.Id)).AddedByUserId);
            Assert.Null((await db.HubGroupAdmins.SingleAsync(a => a.HubGroupId == theirs.Id && a.UserId == other.Id)).GrantedByUserId);
            Assert.Null((await db.Swimmers.SingleAsync(x => x.Id == swimmer.Id)).LogligIdSuggestedByUserId);

            // Аудит: user.delete есть, и ни в одной строке, где я актор, нет моего email и IP (Р5).
            var mine = await db.AdminAudits.Where(a => a.ActorUserId == me.Id).ToListAsync();
            Assert.Contains(mine, a => a.Action == "user.delete" && a.EntityId == me.Id.ToString());
            Assert.All(mine, a =>
            {
                Assert.Equal(AccountDeletionService.DeletedActorName(me.Id), a.ActorName);
                Assert.Null(a.IpAddress);
                Assert.DoesNotContain(me.Email, a.Summary, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(me.Email, a.DetailsJson ?? "", StringComparison.OrdinalIgnoreCase);
            });
        }
        finally { await CleanupAsync(db, s); }
    }

    [Fact]
    public async Task DeleteSelf_OwnSoloGroup_GoesWithAccount_IncludingGroupSwimmersAndTrainingResults()
    {
        await using var db = TryCreate();
        if (db == null) return;
        var s = new Scratch();
        try
        {
            var me = await NewUserAsync(db, s, "solo", withPassword: false);
            var group = await NewGroupAsync(db, s, me, "solo");
            var local = await NewSwimmerAsync(db, s, privateGroupId: group.Id);
            db.HubGroupMembers.Add(new HubGroupMember { HubGroupId = group.Id, SwimmerId = local.Id, Source = "private" });
            // Я сам — участник своей группы: это не «другие люди».
            db.HubGroupUserMembers.Add(new HubGroupUserMember { HubGroupId = group.Id, UserId = me.Id });
            var session = new TrainingSession { HubGroupId = group.Id, ExternalTrainingId = s.Tag, Date = DateTime.UtcNow, PoolType = "25m", CreatedByUserId = me.Id };
            db.TrainingSessions.Add(session);
            await db.SaveChangesAsync();
            // RESTRICT Sys_TrainingResults → пловец: без удаления тренировок раньше группы падает.
            var styleId = await db.Styles.Select(x => x.Id).FirstAsync();
            db.TrainingResults.Add(new TrainingResult { SessionId = session.Id, SwimmerId = local.Id, StyleId = styleId, Distance = "50", Gender = "male", TimeOriginal = "0:40.00" });
            await db.SaveChangesAsync();

            var svc = Service(db, new FakeActor(me.Id, me.Email, null));
            var impact = (await svc.GetImpactAsync(me.Id, DateTime.UtcNow))!;
            Assert.Empty(impact.BlockReasons);
            Assert.Empty(impact.SharedGroups);
            Assert.Equal(group.Id, Assert.Single(impact.GroupsDeleted).Id);
            Assert.True(impact.SignInFresh);
            Assert.Equal(0, impact.GroupMemberships); // своя группа не в счёт

            // Google-only без свежего входа — нельзя; свежий вход — можно (Р3).
            var stale = await svc.DeleteSelfAsync(me.Id, new AccountDeletionRequest { ConfirmEmail = me.Email }, DateTime.UtcNow.AddHours(-1));
            Assert.Equal(403, stale.Status);
            Assert.Equal("sign-in-again", stale.Code);

            var ok = await svc.DeleteSelfAsync(me.Id, new AccountDeletionRequest { ConfirmEmail = me.Email }, DateTime.UtcNow);
            Assert.Equal(204, ok.Status);

            db.ChangeTracker.Clear();
            Assert.False(await db.AppUsers.AnyAsync(u => u.Id == me.Id));
            Assert.False(await db.HubGroups.AnyAsync(g => g.Id == group.Id));
            Assert.False(await db.Swimmers.AnyAsync(x => x.Id == local.Id));
            Assert.False(await db.TrainingSessions.AnyAsync(x => x.Id == session.Id));
        }
        finally { await CleanupAsync(db, s); }
    }

    [Fact]
    public async Task DeleteSelf_OwnerOfSharedGroup_Blocked_NothingDeleted()
    {
        await using var db = TryCreate();
        if (db == null) return;
        var s = new Scratch();
        try
        {
            var me = await NewUserAsync(db, s, "shared", withPassword: true);
            var member = await NewUserAsync(db, s, "member", withPassword: false);
            var group = await NewGroupAsync(db, s, me, "shared");
            db.HubGroupUserMembers.Add(new HubGroupUserMember { HubGroupId = group.Id, UserId = member.Id, AddedByUserId = me.Id });
            await db.SaveChangesAsync();

            var svc = Service(db, new FakeActor(me.Id, me.Email, null));
            var impact = (await svc.GetImpactAsync(me.Id))!;
            Assert.Equal([AccountDeletionRules.ReasonSharedGroup], impact.BlockReasons);
            Assert.Equal(1, Assert.Single(impact.SharedGroups).OtherPeople);

            var result = await svc.DeleteSelfAsync(me.Id, new AccountDeletionRequest { ConfirmEmail = me.Email, Password = Password }, null);
            Assert.Equal(409, result.Status);
            Assert.Equal(AccountDeletionRules.ReasonSharedGroup, result.Code);

            db.ChangeTracker.Clear();
            Assert.True(await db.AppUsers.AnyAsync(u => u.Id == me.Id));
            Assert.True(await db.HubGroups.AnyAsync(g => g.Id == group.Id));

            // Админ сайта по письму решил удалить и группу (Р7).
            var byAdmin = await Service(db, new FakeActor(null, "cli", null))
                .DeleteByAdminAsync(me.Id, "письмо privacy@", deleteSharedGroups: true);
            Assert.Equal(204, byAdmin.Status);
            db.ChangeTracker.Clear();
            Assert.False(await db.AppUsers.AnyAsync(u => u.Id == me.Id));
            Assert.False(await db.HubGroups.AnyAsync(g => g.Id == group.Id));
            Assert.True(await db.AppUsers.AnyAsync(u => u.Id == member.Id));
        }
        finally { await CleanupAsync(db, s); }
    }

    /// <summary>
    /// Проверка пароля при удалении — не оракул для подбора: неудачи идут в тот же счётчик, что у
    /// входа, и после 5-й аккаунт заблокирован — даже верный пароль не принимается 15 минут.
    /// </summary>
    [Fact]
    public async Task DeleteSelf_WrongPasswords_ShareLoginLockout_EvenRightPasswordRefused()
    {
        await using var db = TryCreate();
        if (db == null) return;
        var s = new Scratch();
        try
        {
            var me = await NewUserAsync(db, s, "brute", withPassword: true);
            var svc = Service(db, new FakeActor(me.Id, me.Email, null));
            var wrong = new AccountDeletionRequest { ConfirmEmail = me.Email, Password = "guess" };

            for (var i = 1; i <= 4; i++)
                Assert.Equal("wrong-password", (await svc.DeleteSelfAsync(me.Id, wrong, DateTime.UtcNow)).Code);
            Assert.Equal("locked-out", (await svc.DeleteSelfAsync(me.Id, wrong, DateTime.UtcNow)).Code);

            var right = await svc.DeleteSelfAsync(me.Id, new AccountDeletionRequest { ConfirmEmail = me.Email, Password = Password }, DateTime.UtcNow);
            Assert.Equal(403, right.Status);
            Assert.Equal("locked-out", right.Code);

            db.ChangeTracker.Clear();
            Assert.True(await db.AppUsers.AnyAsync(u => u.Id == me.Id));
            var cred = await db.UserLocalCredentials.SingleAsync(c => c.UserId == me.Id);
            Assert.NotNull(cred.LockoutEnd); // и обычный вход теперь заблокирован тем же полем
            // 5 неверных + попытка во время блокировки — всё в журнале входов (виден в /Admin/Users).
            Assert.Equal(6, await db.UserLoginHistory.CountAsync(h => h.UserId == me.Id && !h.Success));
        }
        finally { await CleanupAsync(db, s); }
    }

    [Fact]
    public async Task DeleteSelf_WrongConfirmation_Or_SiteAdmin_NothingDeleted()
    {
        await using var db = TryCreate();
        if (db == null) return;
        var s = new Scratch();
        try
        {
            var me = await NewUserAsync(db, s, "guard", withPassword: true);
            var svc = Service(db, new FakeActor(me.Id, me.Email, null));

            var wrongEmail = await svc.DeleteSelfAsync(me.Id, new AccountDeletionRequest { ConfirmEmail = "x@example.com", Password = Password }, null);
            Assert.Equal(403, wrongEmail.Status);
            Assert.Equal("email-mismatch", wrongEmail.Code);

            var wrongPassword = await svc.DeleteSelfAsync(me.Id, new AccountDeletionRequest { ConfirmEmail = me.Email, Password = "nope" }, DateTime.UtcNow);
            Assert.Equal(403, wrongPassword.Status); // свежий вход пароль не заменяет, если он есть
            Assert.Equal("wrong-password", wrongPassword.Code);

            var adminRole = await db.AppRoles.FirstOrDefaultAsync(r => r.Name == "Admin");
            if (adminRole != null)
            {
                db.AppUserRoles.Add(new AppUserRole { UserId = me.Id, RoleId = adminRole.Id });
                await db.SaveChangesAsync();
                var admin = await svc.DeleteSelfAsync(me.Id, new AccountDeletionRequest { ConfirmEmail = me.Email, Password = Password }, null);
                Assert.Equal(409, admin.Status);
                Assert.Equal(AccountDeletionRules.ReasonSiteAdmin, admin.Code);
            }

            db.ChangeTracker.Clear();
            Assert.True(await db.AppUsers.AnyAsync(u => u.Id == me.Id));
        }
        finally { await CleanupAsync(db, s); }
    }
}
