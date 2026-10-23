using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Удаление аккаунта (docs/plans/account-deletion-plan.md, У2). Правило — только
/// <see cref="AccountDeletionRules"/>; здесь — порядок записи.
///
/// Почти всё уходит каскадом базы от <c>Sys_AppUsers</c> (личные таблицы), авторство в чужих
/// группах обнуляется (SetNull, миграция AccountDeletionSetNullUserRefs). Руками — то, что
/// каскад не покрывает:
///  1. свои группы — через <see cref="IHubGroupAdminService.DeleteAsync"/>: он удаляет
///     тренировки раньше группы (иначе RESTRICT <c>Sys_TrainingResults</c> → пловец группы) и
///     пишет аудит <c>hubgroup.delete</c>. <c>HubGroups.OwnerUserId</c> — RESTRICT намеренно;
///  2. <c>Swimmers.LogligIdSuggestedByUserId</c> — id без FK;
///  3. <c>Sys_AdminAudit</c>: в строках, где он актор, email и IP заменяются (Р5) — ПОСЛЕ записи
///     <c>user.delete</c>, потому что при самоудалении её актор — он сам.
/// Всё в одной транзакции: упало посередине — аккаунт и группы на месте.
/// </summary>
public class AccountDeletionService(
    SwimmDbContext db,
    IHubGroupAdminService groups,
    ILocalAuthService localAuth,
    IAdminAuditService audit,
    ILogger<AccountDeletionService> logger) : IAccountDeletionService
{
    private const string AdminRole = "Admin";

    /// <summary>Чем заменяется email актора в аудите после удаления (Р5).</summary>
    public static string DeletedActorName(int userId) => $"deleted user #{userId}";

    public async Task<AccountDeletionImpactDto?> GetImpactAsync(int userId, DateTime? authTimeUtc = null, CancellationToken ct = default)
    {
        var user = await LoadUserFactsAsync(userId, ct);
        if (user == null) return null;

        var owned = await LoadOwnedGroupsAsync(userId, ct);
        var shared = owned.Where(g => g.IsShared).ToList();

        var dto = new AccountDeletionImpactDto
        {
            UserId = userId,
            Favorites = await db.UserFavorites.CountAsync(f => f.UserId == userId, ct),
            Media = await db.UserMedia.CountAsync(m => m.UserId == userId, ct),
            PublishedMedia = await db.UserMedia.CountAsync(m => m.UserId == userId
                && (m.Visibility == "public"
                    || db.UserMediaPublications.Any(p => p.UserMediaId == m.Id
                        && p.Status != UserMediaPublicationStatus.Rejected)), ct),
            Reactions = await db.UserReactions.CountAsync(r => r.UserId == userId, ct),
            StartListPlans = await db.UserStartListPlans.CountAsync(p => p.UserId == userId, ct),
            GroupMemberships = await db.HubGroupUserMembers.CountAsync(m => m.UserId == userId
                && m.HubGroup!.OwnerUserId != userId, ct),
            GroupAdminships = await db.HubGroupAdmins.CountAsync(a => a.UserId == userId
                && a.HubGroup!.OwnerUserId != userId, ct),
            PushDevices = await db.UserPushSubscriptions.CountAsync(s => s.UserId == userId, ct),
            GroupContentKept =
                await db.HubGroupMedia.CountAsync(m => m.CreatedByUserId == userId && m.HubGroup!.OwnerUserId != userId, ct)
                + await db.TrainingSessions.CountAsync(s => s.CreatedByUserId == userId && s.HubGroup!.OwnerUserId != userId, ct)
                + await db.LanePlans.CountAsync(p => p.CreatedByUserId == userId && p.HubGroup!.OwnerUserId != userId, ct),
            SharedGroups = shared.Select(g => new AccountSharedGroupDto
            {
                Id = g.Id, Name = g.Name, NameEn = g.NameEn, Slug = g.Slug, OtherPeople = g.OtherPeople,
            }).ToList(),
            HasPassword = user.HasPassword,
            SignInFresh = AccountDeletionRules.IsFreshSignIn(authTimeUtc, DateTime.UtcNow),
        };

        foreach (var g in owned.Where(g => !g.IsShared))
            if (await groups.GetDeleteImpactAsync(g.Id) is { } impact)
                dto.GroupsDeleted.Add(impact);

        dto.BlockReasons = AccountDeletionRules.BlockReasons(user.IsSiteAdmin, shared.Count > 0).ToList();
        dto.BlockMessages = dto.BlockReasons.Select(AccountDeletionRules.Message).ToList();
        return dto;
    }

    public async Task<AccountDeletionResult> DeleteSelfAsync(int userId, AccountDeletionRequest request, DateTime? authTimeUtc, CancellationToken ct = default)
    {
        var user = await LoadUserFactsAsync(userId, ct);
        if (user == null) return new AccountDeletionResult(404, "Account not found.");

        if (!AccountDeletionRules.EmailMatches(request.ConfirmEmail, user.Email))
            return new AccountDeletionResult(403, "The email doesn't match your account.", "email-mismatch");

        if (user.HasPassword)
        {
            // Через общий счётчик входа: 5 неверных — блокировка на 15 минут и для входа, и здесь.
            // Иначе из чужой открытой сессии можно подбирать пароль мимо lockout.
            var check = await localAuth.VerifyPasswordAsync(userId, request.Password ?? "", ct);
            switch (check.Status)
            {
                case PasswordCheckStatus.LockedOut:
                    return new AccountDeletionResult(403, "Too many wrong passwords. Try again in 15 minutes.", "locked-out");
                case PasswordCheckStatus.Wrong:
                case PasswordCheckStatus.NoPassword:
                    return new AccountDeletionResult(403, "Wrong password.", "wrong-password");
            }
        }
        else if (!AccountDeletionRules.IsFreshSignIn(authTimeUtc, DateTime.UtcNow))
        {
            return new AccountDeletionResult(403, "Please sign in again to confirm.", "sign-in-again");
        }

        return await DeleteCoreAsync(user, deleteSharedGroups: false, reason: null, ct);
    }

    public async Task<AccountDeletionResult> DeleteByAdminAsync(int userId, string reason, bool deleteSharedGroups, CancellationToken ct = default)
    {
        var user = await LoadUserFactsAsync(userId, ct);
        if (user == null) return new AccountDeletionResult(404, "Пользователь не найден.");
        return await DeleteCoreAsync(user, deleteSharedGroups, reason, ct);
    }

    private async Task<AccountDeletionResult> DeleteCoreAsync(UserFacts user, bool deleteSharedGroups, string? reason, CancellationToken ct)
    {
        var userId = user.Id;
        var owned = await LoadOwnedGroupsAsync(userId, ct);
        var blocks = AccountDeletionRules.BlockReasons(user.IsSiteAdmin, owned.Any(g => g.IsShared), deleteSharedGroups);
        if (blocks.Count > 0)
            return new AccountDeletionResult(409, AccountDeletionRules.Message(blocks[0]), blocks[0]);

        // Снимок «что ушло» — до удаления: после каскада считать нечего. Пойдёт в аудит числами,
        // без email (иначе удаление оставит то, что удаляли).
        var impact = await GetImpactAsync(userId, null, ct);

        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);

                foreach (var g in owned)
                {
                    var deleted = await groups.DeleteAsync(g.Id);
                    if (!deleted.Success)
                        throw new InvalidOperationException($"Группа #{g.Id} не удалилась: {deleted.Error}");
                }

                // Через трекер, а не ExecuteUpdate: колонка служебная (CacheServiceColumns), и
                // сброс кэша по ней делает интерсептор сохранения.
                var suggested = await db.Swimmers.Where(s => s.LogligIdSuggestedByUserId == userId).ToListAsync(ct);
                foreach (var s in suggested) s.LogligIdSuggestedByUserId = null;

                var entity = await db.AppUsers.FirstAsync(u => u.Id == userId, ct);
                db.AppUsers.Remove(entity);
                await db.SaveChangesAsync(ct);

                await audit.LogAsync("user.delete", "AppUser", userId.ToString(),
                    AuditSummary(userId, reason, impact),
                    new { userId, bySelf = reason == null, reason, impact = AuditDetails(impact) }, ct);

                await db.AdminAudits
                    .Where(a => a.ActorUserId == userId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(a => a.ActorName, DeletedActorName(userId))
                        .SetProperty(a => a.IpAddress, (string?)null), ct);

                await tx.CommitAsync(ct);
            });
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            // Чаще всего — FK, которого нет в разборе плана (новая таблица со ссылкой на
            // пользователя без SetNull/Cascade). Транзакция откатилась: аккаунт и группы целы.
            db.ChangeTracker.Clear();
            logger.LogError(ex, "Удаление аккаунта #{UserId} не прошло", userId);
            return new AccountDeletionResult(500,
                "Your account couldn't be deleted. Please write to privacy@swimhub.co.il.", "failed");
        }

        return new AccountDeletionResult(204);
    }

    private static string AuditSummary(int userId, string? reason, AccountDeletionImpactDto? i)
    {
        var who = reason == null ? "сам пользователь" : $"админ сайта ({reason})";
        if (i == null) return $"Аккаунт #{userId} удалён; удалил — {who}";
        var parts = new List<string>();
        if (i.Favorites > 0) parts.Add($"избранное {i.Favorites}");
        if (i.Media > 0) parts.Add($"медиа {i.Media} (опубликованных {i.PublishedMedia})");
        if (i.Reactions > 0) parts.Add($"реакций {i.Reactions}");
        if (i.GroupMemberships > 0) parts.Add($"участий в группах {i.GroupMemberships}");
        if (i.GroupAdminships > 0) parts.Add($"админств в группах {i.GroupAdminships}");
        var groupsGone = i.GroupsDeleted.Count + i.SharedGroups.Count;
        if (groupsGone > 0) parts.Add($"своих групп удалено {groupsGone}");
        if (i.GroupContentKept > 0) parts.Add($"осталось в группах без автора {i.GroupContentKept}");
        return $"Аккаунт #{userId} удалён; удалил — {who}" + (parts.Count > 0 ? ": " + string.Join(", ", parts) : "");
    }

    /// <summary>Детали для DetailsJson — только числа и id групп, без имён и email.</summary>
    private static object? AuditDetails(AccountDeletionImpactDto? i) => i == null ? null : new
    {
        i.Favorites, i.Media, i.PublishedMedia, i.Reactions, i.StartListPlans,
        i.GroupMemberships, i.GroupAdminships, i.PushDevices, i.GroupContentKept,
        groupsDeleted = i.GroupsDeleted.Select(g => g.Id).Concat(i.SharedGroups.Select(g => g.Id)).ToList(),
    };

    /// <summary>Факты для правила. Хеш пароля сюда не читается — сверяет его <see cref="ILocalAuthService"/>.</summary>
    private sealed record UserFacts(int Id, string Email, bool IsSiteAdmin, bool HasPassword);

    private Task<UserFacts?> LoadUserFactsAsync(int userId, CancellationToken ct) =>
        db.AppUsers.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserFacts(
                u.Id,
                u.Email,
                u.UserRoles.Any(r => r.Role.Name == AdminRole),
                u.LocalCredential != null && u.LocalCredential.PasswordHash != null && u.LocalCredential.PasswordHash != ""))
            .FirstOrDefaultAsync(ct);

    private sealed record OwnedGroup(int Id, string Name, string? NameEn, string Slug, int OtherPeople)
    {
        public bool IsShared => OtherPeople > 0;
    }

    private async Task<List<OwnedGroup>> LoadOwnedGroupsAsync(int userId, CancellationToken ct)
    {
        var rows = await db.HubGroups.AsNoTracking()
            .Where(g => g.OwnerUserId == userId)
            .OrderBy(g => g.Id)
            .Select(g => new
            {
                g.Id, g.Name, g.NameEn, g.Slug,
                Members = g.UserMembers.Count(m => m.UserId != userId),
                Admins = g.Admins.Count(a => a.UserId != userId),
            })
            .ToListAsync(ct);
        return rows
            .Select(r => new OwnedGroup(r.Id, r.Name, r.NameEn, r.Slug,
                AccountDeletionRules.IsShared(r.Members, r.Admins) ? r.Members + r.Admins : 0))
            .ToList();
    }
}
