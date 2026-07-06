using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

public class UserFavoriteRepository : IUserFavoriteRepository
{
    private readonly SwimmDbContext _db;

    public UserFavoriteRepository(SwimmDbContext db)
    {
        _db = db;
    }

    public async Task<List<FavoriteDto>> GetForUserAsync(int userId)
    {
        return await _db.UserFavorites
            .AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Id)
            .Select(f => new FavoriteDto
            {
                Id = f.Id,
                TargetType = f.TargetType,
                SwimmerId = f.SwimmerId,
                SwimmerName = f.Swimmer != null
                    ? f.Swimmer.LastName + " " + f.Swimmer.FirstName
                    : null,
                ClubId = f.ClubId,
                ClubName = f.Club != null ? f.Club.Name : null,
                IsPrimary = f.IsPrimary,
                SortOrder = f.SortOrder,
                CreatedAt = f.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<FavoriteDto?> AddAsync(int userId, AddFavoriteRequest request)
    {
        var fav = new UserFavorite
        {
            UserId = userId,
            TargetType = request.TargetType,
            SwimmerId = request.SwimmerId,
            ClubId = request.ClubId,
            IsPrimary = false,
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow
        };

        _db.UserFavorites.Add(fav);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Нарушение unique-constraint → дубль, возвращаем null (409 Conflict на уровне контроллера).
            _db.Entry(fav).State = EntityState.Detached;
            return null;
        }

        return await _db.UserFavorites
            .AsNoTracking()
            .Where(f => f.Id == fav.Id)
            .Select(f => new FavoriteDto
            {
                Id = f.Id,
                TargetType = f.TargetType,
                SwimmerId = f.SwimmerId,
                SwimmerName = f.Swimmer != null
                    ? f.Swimmer.LastName + " " + f.Swimmer.FirstName
                    : null,
                ClubId = f.ClubId,
                ClubName = f.Club != null ? f.Club.Name : null,
                IsPrimary = f.IsPrimary,
                SortOrder = f.SortOrder,
                CreatedAt = f.CreatedAt
            })
            .FirstOrDefaultAsync();
    }

    public async Task<bool> RemoveAsync(int userId, int favoriteId)
    {
        // IDOR: фильтруем по userId — нельзя удалить чужой фаворит.
        var fav = await _db.UserFavorites
            .FirstOrDefaultAsync(f => f.Id == favoriteId && f.UserId == userId);

        if (fav == null) return false;

        _db.UserFavorites.Remove(fav);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetPrimaryAsync(int userId, int favoriteId)
    {
        // IDOR: проверяем, что фаворит принадлежит текущему пользователю и является swimmer.
        var target = await _db.UserFavorites
            .FirstOrDefaultAsync(f => f.Id == favoriteId && f.UserId == userId && f.TargetType == "swimmer");

        if (target == null) return false;

        // Clear СНАЧАЛА своим SaveChangesAsync, потом set — раздельно, а не одним вызовом.
        // Partial unique index UX_UserFav_OnePrimary проверяется immediately (не deferred):
        // если бы EF отправил UPDATE "target → true" раньше UPDATE "old → false" в одном
        // SaveChanges (порядок операторов внутри одного SaveChanges не гарантирован), это
        // временно давало бы два primary одновременно → нарушение индекса. Раздельные вызовы
        // убирают эту гонку с самим собой полностью (не просто глушат исключение).
        var currentPrimaries = await _db.UserFavorites
            .Where(f => f.UserId == userId && f.IsPrimary && f.TargetType == "swimmer" && f.Id != favoriteId)
            .ToListAsync();

        if (currentPrimaries.Count > 0)
        {
            foreach (var f in currentPrimaries)
                f.IsPrimary = false;
            await _db.SaveChangesAsync();
        }

        target.IsPrimary = true;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Гонка с ДРУГИМ конкурентным запросом (не с самим собой, та убрана выше) → no-op, не 500.
        }
        return true;
    }

    public async Task<bool> UnsetPrimaryAsync(int userId, int favoriteId)
    {
        // IDOR: only the owner's swimmer favorite can be cleared.
        var fav = await _db.UserFavorites
            .FirstOrDefaultAsync(f => f.Id == favoriteId && f.UserId == userId && f.TargetType == "swimmer");

        if (fav == null) return false;

        fav.IsPrimary = false;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ReorderAsync(int userId, List<ReorderItem> items)
    {
        foreach (var item in items)
        {
            await _db.UserFavorites
                .Where(f => f.Id == item.Id && f.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.SortOrder, item.SortOrder));
        }
        return true;
    }
}
