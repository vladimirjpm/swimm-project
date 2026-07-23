using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Порт админского CRUD стилей плавания (Admin/Styles). Посевные стили с именем из
/// <see cref="Swimm.Domain.Entities.Style.ReservedNames"/> зашиты в парсер/импорт/рекорды —
/// у них нельзя менять имя и нельзя удалять (см. <see cref="StyleEditDto.IsReserved"/>).
/// </summary>
public interface IStyleAdminRepository
{
    /// <summary>Все стили по имени, с числом ссылающихся результатов.</summary>
    Task<IReadOnlyList<StyleAdminRowDto>> GetAllAsync();

    /// <summary>Данные для формы Edit. null — не найдено.</summary>
    Task<StyleEditDto?> GetByIdAsync(int id);

    /// <summary>Создать. Ошибка — при пустом/занятом имени.</summary>
    Task<StyleSaveResult> CreateAsync(StyleInputDto input);

    /// <summary>Обновить. Для зарезервированных стилей имя менять нельзя.</summary>
    Task<StyleSaveResult> UpdateAsync(int id, StyleInputDto input);

    /// <summary>Удалить. Отказ — если стиль зарезервирован или на него ссылаются результаты.</summary>
    Task<StyleSaveResult> DeleteAsync(int id);
}
