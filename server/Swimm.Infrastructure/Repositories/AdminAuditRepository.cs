using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>Чтение журнала аудита для /Admin/Audit (фаза 7.4).</summary>
public class AdminAuditRepository(SwimmDbContext db) : IAdminAuditRepository
{
    public async Task<PagedResult<AdminAuditRowDto>> QueryAsync(
        AdminAuditFilter filter, CancellationToken ct = default)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize is < 1 or > 200 ? 30 : filter.PageSize;

        var q = db.AdminAudits.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Action))
            q = q.Where(a => a.Action == filter.Action);
        if (!string.IsNullOrWhiteSpace(filter.EntityType))
            q = q.Where(a => a.EntityType == filter.EntityType);
        if (filter.ActorUserId is int actorId)
            q = q.Where(a => a.ActorUserId == actorId);
        if (filter.SinceUtc is DateTime sinceUtc)
            q = q.Where(a => a.CreatedAt >= sinceUtc);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            q = q.Where(a =>
                EF.Functions.ILike(a.Summary, $"%{term}%") ||
                EF.Functions.ILike(a.ActorName, $"%{term}%") ||
                (a.EntityId != null && EF.Functions.ILike(a.EntityId, $"%{term}%")));
        }

        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AdminAuditRowDto(
                a.Id, a.ActorUserId, a.ActorName, a.Action, a.EntityType,
                a.EntityId, a.Summary, a.DetailsJson, a.IpAddress, a.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<AdminAuditRowDto>(rows, total, page, pageSize);
    }

    public async Task<IReadOnlyList<string>> GetDistinctActionsAsync(CancellationToken ct = default)
        => await db.AdminAudits.AsNoTracking()
            .Select(a => a.Action)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync(ct);
}
