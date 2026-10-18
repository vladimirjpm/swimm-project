using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// «Set up profile» (docs/plans/profile-setup-plan.md). Что засчитано — решает
/// <see cref="ProfileSetupRules"/>; здесь только сбор фактов и запись колонок опроса.
///
/// Факты берутся готовыми ручками, а не своими запросами: имя пловца и клуба — из
/// <see cref="IUserFavoriteRepository"/> (там правило «иврит по умолчанию», копии разъехаться нельзя),
/// группы — из <see cref="IHubGroupUserService.GetJoinedAsync"/> (тот же список, что «участвую»).
/// Избранного у человека не больше лимита (30 пловцов + 3 клуба), групп — единицы.
/// </summary>
public sealed class ProfileSetupService(
    SwimmDbContext db,
    IUserFavoriteRepository favorites,
    IHubGroupUserService groups) : IProfileSetupService
{
    public async Task<ProfileSetupDto?> GetAsync(int userId)
    {
        var user = await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.ProfileRoleSwimmer, u.ProfileRoleParent, u.ProfileRoleCoach, u.ProfileRoleWatcher,
                u.ProfileRolesAnsweredAt, u.ProfileSetupDismissedAt,
                u.ProfileSkipMe, u.ProfileSkipClub, u.ProfileSkipGroup, u.ProfileSkipFamily, u.ProfileStepsSeen,
            })
            .FirstOrDefaultAsync();
        if (user == null) return null;

        var favs = await favorites.GetForUserAsync(userId);
        var me = favs.FirstOrDefault(f => f.IsPrimary && f.SwimmerId != null);
        var club = favs.FirstOrDefault(f => f.TargetType == FavoritesRules.TargetClub && f.ClubId != null);
        // Семья — до FavoritesRules.MaxFamily пловцов; в шаге и итоге — имена через запятую.
        var family = favs.Where(f => f.IsFamily && f.SwimmerId != null).ToList();
        var clubs = favs.Where(f => f.TargetType == FavoritesRules.TargetClub && f.ClubId != null).ToList();

        // Заявка (pending) засчитывает шаг (Р3), но показать лучше группу, где уже приняли.
        var joined = (await groups.GetJoinedAsync(userId))
            .OrderBy(g => g.Status == HubGroupUserMemberStatus.Active ? 0 : 1)
            .ToList();
        var group = joined.FirstOrDefault();

        // Итог рисует строку на КАЖДЫЙ клуб и группу (ex4, Р10) — отдаём все цели шага.
        static ProfileSetupTargetDto Swimmer(FavoriteDto f) =>
            new() { Id = f.SwimmerId!.Value, FavoriteId = f.Id, Name = f.SwimmerName ?? $"#{f.SwimmerId}" };

        var dismissed = user.ProfileSetupDismissedAt != null;
        var status = ProfileSetupRules.Evaluate(
            new ProfileSetupFacts(HasMe: me != null, HasClub: club != null, InGroup: group != null,
                HasFamily: family.Count > 0),
            new ProfileSetupSkips(user.ProfileSkipMe, user.ProfileSkipClub, user.ProfileSkipGroup, user.ProfileSkipFamily),
            dismissed,
            ProfileSetupRules.ParseSeen(user.ProfileStepsSeen),
            parent: user.ProfileRoleParent);

        // Всё заполнено — запомнить, какие шаги человек видел: появится новый шаг — он придёт как
        // «New: …» (хендофф ex2, 3b). Пишем, только когда снимок устарел, — обычно ни разу.
        var snapshot = ProfileSetupRules.SeenSnapshot();
        if (status.Complete && user.ProfileStepsSeen != snapshot)
            await db.AppUsers.Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.ProfileStepsSeen, snapshot));

        return new ProfileSetupDto
        {
            Survey = new ProfileSurveyDto
            {
                Swimmer = user.ProfileRoleSwimmer,
                Parent = user.ProfileRoleParent,
                Coach = user.ProfileRoleCoach,
                Watcher = user.ProfileRoleWatcher,
                AnsweredAt = user.ProfileRolesAnsweredAt,
            },
            Steps = status.Steps.Select(s => s.Key switch
            {
                ProfileSetupRules.StepMe => new ProfileSetupStepDto
                {
                    Key = s.Key, Done = s.Done, Skipped = s.Skipped, TargetId = me?.SwimmerId, TargetName = me?.SwimmerName,
                    Targets = me == null ? [] : [Swimmer(me)],
                },
                ProfileSetupRules.StepClub => new ProfileSetupStepDto
                {
                    Key = s.Key, Done = s.Done, Skipped = s.Skipped, TargetId = club?.ClubId, TargetName = club?.ClubName,
                    Targets = clubs.Select(c => new ProfileSetupTargetDto
                    {
                        Id = c.ClubId!.Value, FavoriteId = c.Id, Name = c.ClubName ?? $"#{c.ClubId}",
                    }).ToList(),
                },
                ProfileSetupRules.StepFamily => new ProfileSetupStepDto
                {
                    Key = s.Key, Done = s.Done, Skipped = s.Skipped,
                    TargetName = family.Count == 0 ? null
                        : string.Join(", ", family.Select(f => f.SwimmerName ?? $"#{f.SwimmerId}")),
                    Targets = family.Select(Swimmer).ToList(),
                },
                _ => new ProfileSetupStepDto
                {
                    Key = s.Key, Done = s.Done, Skipped = s.Skipped, TargetId = group?.Id, TargetName = group?.Name,
                    TargetSlug = group?.Slug, Status = group?.Status,
                    Targets = joined.Select(g => new ProfileSetupTargetDto
                    {
                        Id = g.Id, Name = g.Name, Slug = g.Slug, Status = g.Status,
                    }).ToList(),
                },
            }).ToList(),
            Done = status.Done,
            Total = status.Total,
            Visible = status.Visible,
            Complete = status.Complete,
            NewSteps = status.NewSteps ?? [],
            Dismissed = dismissed,
        };
    }

    // Запись — ExecuteUpdate мимо трекера, как отметка «был онлайн»: SaveChanges сбросил бы метку
    // строки Sys_AppUsers, а из этой таблицы собраны кэшированные страницы групп. Колонки опроса и
    // Dismiss не читает ни одна кэшируемая страница, поэтому и сбрасывать нечего.

    public async Task<bool> SaveSurveyAsync(int userId, ProfileSurveyRequest answer)
    {
        var now = DateTime.UtcNow;
        var rows = await db.AppUsers
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.ProfileRoleSwimmer, answer.Swimmer)
                .SetProperty(u => u.ProfileRoleParent, answer.Parent)
                .SetProperty(u => u.ProfileRoleCoach, answer.Coach)
                .SetProperty(u => u.ProfileRoleWatcher, answer.Watcher)
                .SetProperty(u => u.ProfileRolesAnsweredAt, now));
        return rows > 0;
    }

    public async Task<bool> SetStepSkippedAsync(int userId, string stepKey, bool skipped)
    {
        var q = db.AppUsers.Where(u => u.Id == userId);
        var rows = stepKey switch
        {
            ProfileSetupRules.StepMe => await q.ExecuteUpdateAsync(s => s.SetProperty(u => u.ProfileSkipMe, skipped)),
            ProfileSetupRules.StepClub => await q.ExecuteUpdateAsync(s => s.SetProperty(u => u.ProfileSkipClub, skipped)),
            ProfileSetupRules.StepGroup => await q.ExecuteUpdateAsync(s => s.SetProperty(u => u.ProfileSkipGroup, skipped)),
            ProfileSetupRules.StepFamily => await q.ExecuteUpdateAsync(s => s.SetProperty(u => u.ProfileSkipFamily, skipped)),
            _ => throw new ArgumentOutOfRangeException(nameof(stepKey), stepKey, "Unknown profile setup step"),
        };
        return rows > 0;
    }

    public async Task<bool> SetDismissedAsync(int userId, bool dismissed)
    {
        DateTime? value = dismissed ? DateTime.UtcNow : null;
        var q = db.AppUsers.Where(u => u.Id == userId);
        // Dismiss — тоже «видел эти шаги»: следующий новый шаг вернёт пункт, а нынешние — нет.
        var rows = dismissed
            ? await q.ExecuteUpdateAsync(s => s
                .SetProperty(u => u.ProfileSetupDismissedAt, value)
                .SetProperty(u => u.ProfileStepsSeen, ProfileSetupRules.SeenSnapshot()))
            : await q.ExecuteUpdateAsync(s => s.SetProperty(u => u.ProfileSetupDismissedAt, value));
        return rows > 0;
    }
}
