using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Уровни пловцов группы (docs/plans/lane-plans-plan.md, L1). Все записи — под advisory-lock
/// группы: иначе два первых открытия карточки в одну секунду завели бы стандартный набор
/// дважды, а два клика по уровню одного пловца упали бы на ключе (HubGroupId, SwimmerId).
/// </summary>
public class HubGroupLevelService : IHubGroupLevelService
{
    private readonly SwimmDbContext _db;

    public HubGroupLevelService(SwimmDbContext db) => _db = db;

    /// <summary>
    /// Первый ключ транзакционной advisory-блокировки «уровни группы» (второй — id группы).
    /// Произвольная константа, лишь бы не совпала с другими блокировками в БД.
    /// </summary>
    private const int LevelsLockClass = 0x48474C56; // "HGLV" — hub group levels

    public async Task<HubGroupLevelsDto?> GetAsync(int hubGroupId)
    {
        if (!await _db.HubGroups.AnyAsync(g => g.Id == hubGroupId)) return null;

        if (!await _db.HubGroupLevels.AnyAsync(l => l.HubGroupId == hubGroupId))
            await InGroupTransactionAsync(hubGroupId, () => SeedDefaultsAsync(hubGroupId));

        // Видимый состав: скрытых клубных не видит ни один читатель состава (§4а).
        var swimmers = await _db.HubGroupMembers.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId && !m.IsExcluded)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Swimmer!.LastName).ThenBy(m => m.Swimmer!.FirstName)
            .Select(m => new HubGroupLevelSwimmerDto
            {
                SwimmerId = m.SwimmerId,
                Name = (m.Swimmer!.LastName + " " + m.Swimmer.FirstName).Trim(),
                NameEn = (m.Swimmer.LastNameEn + " " + m.Swimmer.FirstNameEn).Trim(),
                BirthYear = m.Swimmer.BirthYear,
                Gender = m.Swimmer.Gender,
                ClubName = m.Swimmer.Club != null ? m.Swimmer.Club.Name : null,
                LevelId = _db.HubGroupSwimmerLevels
                    .Where(l => l.HubGroupId == hubGroupId && l.SwimmerId == m.SwimmerId)
                    .Select(l => (int?)l.LevelId)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        var levels = await _db.HubGroupLevels.AsNoTracking()
            .Where(l => l.HubGroupId == hubGroupId)
            .OrderBy(l => l.Rank).ThenBy(l => l.Id)
            .Select(l => new HubGroupLevelDto
            {
                Id = l.Id, Rank = l.Rank, Name = l.Name, Description = l.Description, Color = l.Color,
            })
            .ToListAsync();

        // Счётчик — по составу, а не по таблице: ушедший из группы пловец уровень хранит
        // (вернётся — уровень на месте), но «на уровне» его не считаем.
        var counts = swimmers.Where(s => s.LevelId != null)
            .GroupBy(s => s.LevelId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
        foreach (var level in levels) level.SwimmerCount = counts.GetValueOrDefault(level.Id);

        return new HubGroupLevelsDto { Levels = levels, Swimmers = swimmers };
    }

    public async Task<HubGroupMemberSaveResult> SaveLevelsAsync(int hubGroupId, HubGroupLevelsInputDto input)
    {
        var (normalized, error) = HubGroupLevelRules.Normalize(input.Levels);
        if (normalized == null) return HubGroupMemberSaveResult.Fail(error!);

        if (!await _db.HubGroups.AnyAsync(g => g.Id == hubGroupId))
            return HubGroupMemberSaveResult.Fail($"Group #{hubGroupId} not found.");

        return await InGroupTransactionAsync(hubGroupId, async () =>
        {
            var existing = await _db.HubGroupLevels
                .Where(l => l.HubGroupId == hubGroupId)
                .ToDictionaryAsync(l => l.Id);

            // Id не из этой группы (или уже удалён соседней вкладкой) — не угадываем, просим обновить.
            if (normalized.Any(n => n.Id is int id && !existing.ContainsKey(id)))
                return HubGroupMemberSaveResult.Fail("Levels were changed elsewhere. Reload and try again.");

            var keep = normalized.Where(n => n.Id != null).Select(n => n.Id!.Value).ToHashSet();
            var removedIds = existing.Keys.Where(id => !keep.Contains(id)).ToList();
            if (removedIds.Count > 0)
            {
                // Назначения удаляем явно, не полагаясь на каскад базы: так же ведёт себя InMemory
                // в тестах, и трекер не держит строк, которых уже нет.
                var assignments = await _db.HubGroupSwimmerLevels
                    .Where(s => s.HubGroupId == hubGroupId && removedIds.Contains(s.LevelId))
                    .ToListAsync();
                _db.HubGroupSwimmerLevels.RemoveRange(assignments);

                // Дорожки планов теряют подпись (в базе это SET NULL) — план остаётся снимком.
                var lanes = await _db.LanePlanLanes
                    .Where(l => l.LevelId != null && removedIds.Contains(l.LevelId.Value))
                    .ToListAsync();
                foreach (var lane in lanes) lane.LevelId = null;

                _db.HubGroupLevels.RemoveRange(removedIds.Select(id => existing[id]));
            }

            foreach (var n in normalized)
            {
                var level = n.Id is int id ? existing[id] : new HubGroupLevel { HubGroupId = hubGroupId };
                level.Rank = n.Rank;
                level.Name = n.Name;
                level.Description = n.Description;
                level.Color = n.Color;
                if (n.Id == null) _db.HubGroupLevels.Add(level);
            }

            await _db.SaveChangesAsync();
            return HubGroupMemberSaveResult.Ok();
        });
    }

    public async Task<HubGroupMemberSaveResult> SetSwimmerLevelAsync(int hubGroupId, int swimmerId, int? levelId)
    {
        var inRoster = await _db.HubGroupMembers
            .AnyAsync(m => m.HubGroupId == hubGroupId && m.SwimmerId == swimmerId && !m.IsExcluded);
        if (!inRoster) return HubGroupMemberSaveResult.Fail("This swimmer is not in the group.");

        return await InGroupTransactionAsync(hubGroupId, async () =>
        {
            if (levelId is int id
                && !await _db.HubGroupLevels.AnyAsync(l => l.HubGroupId == hubGroupId && l.Id == id))
                return HubGroupMemberSaveResult.Fail("Levels were changed elsewhere. Reload and try again.");

            var row = await _db.HubGroupSwimmerLevels
                .FirstOrDefaultAsync(s => s.HubGroupId == hubGroupId && s.SwimmerId == swimmerId);

            if (levelId == null)
            {
                if (row != null) _db.HubGroupSwimmerLevels.Remove(row);
            }
            else if (row == null)
            {
                _db.HubGroupSwimmerLevels.Add(new HubGroupSwimmerLevel
                {
                    HubGroupId = hubGroupId, SwimmerId = swimmerId, LevelId = levelId.Value,
                });
            }
            else if (row.LevelId != levelId.Value)
            {
                // Часть ключа FK на уровень — обычное поле, не PK строки: правится на месте.
                row.LevelId = levelId.Value;
            }

            await _db.SaveChangesAsync();
            return HubGroupMemberSaveResult.Ok();
        });
    }

    /// <summary>Стандартный набор — только если уровней всё ещё нет (проверка уже под блокировкой).</summary>
    private async Task<int> SeedDefaultsAsync(int hubGroupId)
    {
        if (await _db.HubGroupLevels.AnyAsync(l => l.HubGroupId == hubGroupId)) return 0;

        var rank = 0;
        foreach (var (name, description) in HubGroupLevelRules.Defaults)
        {
            _db.HubGroupLevels.Add(new HubGroupLevel
            {
                HubGroupId = hubGroupId,
                Rank = ++rank,
                Name = name,
                Description = string.IsNullOrEmpty(description) ? null : description,
            });
        }
        await _db.SaveChangesAsync();
        return rank;
    }

    private Task<T> InGroupTransactionAsync<T>(int hubGroupId, Func<Task<T>> work) =>
        GroupAdvisoryLock.InGroupTransactionAsync(_db, LevelsLockClass, hubGroupId, work);
}
