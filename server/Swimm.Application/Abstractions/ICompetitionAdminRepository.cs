using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Порт админского CRUD соревнований (эталон для будущих CRUD: Clubs, Swimmers, …).
/// Каскадное удаление живёт в <see cref="IImportService.DeleteCompetitionAsync"/> — здесь
/// только чтение/создание/обновление и управление <see cref="CompetitionResultUrlDto"/> по OrgCompId.
/// </summary>
public interface ICompetitionAdminRepository
{
    /// <summary>Список с поиском (по Name/SubName) и пагинацией. Многодневные соревнования
    /// свёрнуты в одну строку-событие (<see cref="CompetitionRowDto"/>).</summary>
    Task<PagedResult<CompetitionRowDto>> GetPagedAsync(string? search, int page, int pageSize);

    /// <summary>Полные данные для формы Edit (включая URL-ы результатов). null — не найдено.</summary>
    Task<CompetitionEditDto?> GetByIdAsync(int id);

    /// <summary>Все категории (для чекбоксов формы), по DisplayOrder.</summary>
    Task<IReadOnlyList<CategoryTagDto>> GetAllCategoriesAsync();

    /// <summary>Создать. Ошибки уникальности (Name+Date+PoolType, OrgCompId) возвращаются в результате.</summary>
    Task<CompetitionSaveResult> CreateAsync(CompetitionInputDto input);

    /// <summary>Обновить основные поля. null-возврат Success=false с текстом при конфликте/отсутствии.</summary>
    Task<CompetitionSaveResult> UpdateAsync(int id, CompetitionInputDto input);

    // ── CompetitionResultUrls (связь по OrgCompId) ─────────────────────────────

    /// <summary>Добавить URL результатов. Требует заданного OrgCompId; проверяет уникальность (OrgCompId, Culture).</summary>
    Task<CompetitionSaveResult> AddResultUrlAsync(int orgCompId, string culture, string url);

    /// <summary>Удалить URL результатов по его Id, но только если он принадлежит соревнованию
    /// с этим OrgCompId (защита от удаления чужого URL по подделанному id). false — не найден/не совпал.</summary>
    Task<bool> RemoveResultUrlAsync(int urlId, int orgCompId);
}
