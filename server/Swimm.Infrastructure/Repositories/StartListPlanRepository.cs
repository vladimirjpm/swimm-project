using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Персональный план на соревнование (шаг Т3). Хранит состав целиком: пикер — экран
/// множественного выбора, и «добавь одного» здесь только плодило бы расхождение между
/// показанным и сохранённым.
/// </summary>
public class StartListPlanRepository : IStartListPlanRepository
{
    /// <summary>
    /// Потолок состава. Не защита от злого умысла (план и так свой собственный), а страховка
    /// от кривого клиента: колонка ограничена длиной, и молча обрезанный хвост читался бы как
    /// «часть выбора пропала».
    /// </summary>
    private const int MaxIds = 100;

    private readonly SwimmDbContext _db;

    public StartListPlanRepository(SwimmDbContext db)
    {
        _db = db;
    }

    public async Task<StartListPlanDto?> GetAsync(int userId, int orgCompId, CancellationToken ct = default)
    {
        var plan = await _db.UserStartListPlans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.OrgCompId == orgCompId, ct);

        return plan is null ? null : ToDto(plan);
    }

    public async Task<IReadOnlyList<StartListPlanDto>> GetAllAsync(int userId, CancellationToken ct = default)
    {
        var plans = await _db.UserStartListPlans.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync(ct);

        return plans.Select(ToDto).ToList();
    }

    public async Task<StartListPlanDto> SaveAsync(
        int userId, int orgCompId, StartListPlanSaveRequest request, CancellationToken ct = default)
    {
        var plan = await _db.UserStartListPlans
            .FirstOrDefaultAsync(p => p.UserId == userId && p.OrgCompId == orgCompId, ct);

        if (plan is null)
        {
            plan = new UserStartListPlan { UserId = userId, OrgCompId = orgCompId };
            _db.UserStartListPlans.Add(plan);
        }

        plan.SwimmerIds = Pack(request.SwimmerIds);
        plan.ClubIds = Pack(request.ClubIds);
        plan.ImComing = request.ImComing;
        plan.NotifyMe = request.NotifyMe;
        plan.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return ToDto(plan);
    }

    public async Task<bool> DeleteAsync(int userId, int orgCompId, CancellationToken ct = default)
    {
        var plan = await _db.UserStartListPlans
            .FirstOrDefaultAsync(p => p.UserId == userId && p.OrgCompId == orgCompId, ct);
        if (plan is null) return false;

        _db.UserStartListPlans.Remove(plan);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static StartListPlanDto ToDto(UserStartListPlan p) => new(
        p.OrgCompId, Unpack(p.SwimmerIds), Unpack(p.ClubIds), p.ImComing, p.NotifyMe, p.UpdatedAt);

    /// <summary>Список id → «10,42,77». Дубли и мусор отбрасываем на входе, а не при чтении.</summary>
    private static string Pack(IReadOnlyList<int>? ids) =>
        ids is null or { Count: 0 }
            ? string.Empty
            : string.Join(',', ids.Where(id => id > 0).Distinct().Take(MaxIds));

    private static IReadOnlyList<int> Unpack(string raw) =>
        raw.Length == 0
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var id) ? id : 0)
                .Where(id => id > 0)
                .ToList();
}
