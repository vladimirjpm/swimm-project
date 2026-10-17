using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Тестовые персонажи и тестовые группы (см. <see cref="IPersonaSeeder"/>,
/// docs/plans/test-personas-plan.md). Всё, что создаётся, — utest-аккаунты и группы с
/// IsTest: на прод это не переносится (server/db/seed-tables.txt), в каталоге и чужим не видно.
///
/// Кто в каком состоянии — ДАННЫЕ в <see cref="TestPersonas"/> (<see cref="TestPersona"/>,
/// <see cref="TestPersonas.Groups"/>): сидер их выставляет, <see cref="GetPersonaChangesAsync"/> с ними
/// сравнивает, <see cref="ResetPersonaAsync"/> к ним возвращает. Персонажа меняют там, не здесь.
///
/// ⚠ Пловцов НЕ заводим: таблица Swimmers едет на прод. Состав тест-групп и «дети» родителя —
/// существующие пловцы, выбранные один раз; повторный прогон их не перевыбирает.
/// </summary>
public class PersonaSeeder : IPersonaSeeder
{
    /// <summary>Сколько пловцов в составе тест-групп.</summary>
    private const int RosterSize = 6;

    private const string TrainingExternalId = "utest-training-1";

    private readonly SwimmDbContext _db;

    public PersonaSeeder(SwimmDbContext db) => _db = db;

    public async Task<IReadOnlyList<string>> SeedAsync(bool reset = false)
    {
        var log = new List<string>();
        if (reset) await ResetAsync(log);

        var users = await EnsureUsersAsync(log);

        var groups = new Dictionary<string, HubGroup>();
        foreach (var g in TestPersonas.Groups)
            groups[g.Slug] = await EnsureGroupAsync(g, users[g.OwnerNick], log);

        var openGroup = groups[TestPersonas.OpenGroupSlug];
        var roster = await EnsureRosterAsync(groups.Values.ToList(), log);

        foreach (var persona in TestPersonas.All)
        {
            var user = users[persona.Nick];

            // Активное участие добавил владелец группы, заявку (pending) — подал сам персонаж.
            foreach (var m in persona.Memberships)
            {
                var group = groups[m.GroupSlug];
                var owner = users[TestPersonas.Groups.First(g => g.Slug == m.GroupSlug).OwnerNick];
                await EnsureUserMemberAsync(group, user, m.Status,
                    m.Status == HubGroupUserMemberStatus.Active ? owner : null, log);
            }

            foreach (var slug in persona.AdminOf)
                await EnsureGroupAdminAsync(groups[slug], user, users[TestPersonas.Groups.First(g => g.Slug == slug).OwnerNick], log);

            for (var i = 0; i < persona.Favorites.Count; i++)
            {
                var f = persona.Favorites[i];
                await EnsureFavoriteAsync(user, roster[f.RosterIndex],
                    isPrimary: f.Level == PersonaFavoriteLevel.Me, isFamily: f.Level == PersonaFavoriteLevel.Family, sortOrder: i, log);
            }

            if (persona.Media.Count > 0)
            {
                var decider = users[TestPersonas.Groups.First(g => g.Slug == TestPersonas.OpenGroupSlug).OwnerNick];
                await EnsureMediaAsync(user, persona.Media, roster, openGroup, decider, log);
            }
        }

        await EnsureTrainingAsync(openGroup, roster, log);

        log.Add($"готово: персонажей {users.Count}, тест-группы {string.Join(", ", groups.Keys.Select(s => $"«{s}»"))}");
        return log;
    }

    public async Task<IReadOnlyList<string>?> ResetPersonaAsync(int userId)
    {
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId);
        var persona = TestPersonas.ByEmail(user?.Email);
        if (user == null || persona == null) return null;

        var log = new List<string>();

        // Группы, которые персонаж создал сам. Тест-группы из описания не трогаем: на них держится
        // состояние остальных персонажей. Удаление группы каскадом уносит её состав, участников,
        // админов, тренировки и публикации.
        var seeded = TestPersonas.Groups.Select(g => g.Slug).ToList();
        var ownGroups = await _db.HubGroups
            .Where(g => g.OwnerUserId == userId && !seeded.Contains(g.Slug))
            .ToListAsync();
        _db.HubGroups.RemoveRange(ownGroups);
        await _db.SaveChangesAsync();

        // Всё, что висит на самом аккаунте. Через трекер (а не ExecuteDelete) — так перехватчик
        // кэша сбросит страницы, где это показывалось. Медиа уносит свои публикации, жалобы и
        // реакции каскадом. Журнал входов, внешние логины и пароль — не состояние персонажа.
        _db.UserFavorites.RemoveRange(await _db.UserFavorites.Where(x => x.UserId == userId).ToListAsync());
        _db.UserMedia.RemoveRange(await _db.UserMedia.Where(x => x.UserId == userId).ToListAsync());
        _db.UserReactions.RemoveRange(await _db.UserReactions.Where(x => x.UserId == userId).ToListAsync());
        _db.MediaReports.RemoveRange(await _db.MediaReports.Where(x => x.ReporterUserId == userId).ToListAsync());
        _db.UserStartListPlans.RemoveRange(await _db.UserStartListPlans.Where(x => x.UserId == userId).ToListAsync());
        _db.HubGroupUserMembers.RemoveRange(await _db.HubGroupUserMembers.Where(x => x.UserId == userId).ToListAsync());
        _db.HubGroupAdmins.RemoveRange(await _db.HubGroupAdmins.Where(x => x.UserId == userId).ToListAsync());
        _db.HubGroupClubRequests.RemoveRange(await _db.HubGroupClubRequests.Where(x => x.UserId == userId).ToListAsync());
        _db.HubGroupTrainingRsvps.RemoveRange(await _db.HubGroupTrainingRsvps.Where(x => x.UserId == userId).ToListAsync());
        _db.HubGroupBreaks.RemoveRange(await _db.HubGroupBreaks.Where(x => x.UserId == userId).ToListAsync());
        _db.HubGroupAccountLevels.RemoveRange(await _db.HubGroupAccountLevels.Where(x => x.UserId == userId).ToListAsync());
        _db.HubGroupUserSeen.RemoveRange(await _db.HubGroupUserSeen.Where(x => x.UserId == userId).ToListAsync());
        _db.UserSecurityTokens.RemoveRange(await _db.UserSecurityTokens.Where(x => x.UserId == userId).ToListAsync());
        // Роли — тоже: админ мог выдать лишнюю в /Admin/Users; сидер вернёт положенные.
        _db.AppUserRoles.RemoveRange(await _db.AppUserRoles.Where(x => x.UserId == userId).ToListAsync());

        // Поля, которых нет в описании персонажа, — как у только что созданного аккаунта.
        // IsActive и HubGroupLimit выставит сидер по описанию.
        user.DisplayName = persona.Nick;
        user.AvatarUrl = null;
        user.SwimmerId = null;
        user.ProfileRoleSwimmer = user.ProfileRoleParent = user.ProfileRoleCoach = user.ProfileRoleWatcher = false;
        user.ProfileRolesAnsweredAt = null;
        user.ProfileSetupDismissedAt = null;
        user.ProfileSkipMe = user.ProfileSkipClub = user.ProfileSkipGroup = false;
        // Роли менялись — сессии персонажа (если входил по-настоящему) перечитают claims.
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await _db.SaveChangesAsync();
        log.Add($"сброс {persona.Nick} (#{userId}): удалено своих групп {ownGroups.Count}, связи аккаунта очищены");

        // Сидер идемпотентен: вернёт персонажу его роли, участие, избранное и медиа, остальных не тронет.
        log.AddRange(await SeedAsync());
        return log;
    }

    /// <summary>
    /// Отличия персонажей от их описания в <see cref="TestPersonas"/>. Ожидаемое берётся из того же
    /// описания, по которому работает сидер, — зеркала в коде нет. Сторож — тест «после сида никто
    /// не изменён» (PersonaSeederTests.PersonaChanges_*).
    /// </summary>
    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> GetPersonaChangesAsync()
    {
        var emails = TestPersonas.All.Select(p => p.Email.ToLowerInvariant()).ToList();
        var users = await _db.AppUsers.AsNoTracking()
            .Where(u => emails.Contains(u.Email.ToLower()))
            .ToListAsync();
        var ids = users.Select(u => u.Id).ToList();

        var slugs = TestPersonas.Groups.Select(g => g.Slug).ToList();
        var groups = await _db.HubGroups.AsNoTracking()
            .Where(g => slugs.Contains(g.Slug))
            .Select(g => new { g.Id, g.Slug })
            .ToListAsync();
        var slugOf = groups.ToDictionary(g => g.Id, g => g.Slug);
        var openId = groups.FirstOrDefault(g => g.Slug == TestPersonas.OpenGroupSlug)?.Id;
        var roster = openId == null
            ? new List<int>()
            : await _db.HubGroupMembers.AsNoTracking().Where(m => m.HubGroupId == openId)
                .OrderBy(m => m.SortOrder).Select(m => m.SwimmerId).ToListAsync();

        var roles = await _db.AppUserRoles.AsNoTracking().Where(r => ids.Contains(r.UserId))
            .Select(r => new { r.UserId, r.Role.Name }).ToListAsync();
        var owned = await _db.HubGroups.AsNoTracking().Where(g => ids.Contains(g.OwnerUserId))
            .Select(g => new { g.OwnerUserId, g.Slug }).ToListAsync();
        var members = await _db.HubGroupUserMembers.AsNoTracking().Where(m => ids.Contains(m.UserId))
            .Select(m => new { m.UserId, m.HubGroupId, m.Status }).ToListAsync();
        var admins = await _db.HubGroupAdmins.AsNoTracking().Where(a => ids.Contains(a.UserId))
            .Select(a => new { a.UserId, a.HubGroupId }).ToListAsync();
        var favs = await _db.UserFavorites.AsNoTracking().Where(f => ids.Contains(f.UserId))
            .Select(f => new { f.UserId, f.TargetType, f.SwimmerId, f.ClubId, f.IsPrimary, f.IsFamily }).ToListAsync();
        var media = await _db.UserMedia.AsNoTracking().Where(m => ids.Contains(m.UserId))
            .Select(m => new { m.UserId, m.Url, m.Visibility, m.SwimmerId }).ToListAsync();
        var publications = await _db.UserMediaPublications.AsNoTracking().Where(p => ids.Contains(p.Media!.UserId))
            .Select(p => new { p.Media!.UserId, p.Media.Url, p.HubGroupId, p.Level, p.Status }).ToListAsync();

        // Таблицы, где у персонажа по описанию пусто (описание их не знает вовсе).
        var extra = new List<(int UserId, string What)>();
        extra.AddRange((await _db.UserReactions.AsNoTracking().Where(x => ids.Contains(x.UserId)).Select(x => x.UserId).ToListAsync()).Select(u => (u, "реакции")));
        extra.AddRange((await _db.MediaReports.AsNoTracking().Where(x => ids.Contains(x.ReporterUserId)).Select(x => x.ReporterUserId).ToListAsync()).Select(u => (u, "жалобы на медиа")));
        extra.AddRange((await _db.UserStartListPlans.AsNoTracking().Where(x => ids.Contains(x.UserId)).Select(x => x.UserId).ToListAsync()).Select(u => (u, "планы стартового протокола")));
        extra.AddRange((await _db.HubGroupClubRequests.AsNoTracking().Where(x => ids.Contains(x.UserId)).Select(x => x.UserId).ToListAsync()).Select(u => (u, "заявки на клуб")));
        extra.AddRange((await _db.HubGroupTrainingRsvps.AsNoTracking().Where(x => x.UserId != null && ids.Contains(x.UserId.Value)).Select(x => x.UserId!.Value).ToListAsync()).Select(u => (u, "ответы на тренировки")));
        extra.AddRange((await _db.HubGroupBreaks.AsNoTracking().Where(x => x.UserId != null && ids.Contains(x.UserId.Value)).Select(x => x.UserId!.Value).ToListAsync()).Select(u => (u, "перерывы в группе")));
        extra.AddRange((await _db.HubGroupAccountLevels.AsNoTracking().Where(x => ids.Contains(x.UserId)).Select(x => x.UserId).ToListAsync()).Select(u => (u, "уровни в группе")));

        static string Set(IEnumerable<string> items) => string.Join(", ", items.OrderBy(x => x, StringComparer.Ordinal));
        string Swimmer(int rosterIndex) => rosterIndex < roster.Count ? roster[rosterIndex].ToString() : $"состав[{rosterIndex}]";
        string GroupOf(int id) => slugOf.GetValueOrDefault(id, "#" + id);

        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var user in users)
        {
            var p = TestPersonas.ByEmail(user.Email)!;
            var diff = new List<string>();
            void Check(string what, IEnumerable<string> expected, IEnumerable<string> actual)
            {
                if (Set(expected) != Set(actual)) diff.Add(what);
            }

            // Аккаунт: из описания — активность и лимит; остальное — как у нового (ResetPersonaAsync).
            if (user.DisplayName != p.Nick || user.AvatarUrl != null || user.SwimmerId != null) diff.Add("имя / фото / привязка");
            if (user.IsActive != p.IsActive) diff.Add("активность");
            if (user.HubGroupLimit != p.HubGroupLimit) diff.Add("лимит групп");
            if (user.ProfileRoleSwimmer || user.ProfileRoleParent || user.ProfileRoleCoach || user.ProfileRoleWatcher
                || user.ProfileRolesAnsweredAt != null) diff.Add("опрос «Who are you?»");
            if (user.ProfileSkipMe || user.ProfileSkipClub || user.ProfileSkipGroup || user.ProfileSetupDismissedAt != null)
                diff.Add("Set up profile (отказы / Dismiss)");

            Check("роли", p.Roles, roles.Where(r => r.UserId == user.Id).Select(r => r.Name));
            Check("свои группы",
                TestPersonas.Groups.Where(g => g.OwnerNick == p.Nick).Select(g => g.Slug),
                owned.Where(g => g.OwnerUserId == user.Id).Select(g => g.Slug));
            Check("участие в группах",
                p.Memberships.Select(m => $"{m.GroupSlug}:{m.Status}"),
                members.Where(m => m.UserId == user.Id).Select(m => $"{GroupOf(m.HubGroupId)}:{m.Status}"));
            Check("админство в группах", p.AdminOf, admins.Where(a => a.UserId == user.Id).Select(a => GroupOf(a.HubGroupId)));
            Check("избранное",
                p.Favorites.Select(f => $"swimmer:{Swimmer(f.RosterIndex)}:{f.Level}"),
                favs.Where(f => f.UserId == user.Id).Select(f => f.TargetType == FavoritesRules.TargetClub
                    ? $"club:{f.ClubId}"
                    : $"swimmer:{f.SwimmerId}:{(f.IsPrimary ? PersonaFavoriteLevel.Me : f.IsFamily ? PersonaFavoriteLevel.Family : PersonaFavoriteLevel.Favorite)}"));
            Check("медиа",
                p.Media.Select(m => $"{TestPersonas.MediaUrlPrefix}{m.Key}:{m.Visibility}:{Swimmer(m.RosterIndex)}"),
                media.Where(m => m.UserId == user.Id).Select(m => $"{m.Url}:{m.Visibility}:{m.SwimmerId}"));
            Check("публикации медиа",
                p.Media.Where(m => m.PublicationLevel != null)
                    .Select(m => $"{TestPersonas.MediaUrlPrefix}{m.Key}:{TestPersonas.OpenGroupSlug}:{m.PublicationLevel}:{m.PublicationStatus}"),
                publications.Where(x => x.UserId == user.Id)
                    .Select(x => $"{x.Url}:{(x.HubGroupId is { } gid ? GroupOf(gid) : "-")}:{x.Level}:{x.Status}"));

            diff.AddRange(extra.Where(e => e.UserId == user.Id).Select(e => e.What).Distinct());
            result[user.Id] = diff;
        }
        return result;
    }

    /// <summary>Удалить всех utest-пользователей и тестовые группы, которыми они владеют.</summary>
    private async Task ResetAsync(List<string> log)
    {
        var testUserIds = await TestUsers().Select(u => u.Id).ToListAsync();

        // Группы — первыми: HubGroups.OwnerUserId держит пользователя (Restrict), а удаление
        // группы каскадом уносит её состав, участников, админов, тренировки и публикации.
        var groups = await _db.HubGroups
            .Where(g => g.IsTest && testUserIds.Contains(g.OwnerUserId))
            .ToListAsync();
        _db.HubGroups.RemoveRange(groups);
        await _db.SaveChangesAsync();

        // Остальное (роли, избранное, медиа, участие в чужих группах) уходит каскадом.
        var users = await TestUsers().ToListAsync();
        _db.AppUsers.RemoveRange(users);
        await _db.SaveChangesAsync();
        log.Add($"reset: удалено тест-групп {groups.Count}, utest-пользователей {users.Count}");
    }

    private IQueryable<AppUser> TestUsers() =>
        _db.AppUsers.Where(u => u.Email.ToLower().StartsWith(TestAccountRules.Prefix));

    private async Task<Dictionary<string, AppUser>> EnsureUsersAsync(List<string> log)
    {
        var roleIds = await _db.AppRoles.ToDictionaryAsync(r => r.Name, r => r.Id);
        var needed = TestPersonas.All.SelectMany(p => p.Roles).Distinct().ToList();
        var missing = needed.Where(r => !roleIds.ContainsKey(r)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"Нет ролей {string.Join("/", missing)} в Sys_AppRoles — сначала запусти API один раз (роли сеются при старте).");

        var result = new Dictionary<string, AppUser>();
        int created = 0;
        foreach (var persona in TestPersonas.All)
        {
            var email = persona.Email.ToLowerInvariant();
            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                user = new AppUser { Email = email, DisplayName = persona.Nick };
                _db.AppUsers.Add(user);
                created++;
            }

            // Состояние персонажа выравниваем при каждом прогоне — это его описание.
            user.IsActive = persona.IsActive;
            user.HubGroupLimit = persona.HubGroupLimit;
            await _db.SaveChangesAsync();

            foreach (var role in persona.Roles) await EnsureRoleAsync(user.Id, roleIds[role]);
            result[persona.Nick] = user;
        }

        await _db.SaveChangesAsync();
        log.Add($"пользователи: создано {created}, уже было {result.Count - created}");
        return result;
    }

    private async Task EnsureRoleAsync(int userId, int roleId)
    {
        if (!await _db.AppUserRoles.AnyAsync(r => r.UserId == userId && r.RoleId == roleId))
            _db.AppUserRoles.Add(new AppUserRole { UserId = userId, RoleId = roleId });
    }

    private async Task<HubGroup> EnsureGroupAsync(TestGroup def, AppUser owner, List<string> log)
    {
        var group = await _db.HubGroups.FirstOrDefaultAsync(g => g.Slug == def.Slug);
        if (group != null)
        {
            // Чужая группа с нашим slug — не трогаем: сидер не должен переписать живую группу.
            if (!group.IsTest)
                throw new InvalidOperationException($"Группа «{def.Slug}» существует и НЕ тестовая — сидер её не трогает. Переименуй slug.");
            log.Add($"группа «{def.Slug}»: уже есть (#{group.Id})");
            return group;
        }

        group = new HubGroup
        {
            Name = def.Name,
            Slug = def.Slug,
            Description = "Test group seeded by --seed-personas. Visible only to site admins and utest accounts.",
            OwnerUserId = owner.Id,
            IsPublic = true,
            IsTest = true,
            JoinPolicy = def.JoinPolicy,
        };
        _db.HubGroups.Add(group);
        await _db.SaveChangesAsync();
        log.Add($"группа «{def.Slug}»: создана (#{group.Id}, {def.JoinPolicy})");
        return group;
    }

    /// <summary>
    /// Состав всех тест-групп — одни и те же пловцы. Если состав уже есть — берём его (выбор
    /// разовый); иначе самые активные за год пловцы 2012–2015 г.р. (реальные дети с заплывами —
    /// страницам есть что показать).
    /// </summary>
    private async Task<List<int>> EnsureRosterAsync(IReadOnlyList<HubGroup> groups, List<string> log)
    {
        var first = groups[0];
        var roster = await _db.HubGroupMembers
            .Where(m => m.HubGroupId == first.Id)
            .OrderBy(m => m.SortOrder)
            .Select(m => m.SwimmerId)
            .ToListAsync();

        if (roster.Count < RosterSize)
        {
            // Прошлый и текущий сезон — то же окно «активности», что у клубной подписки. Дата без
            // Kind=Utc: CompetitionDate — timestamp без зоны, UTC-дату Npgsql в неё не пустит.
            var since = HubGroupClubRules.ActivitySince(DateTime.UtcNow);
            var picked = await _db.Results.AsNoTracking()
                .Where(r => r.CompetitionDate >= since && r.Swimmer.Origin == "isr"
                            && r.Swimmer.BirthYear >= 2012 && r.Swimmer.BirthYear <= 2015)
                .GroupBy(r => r.SwimmerId)
                .Select(g => new { SwimmerId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count).ThenBy(x => x.SwimmerId)
                .Take(RosterSize * 2)
                .Select(x => x.SwimmerId)
                .ToListAsync();
            // Пустая/свежая база без заплывов — любые isr-пловцы, чтобы сидер всё равно отработал.
            if (picked.Count < RosterSize)
                picked.AddRange(await _db.Swimmers.AsNoTracking()
                    .Where(s => s.Origin == "isr" && !picked.Contains(s.Id))
                    .OrderBy(s => s.Id).Select(s => s.Id).Take(RosterSize).ToListAsync());

            roster = roster.Concat(picked.Where(id => !roster.Contains(id))).Take(RosterSize).ToList();
        }

        // Описание персонажей ссылается на места в составе — их должно хватать.
        var needed = TestPersonas.All
            .SelectMany(p => p.Favorites.Select(f => f.RosterIndex).Concat(p.Media.Select(m => m.RosterIndex)))
            .DefaultIfEmpty(-1).Max() + 1;
        if (roster.Count < Math.Max(needed, 1))
            throw new InvalidOperationException($"В базе меньше {needed} пловцов — не из кого собрать состав тест-групп.");

        int added = 0;
        foreach (var group in groups)
        {
            for (var i = 0; i < roster.Count; i++)
            {
                var swimmerId = roster[i];
                if (await _db.HubGroupMembers.AnyAsync(m => m.HubGroupId == group.Id && m.SwimmerId == swimmerId)) continue;
                _db.HubGroupMembers.Add(new HubGroupMember { HubGroupId = group.Id, SwimmerId = swimmerId, SortOrder = i });
                added++;
            }
        }
        await _db.SaveChangesAsync();
        log.Add($"состав: пловцы {string.Join(", ", roster)}; добавлено строк {added}");
        return roster;
    }

    private async Task EnsureUserMemberAsync(HubGroup group, AppUser user, string status, AppUser? addedBy, List<string> log)
    {
        var row = await _db.HubGroupUserMembers.FirstOrDefaultAsync(m => m.HubGroupId == group.Id && m.UserId == user.Id);
        if (row == null)
        {
            _db.HubGroupUserMembers.Add(new HubGroupUserMember
            {
                HubGroupId = group.Id, UserId = user.Id, AddedByUserId = addedBy?.Id, Status = status
            });
            log.Add($"участник: {user.DisplayName} → «{group.Slug}» ({status})");
        }
        else row.Status = status; // заявку могли одобрить руками — вернуть состояние персонажа
        await _db.SaveChangesAsync();
    }

    private async Task EnsureGroupAdminAsync(HubGroup group, AppUser user, AppUser grantedBy, List<string> log)
    {
        if (await _db.HubGroupAdmins.AnyAsync(a => a.HubGroupId == group.Id && a.UserId == user.Id)) return;
        _db.HubGroupAdmins.Add(new HubGroupAdmin { HubGroupId = group.Id, UserId = user.Id, GrantedByUserId = grantedBy.Id });
        await _db.SaveChangesAsync();
        log.Add($"админ группы: {user.DisplayName} → «{group.Slug}»");
    }

    private async Task EnsureFavoriteAsync(AppUser user, int swimmerId, bool isPrimary, bool isFamily, int sortOrder, List<string> log)
    {
        var fav = await _db.UserFavorites.FirstOrDefaultAsync(f => f.UserId == user.Id && f.SwimmerId == swimmerId);
        if (fav != null)
        {
            // Уровень выравниваем, как остальное состояние персонажа: поменяли руками на
            // My favorites — следующий прогон вернёт.
            if (fav.IsFamily != isFamily || fav.IsPrimary != isPrimary)
            {
                fav.IsFamily = isFamily;
                fav.IsPrimary = isPrimary;
                await _db.SaveChangesAsync();
            }
            return;
        }
        _db.UserFavorites.Add(new UserFavorite
        {
            UserId = user.Id, TargetType = "swimmer", SwimmerId = swimmerId,
            IsPrimary = isPrimary, IsFamily = isFamily, SortOrder = sortOrder
        });
        await _db.SaveChangesAsync();
        log.Add($"избранное: {user.DisplayName} → пловец #{swimmerId}{(isPrimary ? " (Me)" : "")}{(isFamily ? " (семья)" : "")}");
    }

    /// <summary>
    /// Медиа персонажа по описанию (<see cref="PersonaMedia"/>): ссылки на example.com — настоящих
    /// видео нет, сторож ссылок пометит их битыми, это нормально. Публикация — в open-группу.
    /// </summary>
    private async Task EnsureMediaAsync(AppUser author, IReadOnlyList<PersonaMedia> plan, List<int> roster,
        HubGroup group, AppUser decider, List<string> log)
    {
        int created = 0;
        foreach (var item in plan)
        {
            var url = TestPersonas.MediaUrlPrefix + item.Key;
            var media = await _db.UserMedia.FirstOrDefaultAsync(m => m.UserId == author.Id && m.Url == url);
            if (media == null)
            {
                media = new UserMedia
                {
                    UserId = author.Id, SwimmerId = roster[item.RosterIndex], Level = "swimmer",
                    MediaType = "video", SourceType = "other", Url = url, Visibility = item.Visibility,
                };
                _db.UserMedia.Add(media);
                await _db.SaveChangesAsync();
                created++;
            }

            if (item.PublicationLevel == null) continue;
            if (await _db.UserMediaPublications.AnyAsync(p => p.UserMediaId == media.Id && p.HubGroupId == group.Id)) continue;
            var approved = item.PublicationStatus == UserMediaPublicationStatus.Approved;
            _db.UserMediaPublications.Add(new UserMediaPublication
            {
                UserMediaId = media.Id,
                TargetType = UserMediaPublicationTarget.Group,
                HubGroupId = group.Id,
                Level = item.PublicationLevel,
                Status = item.PublicationStatus!,
                DecidedByUserId = approved ? decider.Id : null,
                DecidedAt = approved ? DateTime.UtcNow : null,
            });
            await _db.SaveChangesAsync();
        }
        log.Add($"медиа: {author.DisplayName} — создано {created} из {plan.Count}");
    }

    /// <summary>Одна тренировка в open-группе: 4×50 вольным у каждого пловца состава.</summary>
    private async Task EnsureTrainingAsync(HubGroup group, List<int> roster, List<string> log)
    {
        if (await _db.TrainingSessions.AnyAsync(s => s.HubGroupId == group.Id && s.ExternalTrainingId == TrainingExternalId))
            return;

        var freestyleId = await _db.Styles.Where(s => s.Name == "freestyle").Select(s => s.Id).FirstAsync();
        var genders = await _db.Swimmers.AsNoTracking()
            .Where(s => roster.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Gender == "female" ? "female" : "male");

        var session = new TrainingSession
        {
            HubGroupId = group.Id,
            ExternalTrainingId = TrainingExternalId,
            Name = "[TEST] 4x50",
            Date = DateTime.UtcNow.Date.AddDays(-2),
            PoolType = "25m",
            Note = "Seeded by --seed-personas — times are synthetic.",
        };
        _db.TrainingSessions.Add(session);
        await _db.SaveChangesAsync();

        for (var i = 0; i < roster.Count; i++)
        {
            for (var rep = 1; rep <= 4; rep++)
            {
                // Синтетика: базовое время растёт по составу, к концу сета пловец устаёт.
                var ms = 34_000 + i * 900 + rep * 250;
                _db.TrainingResults.Add(new TrainingResult
                {
                    SessionId = session.Id,
                    SwimmerId = roster[i],
                    StyleId = freestyleId,
                    Distance = "50",
                    Gender = genders.GetValueOrDefault(roster[i], "male"),
                    TimeMillisecond = ms,
                    TimeOriginal = $"{ms / 1000}.{ms % 1000 / 10:00}",
                    SetNo = 1,
                    OrderNo = rep,
                    IntervalSec = 60,
                    Intensity = "v3",
                });
            }
        }
        await _db.SaveChangesAsync();
        log.Add($"тренировка: «{session.Name}» в «{group.Slug}», заплывов {roster.Count * 4}");
    }
}
