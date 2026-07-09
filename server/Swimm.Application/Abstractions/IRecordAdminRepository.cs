using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Админский CRUD рекордов и нормативов (Admin/Records). Пишет через owner-контекст
/// (SwimmDbContext); каждая мутация обязана сбросить публичный кэш <see cref="IRecordRepository"/>
/// через <see cref="ICacheService"/> — иначе публичный /api/records отдаёт старое до 24ч.
/// Схему таблиц/осей не меняет — только валидирует значения против
/// <see cref="Swimm.Domain.Entities.Record"/> / <see cref="Swimm.Domain.Entities.NormativeStandard"/>.
/// </summary>
public interface IRecordAdminRepository
{
    /// <summary>Страница рекордов по фильтру (данных ~1.7к строк — пагинация обязательна).</summary>
    Task<PagedResult<RecordDto>> GetRecordsAsync(RecordFilter filter, int page, int pageSize);

    /// <summary>Создать рекорд. Ошибка — при недопустимых значениях осей или занятой позиции.</summary>
    Task<RecordSaveResult> CreateRecordAsync(RecordInputDto input);

    /// <summary>Инлайн-правка времени/держателя/даты — оси (позиция) не трогает.</summary>
    Task<RecordSaveResult> UpdateRecordAsync(int id, RecordQuickEditDto input);

    /// <summary>Удалить рекорд.</summary>
    Task<RecordSaveResult> DeleteRecordAsync(int id);

    /// <summary>Страница нормативов по фильтру (данных ~6.5к строк — пагинация обязательна).</summary>
    Task<PagedResult<NormativeStandardDto>> GetStandardsAsync(StandardFilter filter, int page, int pageSize);

    /// <summary>Создать норматив. Ошибка — при недопустимых значениях осей или занятой позиции.</summary>
    Task<RecordSaveResult> CreateStandardAsync(NormativeStandardInputDto input);

    /// <summary>Инлайн-правка порогового времени — оси не трогает.</summary>
    Task<RecordSaveResult> UpdateStandardAsync(int id, StandardQuickEditDto input);

    /// <summary>Удалить норматив.</summary>
    Task<RecordSaveResult> DeleteStandardAsync(int id);
}
