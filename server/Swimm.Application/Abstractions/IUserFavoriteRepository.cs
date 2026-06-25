using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

public interface IUserFavoriteRepository
{
    Task<List<FavoriteDto>> GetForUserAsync(int userId);
    Task<FavoriteDto?> AddAsync(int userId, AddFavoriteRequest request);
    Task<bool> RemoveAsync(int userId, int favoriteId);
    Task<bool> SetPrimaryAsync(int userId, int favoriteId);
    Task<bool> UnsetPrimaryAsync(int userId, int favoriteId);
    Task<bool> ReorderAsync(int userId, List<ReorderItem> items);
}
