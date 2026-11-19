using Microsoft.EntityFrameworkCore;
using Npgsql;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Application.Validation;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Админский CRUD справочника бассейнов (/Admin/VenuePools, см. <see cref="IVenuePoolAdminRepository"/>).
/// Написания хранятся строками <see cref="VenuePoolAlias"/>; правка места заменяет их набор целиком.
/// </summary>
public sealed class VenuePoolAdminRepository(SwimmDbContext db) : IVenuePoolAdminRepository
{
    public async Task<IReadOnlyList<VenuePoolRowDto>> GetAllAsync(CancellationToken ct = default)
    {
        var pools = await db.VenuePools.AsNoTracking().Include(v => v.Aliases)
            .OrderBy(v => v.Name).ToListAsync(ct);

        // Сколько стартов «Входящих» закрывает каждое место — тем же резолвером, что у превью.
        var directory = Directory(pools);
        var inbox = await InboxVenuesAsync(ct);
        var counts = new Dictionary<int, int>();
        foreach (var v in inbox)
            if (directory.Resolve(v.Venue).Entry is { } e)
                counts[e.Id] = counts.GetValueOrDefault(e.Id) + v.Count;

        return pools.Select(p => ToRow(p, counts.GetValueOrDefault(p.Id))).ToList();
    }

    public async Task<VenuePoolRowDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var p = await db.VenuePools.AsNoTracking().Include(v => v.Aliases).FirstOrDefaultAsync(v => v.Id == id, ct);
        return p is null ? null : ToRow(p, 0);
    }

    public async Task<IReadOnlyList<UnknownVenueDto>> GetUnknownVenuesAsync(CancellationToken ct = default)
    {
        var pools = await db.VenuePools.AsNoTracking().Include(v => v.Aliases).ToListAsync(ct);
        var directory = Directory(pools);
        return (await InboxVenuesAsync(ct))
            .Where(v => directory.Resolve(v.Venue).Status == VenuePoolLookupStatus.NotInDirectory)
            .OrderByDescending(v => v.LastDate).ThenBy(v => v.Venue, StringComparer.Ordinal)
            .ToList();
    }

    public Task<VenuePoolSaveResult> CreateAsync(VenuePoolInputDto input, string confirmedBy, CancellationToken ct = default) =>
        SaveAsync(null, input, confirmedBy, ct);

    public Task<VenuePoolSaveResult> UpdateAsync(int id, VenuePoolInputDto input, string confirmedBy, CancellationToken ct = default) =>
        SaveAsync(id, input, confirmedBy, ct);

    public async Task<VenuePoolSaveResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var n = await db.VenuePools.Where(v => v.Id == id).ExecuteDeleteAsync(ct); // написания — каскадом
        return n == 0 ? VenuePoolSaveResult.Fail("Место не найдено") : VenuePoolSaveResult.Ok(id);
    }

    private async Task<VenuePoolSaveResult> SaveAsync(int? id, VenuePoolInputDto input, string confirmedBy, CancellationToken ct)
    {
        var (value, error) = VenuePoolInputValidator.Validate(input);
        if (value is null) return VenuePoolSaveResult.Fail(error!);

        // Написание уже ведёт к ДРУГОМУ месту — понятная ошибка вместо нарушения UNIQUE.
        var keys = value.Aliases.Select(a => a.Key).ToList();
        var taken = await db.VenuePoolAliases.AsNoTracking()
            .Where(a => keys.Contains(a.NormalizedAlias) && a.VenuePoolId != (id ?? 0))
            .Select(a => new { a.Alias, Owner = a.VenuePool.Name })
            .FirstOrDefaultAsync(ct);
        if (taken is not null)
            return VenuePoolSaveResult.Fail($"Написание «{taken.Alias}» уже относится к месту «{taken.Owner}»");

        if (await db.VenuePools.AnyAsync(v => v.Name == value.Name && v.Id != (id ?? 0), ct))
            return VenuePoolSaveResult.Fail($"Место «{value.Name}» уже есть");

        VenuePool pool;
        if (id is int existingId)
        {
            var found = await db.VenuePools.Include(v => v.Aliases).FirstOrDefaultAsync(v => v.Id == existingId, ct);
            if (found is null) return VenuePoolSaveResult.Fail("Место не найдено");
            pool = found;
            db.VenuePoolAliases.RemoveRange(pool.Aliases);
            pool.Aliases.Clear();
        }
        else
        {
            pool = new VenuePool();
            db.VenuePools.Add(pool);
        }

        pool.Name = value.Name;
        pool.PoolType = value.PoolType;
        pool.IsAmbiguous = value.IsAmbiguous;
        pool.Note = value.Note;
        pool.ConfirmedBy = Truncate(string.IsNullOrWhiteSpace(confirmedBy) ? "admin" : confirmedBy.Trim(),
            VenuePool.ConfirmedByMaxLength);
        pool.ConfirmedAt = DateTime.UtcNow;
        foreach (var (alias, key) in value.Aliases)
            pool.Aliases.Add(new VenuePoolAlias { Alias = alias, NormalizedAlias = key });

        try
        {
            // Снятие старых написаний и вставка новых — одним SaveChanges (одна транзакция):
            // EF удаляет раньше, чем вставляет, так что «то же написание заново» UNIQUE не задевает.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return VenuePoolSaveResult.Fail("Такое место или написание уже есть (сохранили параллельно?) — обновите страницу");
        }

        return VenuePoolSaveResult.Ok(pool.Id);
    }

    /// <summary>Места «Входящих» (только плавание) с числом стартов и последней датой.</summary>
    private async Task<List<UnknownVenueDto>> InboxVenuesAsync(CancellationToken ct) =>
        (await db.DiscoveredCompetitions.AsNoTracking()
            .Where(d => d.Discipline == Disciplines.Swimming && d.Venue != null && d.Venue.Trim() != "")
            .GroupBy(d => d.Venue!)
            .Select(g => new { Venue = g.Key, Count = g.Count(), Last = g.Max(d => d.DateStart) })
            .ToListAsync(ct))
        .GroupBy(g => g.Venue.Trim(), StringComparer.Ordinal)
        .Select(g => new UnknownVenueDto(g.Key, g.Sum(x => x.Count), g.Max(x => x.Last)))
        .ToList();

    private static VenuePoolDirectory Directory(IEnumerable<VenuePool> pools) =>
        new(pools.SelectMany(p => p.Aliases.Select(a => KeyValuePair.Create(a.NormalizedAlias,
            new VenuePoolEntry(p.Id, p.Name, p.PoolType, p.IsAmbiguous, p.Note, p.ConfirmedBy, p.ConfirmedAt)))));

    private static VenuePoolRowDto ToRow(VenuePool p, int inboxCount) => new(
        p.Id, p.Name, p.PoolType, p.IsAmbiguous, p.Note, p.ConfirmedBy, p.ConfirmedAt,
        p.Aliases.Select(a => a.Alias).Where(a => a != p.Name).OrderBy(a => a, StringComparer.Ordinal).ToList(),
        inboxCount);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
