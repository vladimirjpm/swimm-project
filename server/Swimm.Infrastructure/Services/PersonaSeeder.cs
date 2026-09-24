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
/// ⚠ Пловцов НЕ заводим: таблица Swimmers едет на прод. Состав тест-групп и «дети» родителя —
/// существующие пловцы, выбранные один раз; повторный прогон их не перевыбирает.
/// </summary>
public class PersonaSeeder : IPersonaSeeder
{
    /// <summary>Сколько пловцов в составе тест-групп.</summary>
    private const int RosterSize = 6;

    /// <summary>Личный лимит групп у utest-coach: владеет двумя — лимит исчерпан.</summary>
    private const int CoachGroupLimit = 2;

    private const string TrainingExternalId = "utest-training-1";
    private const string MediaUrlPrefix = "https://example.com/utest/";

    private readonly SwimmDbContext _db;

    public PersonaSeeder(SwimmDbContext db) => _db = db;

    public async Task<IReadOnlyList<string>> SeedAsync(bool reset = false)
    {
        var log = new List<string>();
        if (reset) await ResetAsync(log);

        var users = await EnsureUsersAsync(log);
        var coach = users[TestPersonas.Coach];

        var openGroup = await EnsureGroupAsync(TestPersonas.OpenGroupSlug, "[TEST] Open", HubGroupJoinPolicy.Open, coach, log);
        var approvalGroup = await EnsureGroupAsync(TestPersonas.ApprovalGroupSlug, "[TEST] Approval", HubGroupJoinPolicy.Approval, coach, log);

        var roster = await EnsureRosterAsync(openGroup, approvalGroup, log);

        // ── участие аккаунтов ────────────────────────────────────────────────────
        await EnsureUserMemberAsync(openGroup, users[TestPersonas.Member], HubGroupUserMemberStatus.Active, coach, log);
        await EnsureUserMemberAsync(approvalGroup, users[TestPersonas.Member], HubGroupUserMemberStatus.Active, coach, log);
        await EnsureUserMemberAsync(approvalGroup, users[TestPersonas.Pending], HubGroupUserMemberStatus.Pending, addedBy: null, log);
        // Автор медиа — участник: иначе подать публикацию в группу нельзя.
        await EnsureUserMemberAsync(openGroup, users[TestPersonas.MediaAuthor], HubGroupUserMemberStatus.Active, coach, log);
        await EnsureGroupAdminAsync(openGroup, users[TestPersonas.GroupAdmin], coach, log);

        // ── избранное: «дети» родителя и «это я» ─────────────────────────────────
        await EnsureFavoriteAsync(users[TestPersonas.Parent], roster[0], isPrimary: false, sortOrder: 0, log);
        await EnsureFavoriteAsync(users[TestPersonas.Parent], roster[1], isPrimary: false, sortOrder: 1, log);
        await EnsureFavoriteAsync(users[TestPersonas.SwimmerMe], roster[2], isPrimary: true, sortOrder: 0, log);

        await EnsureMediaAsync(users[TestPersonas.MediaAuthor], roster[0], openGroup, coach, log);
        await EnsureTrainingAsync(openGroup, roster, log);

        log.Add($"готово: персонажей {users.Count}, тест-группы «{openGroup.Slug}» и «{approvalGroup.Slug}»");
        return log;
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
        if (!roleIds.TryGetValue("User", out var userRoleId) || !roleIds.TryGetValue("Coach", out var coachRoleId))
            throw new InvalidOperationException("Нет ролей User/Coach в Sys_AppRoles — сначала запусти API один раз (роли сеются при старте).");

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

            // Состояние персонажа выравниваем при каждом прогоне — это его определение.
            user.IsActive = persona.Nick != TestPersonas.Blocked;
            user.HubGroupLimit = persona.Nick == TestPersonas.Coach ? CoachGroupLimit : null;
            await _db.SaveChangesAsync();

            await EnsureRoleAsync(user.Id, userRoleId);
            if (persona.Nick == TestPersonas.Coach) await EnsureRoleAsync(user.Id, coachRoleId);
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

    private async Task<HubGroup> EnsureGroupAsync(string slug, string name, string joinPolicy, AppUser owner, List<string> log)
    {
        var group = await _db.HubGroups.FirstOrDefaultAsync(g => g.Slug == slug);
        if (group != null)
        {
            // Чужая группа с нашим slug — не трогаем: сидер не должен переписать живую группу.
            if (!group.IsTest)
                throw new InvalidOperationException($"Группа «{slug}» существует и НЕ тестовая — сидер её не трогает. Переименуй slug.");
            log.Add($"группа «{slug}»: уже есть (#{group.Id})");
            return group;
        }

        group = new HubGroup
        {
            Name = name,
            Slug = slug,
            Description = "Test group seeded by --seed-personas. Visible only to site admins and utest accounts.",
            OwnerUserId = owner.Id,
            IsPublic = true,
            IsTest = true,
            JoinPolicy = joinPolicy,
        };
        _db.HubGroups.Add(group);
        await _db.SaveChangesAsync();
        log.Add($"группа «{slug}»: создана (#{group.Id}, {joinPolicy})");
        return group;
    }

    /// <summary>
    /// Состав обеих групп — одни и те же пловцы. Если состав уже есть — берём его (выбор
    /// разовый); иначе самые активные за год пловцы 2012–2015 г.р. (реальные дети с заплывами —
    /// страницам есть что показать).
    /// </summary>
    private async Task<List<int>> EnsureRosterAsync(HubGroup openGroup, HubGroup approvalGroup, List<string> log)
    {
        var roster = await _db.HubGroupMembers
            .Where(m => m.HubGroupId == openGroup.Id)
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
        if (roster.Count < 3)
            throw new InvalidOperationException("В базе меньше трёх пловцов — не из кого собрать состав тест-групп.");

        int added = 0;
        foreach (var group in new[] { openGroup, approvalGroup })
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

    private async Task EnsureFavoriteAsync(AppUser user, int swimmerId, bool isPrimary, int sortOrder, List<string> log)
    {
        if (await _db.UserFavorites.AnyAsync(f => f.UserId == user.Id && f.SwimmerId == swimmerId)) return;
        _db.UserFavorites.Add(new UserFavorite
        {
            UserId = user.Id, TargetType = "swimmer", SwimmerId = swimmerId, IsPrimary = isPrimary, SortOrder = sortOrder
        });
        await _db.SaveChangesAsync();
        log.Add($"избранное: {user.DisplayName} → пловец #{swimmerId}{(isPrimary ? " (Me)" : "")}");
    }

    /// <summary>
    /// Четыре медиа автора на одного пловца состава: личное, публичное, личное с публикацией
    /// в группу на одобрении и публичное с одобренной публикацией. Ссылки — на example.com:
    /// настоящих видео нет, сторож ссылок пометит их битыми, это нормально.
    /// </summary>
    private async Task EnsureMediaAsync(AppUser author, int swimmerId, HubGroup group, AppUser decider, List<string> log)
    {
        var plan = new (string Key, string Visibility, string? PublicationLevel, string? PublicationStatus)[]
        {
            ("private-video", "private", null, null),
            ("public-video", "public", null, null),
            ("pending-members-video", "private", UserMediaPublicationLevel.Members, UserMediaPublicationStatus.Pending),
            ("approved-public-video", "public", UserMediaPublicationLevel.Public, UserMediaPublicationStatus.Approved),
        };

        int created = 0;
        foreach (var (key, visibility, level, status) in plan)
        {
            var url = MediaUrlPrefix + key;
            var media = await _db.UserMedia.FirstOrDefaultAsync(m => m.UserId == author.Id && m.Url == url);
            if (media == null)
            {
                media = new UserMedia
                {
                    UserId = author.Id, SwimmerId = swimmerId, Level = "swimmer",
                    MediaType = "video", SourceType = "other", Url = url, Visibility = visibility,
                };
                _db.UserMedia.Add(media);
                await _db.SaveChangesAsync();
                created++;
            }

            if (level == null) continue;
            if (await _db.UserMediaPublications.AnyAsync(p => p.UserMediaId == media.Id && p.HubGroupId == group.Id)) continue;
            var approved = status == UserMediaPublicationStatus.Approved;
            _db.UserMediaPublications.Add(new UserMediaPublication
            {
                UserMediaId = media.Id,
                TargetType = UserMediaPublicationTarget.Group,
                HubGroupId = group.Id,
                Level = level,
                Status = status!,
                DecidedByUserId = approved ? decider.Id : null,
                DecidedAt = approved ? DateTime.UtcNow : null,
            });
            await _db.SaveChangesAsync();
        }
        log.Add($"медиа: {author.DisplayName} — создано {created} из {plan.Length}");
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
