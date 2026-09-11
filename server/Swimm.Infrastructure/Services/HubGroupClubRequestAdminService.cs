using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="IHubGroupClubRequestAdminService"/>
public class HubGroupClubRequestAdminService : IHubGroupClubRequestAdminService
{
    private readonly SwimmDbContext _db;
    private readonly ICacheService _cache;
    private readonly IEmailSender _email;
    private readonly IHubGroupClubSubscriptionService? _clubSubscriptions;
    private readonly ILogger<HubGroupClubRequestAdminService>? _logger;
    private readonly IAdminAuditService? _audit;

    /// <param name="clubSubscriptions">
    /// Автоподписка официальной группы на её клуб при одобрении. Необязательна: тестам
    /// одобрения, которым она не нужна, конструктор не меняли; в приложении её подставляет DI.
    /// </param>
    /// <param name="audit">Аудит одобрения (кого убрали из каталога, кого переименовали). Необязателен так же.</param>
    public HubGroupClubRequestAdminService(SwimmDbContext db, ICacheService cache, IEmailSender email,
        IHubGroupClubSubscriptionService? clubSubscriptions = null,
        ILogger<HubGroupClubRequestAdminService>? logger = null,
        IAdminAuditService? audit = null)
    {
        _db = db;
        _cache = cache;
        _email = email;
        _clubSubscriptions = clubSubscriptions;
        _logger = logger;
        _audit = audit;
    }

    public async Task<IReadOnlyList<HubGroupClubRequestAdminRowDto>> GetAllAsync()
    {
        var rows = await _db.HubGroupClubRequests.AsNoTracking()
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

        // Админ должен видеть последствия ДО кнопки (П4): чьи копии уйдут из каталога и кого
        // переименуют. Pending-заявок единицы — считаем по одной.
        foreach (var row in rows.Where(r => r.Status == HubGroupClubRequestStatus.Pending))
            row.ApproveImpact = await ComputeApproveImpactAsync(row.HubGroupId, row.ClubId);

        return rows;
    }

    /// <summary>HubGroup.Name / NameEn — MaxLength(200): суффикс не должен вытолкнуть имя за колонку.</summary>
    private const int GroupNameMaxLength = 200;

    /// <summary>
    /// Неофициальные группы (кроме будущей официальной), чьё имя или имя на английском совпало с
    /// клубом или с именем будущей официальной группы (§2, нормализация — HubGroupClubRules).
    /// Переименовываются ЛЮБЫЕ такие, а не только подписанные на клуб (решение по §6-2): группа с
    /// именем клуба — самозванство независимо от состава, и тем же правилом живёт валидация имени.
    /// </summary>
    private async Task<List<(HubGroup Group, bool Name, bool NameEn)>> FindNameConflictsAsync(
        int officialGroupId, int clubId, bool tracked)
    {
        var official = await _db.HubGroups.AsNoTracking()
            .Where(g => g.Id == officialGroupId)
            .Select(g => new { g.Name, g.NameEn })
            .FirstAsync();
        var club = await _db.Clubs.AsNoTracking()
            .Where(c => c.Id == clubId)
            .Select(c => new { c.Name, c.NameEn })
            .FirstAsync();
        string?[] reserved = [club.Name, club.NameEn, official.Name, official.NameEn];

        var query = _db.HubGroups.Where(g => !g.IsOfficial && g.Id != officialGroupId);
        if (!tracked) query = query.AsNoTracking();

        return (await query.ToListAsync())
            .Select(g => (Group: g,
                Name: HubGroupClubRules.ConflictsWith(g.Name, reserved),
                NameEn: HubGroupClubRules.ConflictsWith(g.NameEn, reserved)))
            .Where(c => c.Name || c.NameEn)
            .ToList();
    }

