using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Правка клубов (см. <see cref="IClubAdminRepository"/>). Имя клуба денормализованных копий
/// не имеет — публичные выдачи джойнят Clubs по ClubId, но кэшируются, поэтому после
/// переименования сбрасываем кэш целиком (иначе club-summary/результаты покажут старое имя).
/// </summary>
public class ClubAdminRepository(SwimmDbContext db, ICacheService cache) : IClubAdminRepository
{
    public async Task<ClubEditDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var c = await db.Clubs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c == null) return null;

        return new ClubEditDto
        {
            Id = c.Id,
            Name = c.Name,
            NameEn = c.NameEn,
            IsPseudo = c.IsPseudo,
            ResultCount = await db.Results.AsNoTracking().CountAsync(r => r.ClubId == id, ct),
            MergedIntoId = c.MergedIntoId,
            MergedIntoName = c.MergedIntoId == null
                ? null
                : await db.Clubs.AsNoTracking()
                    .Where(x => x.Id == c.MergedIntoId)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync(ct)
        };
    }

    public async Task<ClubSaveResult> UpdateAsync(int id, ClubInputDto input, CancellationToken ct = default)
    {
        var club = await db.Clubs.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (club == null) return ClubSaveResult.Fail($"Клуб #{id} не найден");

        var name = (input.Name ?? "").Trim();
        if (name.Length == 0) return ClubSaveResult.Fail("Название обязательно");

        club.Name = name;
        club.NameEn = (input.NameEn ?? "").Trim();
        club.IsPseudo = input.IsPseudo;

        await db.SaveChangesAsync(ct);
        await cache.InvalidateAllAsync();
        return ClubSaveResult.Ok();
    }
}
