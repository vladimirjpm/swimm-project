using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Порт админского CRUD категорий соревнований (Admin/Categories). Категории с ключом из
/// <see cref="Swimm.Domain.Entities.Category.ReservedKeys"/> зашиты в логику сервера/клиента —
/// у них нельзя менять Key и нельзя удалять (см. <see cref="CategoryEditDto.IsReserved"/>).
/// </summary>
public interface ICategoryAdminRepository
{
    /// <summary>Все категории по DisplayOrder, с числом связанных соревнований.</summary>
    Task<IReadOnlyList<CategoryAdminRowDto>> GetAllAsync();

    /// <summary>Данные для формы Edit. null — не найдено.</summary>
    Task<CategoryEditDto?> GetByIdAsync(int id);

    /// <summary>Создать. Ошибка — при пустом/занятом Key или Name.</summary>
    Task<CategorySaveResult> CreateAsync(CategoryInputDto input);

    /// <summary>Обновить. Для зарезервированных категорий Key менять нельзя (ошибка, если отличается).</summary>
    Task<CategorySaveResult> UpdateAsync(int id, CategoryInputDto input);

    /// <summary>Удалить. Отказ — если категория зарезервирована или в ней ещё есть соревнования.</summary>
    Task<CategorySaveResult> DeleteAsync(int id);
}
