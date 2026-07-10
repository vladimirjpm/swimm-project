using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="IHubGroupClubRequestAdminService"/>
public class HubGroupClubRequestAdminService : IHubGroupClubRequestAdminService
{
    private readonly SwimmDbContext _db;
    private readonly ICacheService _cache;
    private readonly IEmailSender _email;

    public HubGroupClubRequestAdminService(SwimmDbContext db, ICacheService cache, IEmailSender email)
    {
        _db = db;
        _cache = cache;
        _email = email;
    }

    public async Task<IReadOnlyList<HubGroupClubRequestAdminRowDto>> GetAllAsync()
    {
        return await _db.HubGroupClubRequests.AsNoTracking()
            .Include(r => r.HubGroup)
            .Include(r => r.User)
            .Include(r => r.Club)
            .Include(r => r.DecidedByUser)
            .OrderBy(r => r.Status == HubGroupClubRequestStatus.Pending ? 0 : 1)
            .ThenByDescending(r => r.CreatedAt)
            .Select(r => new HubGroupClubRequestAdminRowDto
            {
                Id = r.Id,
                HubGroupId = r.HubGroupId,
                HubGroupName = r.HubGroup!.Name,
                HubGroupSlug = r.HubGroup.Slug,
                RequesterUserId = r.UserId,
                RequesterDisplayName = r.User!.DisplayName,
                RequesterEmail = r.User.Email,
                ClubId = r.ClubId,
                ClubName = r.Club!.Name,
                Message = r.Message,
                Status = r.Status,
                CreatedAt = r.CreatedAt,
                DecidedAt = r.DecidedAt,
                DecidedByDisplayName = r.DecidedByUser != null ? r.DecidedByUser.DisplayName : null
            })
            .ToListAsync();
    }

    public Task<int> GetPendingCountAsync() =>
        _db.HubGroupClubRequests.CountAsync(r => r.Status == HubGroupClubRequestStatus.Pending);

    public async Task<HubGroupMemberSaveResult> ApproveAsync(int requestId, int adminUserId)
    {
        var request = await _db.HubGroupClubRequests
            .Include(r => r.HubGroup)
            .Include(r => r.Club)
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == requestId);
        if (request == null) return HubGroupMemberSaveResult.Fail($"Заявка #{requestId} не найдена");
        if (request.Status != HubGroupClubRequestStatus.Pending)
            return HubGroupMemberSaveResult.Fail("Заявка уже рассмотрена");

        var group = request.HubGroup!;
        var clubTaken = await _db.HubGroups
            .AnyAsync(g => g.Id != group.Id && g.ClubId == request.ClubId && g.IsOfficial);
        if (clubTaken) return HubGroupMemberSaveResult.Fail("У этого клуба уже есть официальная группа");

        // Одна транзакция: SaveChangesAsync ниже сохраняет все изменения атомарно одним
        // db-транзитом — отдельный BeginTransactionAsync не нужен и несовместим с
        // NpgsqlRetryingExecutionStrategy (AdminConnection настроен на retry).
        group.IsOfficial = true;
        group.ClubId = request.ClubId;
        group.UpdatedAt = DateTime.UtcNow;

        var hasCoachRole = await _db.AppUserRoles
            .AnyAsync(ur => ur.UserId == request.UserId && ur.Role.Name == "Coach");
        if (!hasCoachRole)
        {
            var coachRoleId = await _db.AppRoles
                .Where(r => r.Name == "Coach")
                .Select(r => (int?)r.Id)
                .FirstOrDefaultAsync();
            if (coachRoleId != null)
                _db.AppUserRoles.Add(new AppUserRole { UserId = request.UserId, RoleId = coachRoleId.Value });
        }

        // Бамп SecurityStamp — как при смене ролей везде в проекте (см. AuthController.LogoutAll):
        // старая cookie-сессия должна перевалидироваться и подхватить новую роль Coach.
        request.User!.SecurityStamp = Guid.NewGuid().ToString("N");
        request.User.UpdatedAt = DateTime.UtcNow;

        request.Status = HubGroupClubRequestStatus.Approved;
        request.DecidedAt = DateTime.UtcNow;
        request.DecidedByUserId = await ResolveAdminUserIdAsync(adminUserId);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Гонка: параллельное одобрение другой группы на тот же клуб проскочило проверку
            // clubTaken и заняло partial-unique IX_HubGroups_ClubId_Official — внятная ошибка
            // вместо 500. Изменения этой транзакции откатываются целиком.
            return HubGroupMemberSaveResult.Fail("У этого клуба уже есть официальная группа");
        }
        await _cache.InvalidateAllAsync();

        await _email.SendAsync(request.User.Email, "Swimm — club request approved",
            $"Your group \"{group.Name}\" is now the official group of {request.Club!.Name}. " +
            "You've been granted the Coach role for managing groups.");

        return HubGroupMemberSaveResult.Ok();
    }

    public async Task<HubGroupMemberSaveResult> RejectAsync(int requestId, int adminUserId)
    {
        var request = await _db.HubGroupClubRequests
            .Include(r => r.HubGroup)
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == requestId);
        if (request == null) return HubGroupMemberSaveResult.Fail($"Заявка #{requestId} не найдена");
        if (request.Status != HubGroupClubRequestStatus.Pending)
            return HubGroupMemberSaveResult.Fail("Заявка уже рассмотрена");

        request.Status = HubGroupClubRequestStatus.Rejected;
        request.DecidedAt = DateTime.UtcNow;
        request.DecidedByUserId = await ResolveAdminUserIdAsync(adminUserId);
        await _db.SaveChangesAsync();

        await _email.SendAsync(request.User!.Email, "Swimm — club request rejected",
            $"Your official-status request for group \"{request.HubGroup!.Name}\" was not approved.");

        return HubGroupMemberSaveResult.Ok();
    }

    /// <summary>
    /// DecidedByUserId — nullable, в отличие от OwnerUserId (см. HubGroupAdminService.ResolveOwnerIdAsync):
    /// DevAdminBypass (Program.cs) даёт синтетический NameIdentifier="0", которого нет в Sys_AppUsers —
    /// вместо падения на FK просто оставляем поле null (кто одобрил — не проставлен).
    /// </summary>
    private async Task<int?> ResolveAdminUserIdAsync(int adminUserId) =>
        adminUserId > 0 && await _db.AppUsers.AnyAsync(u => u.Id == adminUserId) ? adminUserId : null;
}
