using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
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

    public async Task<ClubDeleteResult> DeleteEmptyAsync(int id, CancellationToken ct = default)
    {
        var club = await db.Clubs.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (club == null) return ClubDeleteResult.Fail($"Клуб #{id} не найден");

        var blocked = await WhyNotDeletableAsync(club, ct);
        if (blocked != null) return ClubDeleteResult.Fail(blocked);

        var name = club.Name;
        db.Clubs.Remove(club);
        await db.SaveChangesAsync(ct);
        await cache.InvalidateAllAsync();
        return ClubDeleteResult.Ok(name);
    }

    public async Task<ClubBulkDeleteResult> DeleteAllEmptyAsync(CancellationToken ct = default)
    {
        // Список берём тем же предикатом, что рисует фильтр «Без пловцов», но каждый клуб всё
        // равно проходит полную проверку — предикат не знает про избранное/заявки (FK RESTRICT).
        var ids = await db.Clubs
            .Where(c => !c.IsPseudo && !c.Name.StartsWith("SYNTH") && c.MergedIntoId == null)
            .Where(c => !db.Swimmers.Any(s => s.ClubId == c.Id))
            .Where(c => !db.Results.Any(r => r.ClubId == c.Id))
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var deleted = new List<ClubDeletedRow>();
        var skipped = new List<string>();

        foreach (var id in ids)
        {
            var club = await db.Clubs.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (club == null) continue;

            var blocked = await WhyNotDeletableAsync(club, ct);
            if (blocked != null) { skipped.Add(blocked); continue; }

            deleted.Add(new ClubDeletedRow(club.Id, club.Name));
            db.Clubs.Remove(club);
        }

        // Одна транзакция на всю пачку: либо чистим справочник целиком, либо не трогаем вовсе.
        if (deleted.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            await cache.InvalidateAllAsync();
        }

        return new ClubBulkDeleteResult(deleted, skipped);
    }

    /// <summary>
    /// Причина, по которой клуб удалять нельзя, или null. Предикат — тот же, что в
    /// DataQualityService."no-swimmers"; перепроверяем здесь, а не доверяем списку на странице:
    /// между отрисовкой и кликом импорт мог повесить на клуб результаты.
    /// </summary>
    private async Task<string?> WhyNotDeletableAsync(Club club, CancellationToken ct)
    {
        var id = club.Id;

        if (club.MergedIntoId != null)
            return $"Клуб #{id} склеен в #{club.MergedIntoId} — надгробие держит старые ссылки, удалять нельзя";
        if (club.IsPseudo)
            return $"Клуб #{id} — псевдо-клуб (сборная/страна), удаление не для него";
        if (await db.Results.AnyAsync(r => r.ClubId == id, ct))
            return $"У клуба #{id} есть результаты — удаление только для пустых";
        if (await db.Swimmers.AnyAsync(s => s.ClubId == id, ct))
            return $"У клуба #{id} есть пловцы — удаление только для пустых";

        // Ссылки с RESTRICT — упали бы исключением БД; отвечаем понятным текстом.
        if (await db.Clubs.AnyAsync(c => c.MergedIntoId == id, ct))
            return $"В клуб #{id} склеены другие клубы — он приёмник, удалять нельзя";
        if (await db.UserFavorites.AnyAsync(f => f.ClubId == id, ct))
            return $"Клуб #{id} у кого-то в избранном";
        if (await db.HubGroupClubRequests.AnyAsync(r => r.ClubId == id, ct))
            return $"На клуб #{id} есть заявка на статус клуба";

        return null;
    }
}
