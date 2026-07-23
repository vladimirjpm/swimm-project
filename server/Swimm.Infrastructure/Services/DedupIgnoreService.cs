using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// «Развязка» пар дедупа (Sys_DedupIgnoredPairs). Хранение нормализовано (IdA &lt; IdB);
/// повторное добавление той же пары — no-op. Имена для списка резолвятся по типу
/// (пловцы/клубы); пара с удалённой сущностью показывается с именем «#id (удалён)».
/// </summary>
public class DedupIgnoreService(SwimmDbContext db) : IDedupIgnoreService
{
    private static (int a, int b) Norm(int idA, int idB) =>
        idA <= idB ? (idA, idB) : (idB, idA);

    private static void Validate(string entityType)
    {
        if (entityType is not (DedupEntityType.Swimmer or DedupEntityType.Club))
            throw new ArgumentException($"Неизвестный тип пары дедупа: '{entityType}'");
    }

    public async Task AddAsync(string entityType, int idA, int idB, CancellationToken ct = default)
    {
        Validate(entityType);
        if (idA == idB) throw new ArgumentException("Пара из одного и того же Id");

        var (a, b) = Norm(idA, idB);
        var exists = await db.DedupIgnoredPairs
            .AnyAsync(p => p.EntityType == entityType && p.IdA == a && p.IdB == b, ct);
        if (exists) return;

        db.DedupIgnoredPairs.Add(new DedupIgnoredPair { EntityType = entityType, IdA = a, IdB = b });
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> RemoveAsync(string entityType, int idA, int idB, CancellationToken ct = default)
    {
        Validate(entityType);
        var (a, b) = Norm(idA, idB);
        var row = await db.DedupIgnoredPairs
            .FirstOrDefaultAsync(p => p.EntityType == entityType && p.IdA == a && p.IdB == b, ct);
        if (row is null) return false;

        db.DedupIgnoredPairs.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<DedupIgnoredPairDto>> ListAsync(string entityType, CancellationToken ct = default)
    {
        Validate(entityType);
        var pairs = await db.DedupIgnoredPairs.AsNoTracking()
            .Where(p => p.EntityType == entityType)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
        if (pairs.Count == 0) return [];

        var ids = pairs.SelectMany(p => new[] { p.IdA, p.IdB }).Distinct().ToList();
        var names = entityType == DedupEntityType.Swimmer
            ? await db.Swimmers.AsNoTracking()
                .Where(s => ids.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => $"{s.LastName} {s.FirstName}".Trim(), ct)
            : await db.Clubs.AsNoTracking()
                .Where(c => ids.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        string NameOf(int id) => names.TryGetValue(id, out var n) ? n : $"#{id} (удалён)";

        return pairs
            .Select(p => new DedupIgnoredPairDto(p.IdA, NameOf(p.IdA), p.IdB, NameOf(p.IdB), p.CreatedAt))
            .ToList();
    }
}
