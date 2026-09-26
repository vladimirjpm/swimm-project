using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// План дорожек группы на дату (docs/plans/lane-plans-plan.md, L2). Запись — под advisory-lock
/// группы: два первых «Save» одного дня в одну секунду иначе упали бы на UNIQUE (группа, дата).
/// </summary>
public class LanePlanService : ILanePlanService
{
    private readonly SwimmDbContext _db;

    public LanePlanService(SwimmDbContext db) => _db = db;

    /// <summary>Первый ключ advisory-блокировки «планы дорожек группы» (второй — id группы).</summary>
    private const int LanePlansLockClass = 0x48474C50; // "HGLP" — hub group lane plans

    /// <summary>Сколько планов отдаёт список — выбор даты, а не архив.</summary>
    private const int ListLimit = 200;

    /// <summary>Пловец видимого состава с местом в порядке состава.</summary>
    private sealed record RosterRow(int SwimmerId, string Name, string NameEn, int BirthYear, int SortKey);

    public async Task<List<LanePlanSummaryDto>> ListAsync(int hubGroupId, bool isManager)
    {
        var rows = await _db.LanePlans.AsNoTracking()
            .Where(p => p.HubGroupId == hubGroupId && (isManager || p.Status == LanePlanStatus.Published))
            .OrderByDescending(p => p.Date)
            .Take(ListLimit)
            .Select(p => new { p.Date, p.Status, p.LaneCount })
            .ToListAsync();

        return rows.Select(p => new LanePlanSummaryDto
        {
            Date = LanePlanRules.FormatDate(p.Date), Status = p.Status, LaneCount = p.LaneCount,
        }).ToList();
    }

    public async Task<LanePlanDto?> GetAsync(int hubGroupId, DateOnly date, bool isManager, int? viewerUserId = null)
    {
        var plan = await _db.LanePlans.AsNoTracking()
            .Include(p => p.Lanes).ThenInclude(l => l.Level)
            .Include(p => p.Swimmers)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.HubGroupId == hubGroupId && p.Date == date);
        if (plan == null || (!isManager && plan.Status != LanePlanStatus.Published)) return null;

        var roster = await LoadRosterAsync(hubGroupId);
        var levels = await LoadSwimmerLevelsAsync(hubGroupId);
        var rosterById = roster.ToDictionary(r => r.SwimmerId);

        // Ушедшие из состава остаются в снимке — имена берём из справочника пловцов.
        var departedIds = plan.Swimmers.Select(s => s.SwimmerId).Where(id => !rosterById.ContainsKey(id)).ToList();
        var departed = departedIds.Count == 0
            ? new Dictionary<int, RosterRow>()
            : (await _db.Swimmers.AsNoTracking()
                .Where(s => departedIds.Contains(s.Id))
                .Select(s => new
                {
                    s.Id,
                    Name = (s.LastName + " " + s.FirstName).Trim(),
                    NameEn = (s.LastNameEn + " " + s.FirstNameEn).Trim(),
                    s.BirthYear,
                })
                .ToListAsync())
              .ToDictionary(s => s.Id, s => new RosterRow(s.Id, s.Name, s.NameEn, s.BirthYear, int.MaxValue));

        LanePlanSwimmerDto ToDto(int swimmerId)
        {
            var inRoster = rosterById.TryGetValue(swimmerId, out var row);
            if (!inRoster) departed.TryGetValue(swimmerId, out row);
            return new LanePlanSwimmerDto
            {
                SwimmerId = swimmerId,
                Name = row?.Name ?? "",
                NameEn = row?.NameEn ?? "",
                BirthYear = row?.BirthYear ?? 0,
                LevelId = levels.TryGetValue(swimmerId, out var levelId) ? levelId : null,
                LeftGroup = !inRoster,
            };
        }

        int SortKey(int swimmerId) => rosterById.TryGetValue(swimmerId, out var r) ? r.SortKey : int.MaxValue;

        List<LanePlanSwimmerDto> Bucket(int? laneNo) => plan.Swimmers
            .Where(s => s.LaneNo == laneNo)
            .OrderBy(s => s.OrderNo).ThenBy(s => SortKey(s.SwimmerId)).ThenBy(s => s.SwimmerId)
            .Select(s => ToDto(s.SwimmerId))
            .ToList();

