using Microsoft.EntityFrameworkCore;
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
/// «Set up profile» на НАСТОЯЩЕМ Postgres (docs/plans/profile-setup-plan.md, S1): запись идёт
/// ExecuteUpdate, которого InMemory не умеет, а колонки опроса — новые в Sys_AppUsers.
/// Проверяется: опрос сохраняется и на шаги не влияет (Р1), заявка в группу засчитана (Р3),
/// Dismiss прячет пункт и снимается. Пропуск, если PG недоступен. Тест заводит своих
/// пользователей и группу и удаляет их в finally.
/// </summary>
[Collection(LiveDbCollection.Name)]
public class ProfileSetupPgTests
{
    private const string Conn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private static SwimmDbContext? TryCreate()
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql(Conn).Options);
        try { if (!db.Database.CanConnect()) { db.Dispose(); return null; } return db; }
        catch { db.Dispose(); return null; }
    }

    private static ProfileSetupService Service(SwimmDbContext db) => new(
        db,
        new UserFavoriteRepository(db, new StubSettings()),
        new HubGroupUserService(db, new HubGroupCrudCore(db), new StubSettings()));

    private static AppUser NewUser(string tag) => new()
    {
        Email = $"utest-setup-{tag}-{Guid.NewGuid():N}@example.com",
        DisplayName = $"utest setup {tag}",
    };

    [Fact]
    public async Task Survey_IsStored_AndDoesNotChangeSteps()
    {
        await using var db = TryCreate();
        if (db == null) return; // PG недоступен — пропуск

        var user = NewUser("survey");
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        try
        {
            var svc = Service(db);

            var before = await svc.GetAsync(user.Id);
            Assert.NotNull(before);
            Assert.Null(before.Survey.AnsweredAt);
            Assert.Equal(0, before.Done);
            Assert.Equal(3, before.Total);
            Assert.True(before.Visible);

            Assert.True(await svc.SaveSurveyAsync(user.Id,
                new ProfileSurveyRequest { Swimmer = true, Parent = false, Coach = true, Watcher = false }));

            var stored = await db.AppUsers.AsNoTracking().Where(u => u.Id == user.Id).SingleAsync();
            Assert.True(stored.ProfileRoleSwimmer);
            Assert.False(stored.ProfileRoleParent);
            Assert.True(stored.ProfileRoleCoach);
            Assert.False(stored.ProfileRoleWatcher);
            Assert.NotNull(stored.ProfileRolesAnsweredAt);

            var after = await svc.GetAsync(user.Id);
            Assert.True(after!.Survey.Swimmer);
            Assert.True(after.Survey.Coach);
            Assert.NotNull(after.Survey.AnsweredAt);
            // Р1: опрос ни на что не влияет — те же три шага, тот же счётчик.
            Assert.Equal(before.Steps.Select(s => s.Key), after.Steps.Select(s => s.Key));
            Assert.Equal(0, after.Done);
            Assert.True(after.Visible);

            // Снять все галочки — тоже ответ: время остаётся, флаги гаснут.
            Assert.True(await svc.SaveSurveyAsync(user.Id, new ProfileSurveyRequest()));
            var cleared = await svc.GetAsync(user.Id);
            Assert.False(cleared!.Survey.Swimmer || cleared.Survey.Parent || cleared.Survey.Coach || cleared.Survey.Watcher);
            Assert.NotNull(cleared.Survey.AnsweredAt);
        }
        finally
        {
            await db.AppUsers.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Dismiss_HidesItem_AndUndismissReturnsIt()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var user = NewUser("dismiss");
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        try
        {
            var svc = Service(db);

            Assert.True(await svc.SetDismissedAsync(user.Id, true));
            var dismissed = await svc.GetAsync(user.Id);
            Assert.True(dismissed!.Dismissed);
            Assert.False(dismissed.Visible);

            Assert.True(await svc.SetDismissedAsync(user.Id, false));
            var back = await svc.GetAsync(user.Id);
            Assert.False(back!.Dismissed);
            Assert.True(back.Visible);
        }
        finally
        {
            await db.AppUsers.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task AllThreeSteps_PendingGroupCounts_ItemHidden()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var swimmerId = await db.Swimmers.AsNoTracking()
            .Where(s => s.Origin == "isr" && s.PrivateHubGroupId == null)
            .Select(s => (int?)s.Id).FirstOrDefaultAsync();
        var clubId = await db.Clubs.AsNoTracking().Select(c => (int?)c.Id).FirstOrDefaultAsync();
        if (swimmerId == null || clubId == null) return; // пустая база — пропуск

        var user = NewUser("steps");
        var owner = NewUser("owner");
        db.AppUsers.AddRange(user, owner);
        await db.SaveChangesAsync();
        var group = new HubGroup { Name = "utest setup group", Slug = $"utest-setup-{Guid.NewGuid():N}", OwnerUserId = owner.Id };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();
        try
        {
            db.UserFavorites.AddRange(
                new UserFavorite { UserId = user.Id, TargetType = FavoritesRules.TargetSwimmer, SwimmerId = swimmerId, IsPrimary = true },
                new UserFavorite { UserId = user.Id, TargetType = FavoritesRules.TargetClub, ClubId = clubId });
            db.HubGroupUserMembers.Add(new HubGroupUserMember
            {
                HubGroupId = group.Id, UserId = user.Id, Status = HubGroupUserMemberStatus.Pending,
            });
            await db.SaveChangesAsync();

            var s = await Service(db).GetAsync(user.Id);

            Assert.Equal(3, s!.Done);
            Assert.False(s.Visible);
            Assert.False(s.Dismissed);

            var me = s.Steps.Single(x => x.Key == ProfileSetupRules.StepMe);
            Assert.Equal(swimmerId, me.TargetId);
            Assert.False(string.IsNullOrWhiteSpace(me.TargetName));

            Assert.Equal(clubId, s.Steps.Single(x => x.Key == ProfileSetupRules.StepClub).TargetId);

            var g = s.Steps.Single(x => x.Key == ProfileSetupRules.StepGroup);
            Assert.True(g.Done);   // Р3: заявка засчитана
            Assert.Equal(group.Id, g.TargetId);
            Assert.Equal(group.Slug, g.TargetSlug);
            Assert.Equal(HubGroupUserMemberStatus.Pending, g.Status);
        }
        finally
        {
            await db.HubGroupUserMembers.Where(m => m.HubGroupId == group.Id).ExecuteDeleteAsync();
            await db.UserFavorites.Where(f => f.UserId == user.Id).ExecuteDeleteAsync();
            await db.HubGroups.Where(x => x.Id == group.Id).ExecuteDeleteAsync();
            await db.AppUsers.Where(u => u.Id == user.Id || u.Id == owner.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task SkipSteps_CountAsDone_AndUndo()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var user = NewUser("skip");
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        try
        {
            var svc = Service(db);

            // Тренер без своих заплывов и без группы-участия: от двух шагов отказался.
            Assert.True(await svc.SetStepSkippedAsync(user.Id, ProfileSetupRules.StepMe, true));
            Assert.True(await svc.SetStepSkippedAsync(user.Id, ProfileSetupRules.StepGroup, true));

            var s = await svc.GetAsync(user.Id);
            Assert.Equal(2, s!.Done);
            Assert.True(s.Visible); // клуб ещё не отмечен
            Assert.True(s.Steps.Single(x => x.Key == ProfileSetupRules.StepMe).Skipped);
            Assert.False(s.Steps.Single(x => x.Key == ProfileSetupRules.StepClub).Done);

            Assert.True(await svc.SetStepSkippedAsync(user.Id, ProfileSetupRules.StepClub, true));
            var all = await svc.GetAsync(user.Id);
            Assert.Equal(3, all!.Done);
            Assert.False(all.Visible);

            Assert.True(await svc.SetStepSkippedAsync(user.Id, ProfileSetupRules.StepMe, false));
            var undone = await svc.GetAsync(user.Id);
            Assert.Equal(2, undone!.Done);
            Assert.False(undone.Steps.Single(x => x.Key == ProfileSetupRules.StepMe).Done);

            var stored = await db.AppUsers.AsNoTracking().Where(u => u.Id == user.Id).SingleAsync();
            Assert.False(stored.ProfileSkipMe);
            Assert.True(stored.ProfileSkipClub);
            Assert.True(stored.ProfileSkipGroup);
        }
        finally
        {
            await db.AppUsers.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task StepsSeen_WrittenOnComplete_AndOnDismiss()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var done = NewUser("seen-done");
        var dismissed = NewUser("seen-dismiss");
        db.AppUsers.AddRange(done, dismissed);
        await db.SaveChangesAsync();
        try
        {
            var svc = Service(db);
            var snapshot = ProfileSetupRules.SeenSnapshot();

            // Пока не всё — снимка нет; отказался от всех трёх — «готово», снимок записан при чтении.
            await svc.GetAsync(done.Id);
            Assert.Null(await db.AppUsers.Where(u => u.Id == done.Id).Select(u => u.ProfileStepsSeen).SingleAsync());
            foreach (var key in ProfileSetupRules.StepKeys) await svc.SetStepSkippedAsync(done.Id, key, true);
            var status = await svc.GetAsync(done.Id);
            Assert.True(status!.Complete);
            Assert.Empty(status.NewSteps);
            Assert.Equal(snapshot, await db.AppUsers.Where(u => u.Id == done.Id).Select(u => u.ProfileStepsSeen).SingleAsync());

            // Dismiss — тоже снимок.
            await svc.SetDismissedAsync(dismissed.Id, true);
            Assert.Equal(snapshot, await db.AppUsers.Where(u => u.Id == dismissed.Id).Select(u => u.ProfileStepsSeen).SingleAsync());
            Assert.False((await svc.GetAsync(dismissed.Id))!.Visible);
        }
        finally
        {
            await db.AppUsers.Where(u => u.Id == done.Id || u.Id == dismissed.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task UnknownUser_NullAndFalse()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var svc = Service(db);
        Assert.Null(await svc.GetAsync(int.MaxValue));
        Assert.False(await svc.SaveSurveyAsync(int.MaxValue, new ProfileSurveyRequest { Swimmer = true }));
        Assert.False(await svc.SetDismissedAsync(int.MaxValue, true));
    }

    private sealed class StubSettings : ISettingsService
    {
        public IReadOnlyList<AdminSetting> GetAll() => [];
        public AdminSetting? Get(string key) => null;
        public T GetValue<T>(string key, T fallback) => fallback;
        public bool Update(string key, string newValue) => true;
    }
}
