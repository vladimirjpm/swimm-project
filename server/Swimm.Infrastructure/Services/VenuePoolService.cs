using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Справочник бассейнов (см. <see cref="IVenuePoolService"/>): весь справочник — один запрос
/// (десятки строк), ответ считает чистый <see cref="VenuePoolDirectory"/>.
/// </summary>
public sealed class VenuePoolService(SwimmDbContext db) : IVenuePoolService
{
    public async Task<VenuePoolLookup> LookupAsync(string? venue, CancellationToken ct = default)
    {
        // Без места базу не трогаем — ответ и так известен.
        if (VenueNameNormalizer.Normalize(venue) is null) return VenuePoolDirectory.Empty.Resolve(venue);
        return (await LoadAsync(ct)).Resolve(venue);
    }

    public async Task<VenuePoolDirectory> LoadAsync(CancellationToken ct = default)
    {
        var rows = await db.VenuePoolAliases.AsNoTracking()
            .Select(a => new
            {
                a.NormalizedAlias,
                a.VenuePool.Id, a.VenuePool.Name, a.VenuePool.PoolType, a.VenuePool.IsAmbiguous,
                a.VenuePool.Note, a.VenuePool.ConfirmedBy, a.VenuePool.ConfirmedAt,
            })
            .ToListAsync(ct);

        return new VenuePoolDirectory(rows.Select(r => KeyValuePair.Create(
            r.NormalizedAlias,
            new VenuePoolEntry(r.Id, r.Name, r.PoolType, r.IsAmbiguous, r.Note, r.ConfirmedBy, r.ConfirmedAt))));
    }
}