        var inPlan = plan.Swimmers.Select(s => s.SwimmerId).ToHashSet();
        var mySwimmers = viewerUserId is int uid ? await LoadMySwimmersAsync(hubGroupId, uid, inPlan) : [];

        return new LanePlanDto
        {
            Date = LanePlanRules.FormatDate(plan.Date),
            Status = plan.Status,
            LaneCount = plan.LaneCount,
            Note = plan.Note,
            UpdatedAt = plan.UpdatedAt,
            Lanes = plan.Lanes
                .Where(l => l.LaneNo <= plan.LaneCount)
                .OrderBy(l => l.LaneNo)
                .Select(l => new LanePlanLaneDto
                {
                    LaneNo = l.LaneNo,
                    Level = l.Level == null ? null : new LanePlanLevelDto
                    {
                        Id = l.Level.Id, Rank = l.Level.Rank, Name = l.Level.Name, Color = l.Level.Color,
                    },
                    Workout = l.Workout,
                    Swimmers = Bucket(l.LaneNo),
                })
                .ToList(),
            Unassigned = Bucket(null),
            // Кого тренер снял — видит только он: участнику это знать незачем.
            NotToday = isManager
                ? roster.Where(r => !inPlan.Contains(r.SwimmerId)).Select(r => ToDto(r.SwimmerId)).ToList()
                : [],
            CanEdit = isManager,
            MySwimmers = mySwimmers,
        };
    }

    /// <summary>
    /// «Мои» пловцы плана (L4, «Your lane») — только подсветка, прав не даёт, поэтому годятся и
    /// недоверенные источники (правило «primary favorite недоверенный» — про права, не про
    /// подсветку). «me»: привязка аккаунта админом сайта (<c>AppUser.SwimmerId</c>) и избранное
    /// «Me»; «family»: избранное-семья и метка членства в этой группе. Берём только тех, кто в
    /// плане (на дорожке или в Unassigned): «Not today» участник не видит.
    /// </summary>
    private async Task<List<LanePlanMySwimmerDto>> LoadMySwimmersAsync(int hubGroupId, int userId, IReadOnlySet<int> inPlan)
    {
        if (inPlan.Count == 0) return [];

        var linked = await _db.AppUsers.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.SwimmerId)
            .FirstOrDefaultAsync();

        var favorites = await _db.UserFavorites.AsNoTracking()
            .Where(f => f.UserId == userId && f.SwimmerId != null && (f.IsPrimary || f.IsFamily))
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Id)
            .Select(f => new { SwimmerId = f.SwimmerId!.Value, f.IsPrimary })
            .ToListAsync();

        var label = await _db.HubGroupUserMembers.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId && m.UserId == userId && m.SwimmerId != null)
            .Select(m => m.SwimmerId)
            .FirstOrDefaultAsync();

        var ordered = new List<(int SwimmerId, string Kind)>();
        if (linked is int self) ordered.Add((self, "me"));
        ordered.AddRange(favorites.Where(f => f.IsPrimary).Select(f => (f.SwimmerId, "me")));
        ordered.AddRange(favorites.Where(f => !f.IsPrimary).Select(f => (f.SwimmerId, "family")));
        if (label is int labelled) ordered.Add((labelled, "family"));

        var seen = new HashSet<int>();
        return ordered
            .Where(x => inPlan.Contains(x.SwimmerId) && seen.Add(x.SwimmerId))
            .Select(x => new LanePlanMySwimmerDto { SwimmerId = x.SwimmerId, Kind = x.Kind })
            .ToList();
    }

    public async Task<HubGroupMemberSaveResult> SaveAsync(int hubGroupId, DateOnly date, LanePlanInputDto input, int userId)
    {
        var (normalized, error) = LanePlanRules.Normalize(input);
        if (normalized == null) return HubGroupMemberSaveResult.Fail(error!);

        var levelError = await CheckLevelsAsync(hubGroupId, normalized.Lanes);
        if (levelError != null) return HubGroupMemberSaveResult.Fail(levelError);

        return await GroupAdvisoryLock.InGroupTransactionAsync(_db, LanePlansLockClass, hubGroupId, async () =>
        {
            var plan = await _db.LanePlans
                .Include(p => p.Lanes)
                .Include(p => p.Swimmers)
                .AsSplitQuery()
                .FirstOrDefaultAsync(p => p.HubGroupId == hubGroupId && p.Date == date);

            // Пловец — из видимого состава или уже стоял в этом плане (ушёл из группы, снимок держит).
            var allowed = (await _db.HubGroupMembers
                    .Where(m => m.HubGroupId == hubGroupId && !m.IsExcluded)
                    .Select(m => m.SwimmerId)
                    .ToListAsync())
                .ToHashSet();
            if (plan != null) allowed.UnionWith(plan.Swimmers.Select(s => s.SwimmerId));
            if (normalized.Swimmers.Any(s => !allowed.Contains(s.SwimmerId)))
                return HubGroupMemberSaveResult.Fail("Someone in the plan is not in the group. Reload and try again.");

            var now = DateTime.UtcNow;
            if (plan == null)
            {
                plan = new LanePlan
                {
                    HubGroupId = hubGroupId, Date = date, Status = LanePlanStatus.Draft,
                    CreatedByUserId = userId, CreatedAt = now,
                };
                _db.LanePlans.Add(plan);
            }

            plan.LaneCount = normalized.LaneCount;
            plan.Note = normalized.Note;
            plan.UpdatedAt = now;

            // Правка на месте, а не «удалить всё и вставить»: у строк составные ключи, и удалённая
            // с тем же ключом, что новая, сбивала бы трекер EF.
            var lanes = plan.Lanes.ToDictionary(l => l.LaneNo);
            foreach (var extra in lanes.Values.Where(l => l.LaneNo > normalized.LaneCount).ToList())
                plan.Lanes.Remove(extra);
            foreach (var n in normalized.Lanes)
            {
                if (!lanes.TryGetValue(n.LaneNo, out var lane))
                {
                    lane = new LanePlanLane { LaneNo = n.LaneNo };
                    plan.Lanes.Add(lane);
                }
                lane.LevelId = n.LevelId;
                lane.Workout = n.Workout;
            }

            var swimmers = plan.Swimmers.ToDictionary(s => s.SwimmerId);
            var keep = normalized.Swimmers.Select(s => s.SwimmerId).ToHashSet();
            foreach (var gone in swimmers.Values.Where(s => !keep.Contains(s.SwimmerId)).ToList())
                plan.Swimmers.Remove(gone);
            foreach (var n in normalized.Swimmers)
            {
                if (!swimmers.TryGetValue(n.SwimmerId, out var row))
                {
                    row = new LanePlanSwimmer { SwimmerId = n.SwimmerId };
                    plan.Swimmers.Add(row);
                }
                row.LaneNo = n.LaneNo;
                row.OrderNo = n.OrderNo;
            }

            await _db.SaveChangesAsync();
            return HubGroupMemberSaveResult.Ok();
        });
    }

    public async Task<(LanePlanDistributionDto? Result, string? Error)> DistributeAsync(
        int hubGroupId, LanePlanDistributeInputDto input)
    {
        var (lanes, error) = LanePlanRules.NormalizeLanes(input.LaneCount, input.Lanes);
        if (lanes == null) return (null, error);

        var levelError = await CheckLevelsAsync(hubGroupId, lanes);
        if (levelError != null) return (null, levelError);

        var roster = await LoadRosterAsync(hubGroupId);
        var levels = await LoadSwimmerLevelsAsync(hubGroupId);

        // Кто пришёл: весь состав или названные. Названный не из состава — ушедший, но стоявший
        // в плане: раскладываем и его (в конец порядка), выкидывать молча нельзя.
        var sortKeys = roster.ToDictionary(r => r.SwimmerId, r => r.SortKey);
        var present = input.SwimmerIds == null
            ? roster.Select(r => r.SwimmerId).ToList()
            : input.SwimmerIds.Distinct().ToList();

        var placements = LaneDistribution.Distribute(
            lanes.Select(l => new LaneDistribution.Lane(l.LaneNo, l.LevelId)),
            present.Select(id => new LaneDistribution.Swimmer(
                id,
                levels.TryGetValue(id, out var levelId) ? levelId : null,
                sortKeys.TryGetValue(id, out var key) ? key : int.MaxValue)));

        return (new LanePlanDistributionDto
        {
            Swimmers = placements.Select(p => new LanePlanSwimmerInputDto { SwimmerId = p.SwimmerId, LaneNo = p.LaneNo }).ToList(),
        }, null);
    }

    public async Task<(LanePlanAutoLanesDto? Result, string? Error)> AutoLanesAsync(
        int hubGroupId, LanePlanAutoLanesInputDto input)
    {
        if (input.LaneCount is < LanePlanRules.MinLanes or > LanePlanRules.MaxLanes)
            return (null, $"Lanes: from {LanePlanRules.MinLanes} to {LanePlanRules.MaxLanes}.");

        var roster = await LoadRosterAsync(hubGroupId);
        var levels = await LoadSwimmerLevelsAsync(hubGroupId);
        var groupLevels = await _db.HubGroupLevels.AsNoTracking()
            .Where(l => l.HubGroupId == hubGroupId)
            .Select(l => new LaneAllocation.Level(l.Id, l.Rank))
            .ToListAsync();

        // Кто пришёл — как в Distribute: весь состав или названные (ушедший, но стоявший в плане, — в конец).
        var sortKeys = roster.ToDictionary(r => r.SwimmerId, r => r.SortKey);
        var present = input.SwimmerIds == null
            ? roster.Select(r => r.SwimmerId).ToList()
            : input.SwimmerIds.Distinct().ToList();
        var swimmers = present.Select(id => new LaneDistribution.Swimmer(
            id,
            levels.TryGetValue(id, out var levelId) ? levelId : null,
            sortKeys.TryGetValue(id, out var key) ? key : int.MaxValue)).ToList();

        if (!swimmers.Any(s => s.LevelId != null))
            return (null, "Nobody coming today has a level yet — set levels in Admin → Levels first.");

        var result = LaneAllocation.AutoLanes(input.LaneCount, groupLevels, swimmers);
        return (new LanePlanAutoLanesDto
        {
            Lanes = result.Lanes.Select(l => new LanePlanLaneInputDto { LaneNo = l.LaneNo, LevelId = l.LevelId }).ToList(),
            Swimmers = result.Placements.Select(p => new LanePlanSwimmerInputDto { SwimmerId = p.SwimmerId, LaneNo = p.LaneNo }).ToList(),
        }, null);
    }

    public async Task<bool> SetStatusAsync(int hubGroupId, DateOnly date, string status)
    {
        var plan = await _db.LanePlans.FirstOrDefaultAsync(p => p.HubGroupId == hubGroupId && p.Date == date);
        if (plan == null) return false;
        if (plan.Status == status) return true;

        plan.Status = status;
        plan.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int hubGroupId, DateOnly date)
    {
        var plan = await _db.LanePlans
            .Include(p => p.Lanes)
            .Include(p => p.Swimmers)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.HubGroupId == hubGroupId && p.Date == date);
        if (plan == null) return false;

        _db.LanePlans.Remove(plan);
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>Видимый состав в порядке группы: SortOrder, затем имя (скрытых клубных нет).</summary>
    private async Task<List<RosterRow>> LoadRosterAsync(int hubGroupId)
    {
        var rows = await _db.HubGroupMembers.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId && !m.IsExcluded)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Swimmer!.LastName).ThenBy(m => m.Swimmer!.FirstName)
            .ThenBy(m => m.SwimmerId)
            .Select(m => new
            {
                m.SwimmerId,
                Name = (m.Swimmer!.LastName + " " + m.Swimmer.FirstName).Trim(),
                NameEn = (m.Swimmer.LastNameEn + " " + m.Swimmer.FirstNameEn).Trim(),
                m.Swimmer.BirthYear,
            })
            .ToListAsync();

        return rows.Select((r, i) => new RosterRow(r.SwimmerId, r.Name, r.NameEn, r.BirthYear, i)).ToList();
    }

    private Task<Dictionary<int, int>> LoadSwimmerLevelsAsync(int hubGroupId) =>
        _db.HubGroupSwimmerLevels.AsNoTracking()
            .Where(l => l.HubGroupId == hubGroupId)
            .ToDictionaryAsync(l => l.SwimmerId, l => l.LevelId);

    /// <summary>Уровни дорожек — только этой группы (FK на уровень простой, группу он не держит).</summary>
    private async Task<string?> CheckLevelsAsync(int hubGroupId, IEnumerable<LanePlanRules.NormalizedLane> lanes)
    {
        var ids = lanes.Where(l => l.LevelId != null).Select(l => l.LevelId!.Value).Distinct().ToList();
        if (ids.Count == 0) return null;

        var found = await _db.HubGroupLevels.CountAsync(l => l.HubGroupId == hubGroupId && ids.Contains(l.Id));
        return found == ids.Count ? null : "Levels were changed elsewhere. Reload and try again.";
    }
}
