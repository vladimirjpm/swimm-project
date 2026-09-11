using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

public interface IUserFavoriteRepository
{
    Task<List<FavoriteDto>> GetForUserAsync(int userId);
    /// <summary>
    /// Добавить в избранное с проверкой лимита типа (<c>FavoritesRules</c>). Дубль проверяется
    /// раньше лимита: «уже в избранном» остаётся 409 и на пределе.
    /// </summary>
    Task<AddFavoriteResult> AddAsync(int userId, AddFavoriteRequest request);
    Task<bool> RemoveAsync(int userId, int favoriteId);
    Task<bool> SetPrimaryAsync(int userId, int favoriteId);
    Task<bool> UnsetPrimaryAsync(int userId, int favoriteId);
    Task<bool> ReorderAsync(int userId, List<ReorderItem> items);
}
