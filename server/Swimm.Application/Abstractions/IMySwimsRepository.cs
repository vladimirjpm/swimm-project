using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Агрегат страницы «My media v3»: заплывы favorite-пловцов юзера за сезон
/// с их медиа и реакциями (❤/🎉). Сезон — сентябрь–август, задаётся стартовым годом.
/// </summary>
public interface IMySwimsRepository
{
    /// <param name="season">Стартовый год сезона (2025 → сезон 2025/26); null — текущий сезон.</param>
    Task<MySwimsResponseDto> GetMySwimsAsync(int userId, int? season);
}