    private async Task<HubGroupClubApproveImpactDto> ComputeApproveImpactAsync(int hubGroupId, int clubId)
    {
        // Уйдут из каталога — подписанные на этот клуб. Официальной у клуба ещё нет: иначе
        // одобрение и так откажет («уже есть официальная группа»).
        var leave = await _db.HubGroups.AsNoTracking()
            .Where(g => g.Id != hubGroupId && g.ClubSubscriptions.Any(s => s.ClubId == clubId))
            .OrderBy(g => g.Name)
            .Select(g => g.Name)
            .ToListAsync();

        var renamed = (await FindNameConflictsAsync(hubGroupId, clubId, tracked: false))
            .Select(c => c.Name
                ? $"{c.Group.Name} → {HubGroupClubRules.WithCommunitySuffix(c.Group.Name, GroupNameMaxLength)}"
                : $"{c.Group.NameEn} → {HubGroupClubRules.WithCommunitySuffix(c.Group.NameEn!, GroupNameMaxLength)}")
            .ToList();

        return new HubGroupClubApproveImpactDto { LeaveCatalog = leave, Renamed = renamed };
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

        // Последствия — до изменений: после одобрения «кто уйдёт из каталога» уже не посчитать.
        var impact = await ComputeApproveImpactAsync(group.Id, request.ClubId);

        // Одна транзакция: SaveChangesAsync ниже сохраняет все изменения атомарно одним
        // db-транзитом — отдельный BeginTransactionAsync не нужен и несовместим с
        // NpgsqlRetryingExecutionStrategy (AdminConnection настроен на retry).
        group.IsOfficial = true;
        group.ClubId = request.ClubId;
        group.UpdatedAt = DateTime.UtcNow;

        // Официальная группа — главная (П4): у неофициальных групп, чьё имя совпало с клубом или
        // с ней самой, — « · community». В той же транзакции: одобрение без переименования
        // оставило бы в каталоге две группы с именем клуба. Разово: снимут статус — имя не вернётся.
        foreach (var (other, nameHit, nameEnHit) in await FindNameConflictsAsync(group.Id, request.ClubId, tracked: true))
        {
            if (nameHit) other.Name = HubGroupClubRules.WithCommunitySuffix(other.Name, GroupNameMaxLength);
            if (nameEnHit && other.NameEn != null)
                other.NameEn = HubGroupClubRules.WithCommunitySuffix(other.NameEn, GroupNameMaxLength);
            other.UpdatedAt = DateTime.UtcNow;
        }

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

        await SubscribeOfficialGroupAsync(group.Id, request.ClubId, request.DecidedByUserId);

        await _email.SendAsync(request.User.Email, "Swimm — club request approved",
            $"Your group \"{group.Name}\" is now the official group of {request.Club!.Name}. " +
            "You've been granted the Coach role for managing groups.");

        // Одобрение меняет чужие группы (каталог, имена) — пусть в /Admin/Audit останется, кто
        // и что. Писем их владельцам не шлём (решение §1-4): они видят плашку в «My groups».
        if (_audit != null)
        {
            var summary = $"Официальная группа «{group.Name}» клуба «{request.Club!.Name}»"
                + (impact.LeaveCatalog.Count > 0 ? $"; ушли из каталога: {impact.LeaveCatalog.Count}" : "")
                + (impact.Renamed.Count > 0 ? $"; переименованы: {string.Join("; ", impact.Renamed)}" : "");
            await _audit.LogAsync("hubgroup.official.approve", "HubGroup", group.Id.ToString(), summary,
                new { requestId, clubId = request.ClubId, impact.LeaveCatalog, impact.Renamed });
        }

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
    /// Официальная группа — «лицо клуба» и весь клуб одной страницей, поэтому при одобрении она
    /// сама подписывается на свой клуб, если подписки у неё ещё нет (решение Влада 10.09.2026,
    /// §6-1 плана docs/plans/hubgroup-club-subscription-plan.md). Без этого только что
    /// одобренная группа стояла пустой: официальный статус — это связь с клубом, не состав.
    /// Уже подписанную не трогаем — подписку выбирал владелец; отписаться он может сам.
    ///
    /// После коммита одобрения и best-effort: сбой подписки одобрение не откатывает, подписать
    /// можно руками (API владельца …/club-subscription).
    /// </summary>
    private async Task SubscribeOfficialGroupAsync(int hubGroupId, int clubId, int? decidedByUserId)
    {
        if (_clubSubscriptions == null) return;
        if (await _db.HubGroupClubSubscriptions.AnyAsync(s => s.HubGroupId == hubGroupId)) return;

        try
        {
            var result = await _clubSubscriptions.SubscribeAsync(hubGroupId, clubId, decidedByUserId);
            if (!result.Success)
                _logger?.LogWarning("Официальная группа {GroupId} не подписана на клуб {ClubId}: {Error}",
                    hubGroupId, clubId, result.Error);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Официальная группа {GroupId} не подписана на клуб {ClubId} — подписать руками",
                hubGroupId, clubId);
        }
    }

    /// <summary>
    /// DecidedByUserId — nullable, в отличие от OwnerUserId (см. HubGroupAdminService.ResolveOwnerIdAsync):
    /// DevAdminBypass (Security/DevAdminBypass.cs) на пустой БД падает в фоллбек — синтетический
    /// NameIdentifier="0", которого нет в Sys_AppUsers; вместо падения на FK просто оставляем
    /// поле null (кто одобрил — не проставлен).
    /// </summary>
    private async Task<int?> ResolveAdminUserIdAsync(int adminUserId) =>
        adminUserId > 0 && await _db.AppUsers.AnyAsync(u => u.Id == adminUserId) ? adminUserId : null;
}
