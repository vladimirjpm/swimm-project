using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Порт для чтения категорий соревнований.
/// </summary>
public interface ICategoryRepository
{
    /// <summary>Возвращает все категории, упорядоченные по DisplayOrder.</summary>
    Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync();

    /// <summary>
    /// Возвращает категорию по slug-ключу вместе со списком соревнований.
    /// Возвращает null, если категория не найдена.
    /// </summary>
    Task<CategoryDetailDto?> GetByKeyAsync(string key);
}
