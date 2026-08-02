using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Правка справочника клубов (Admin/Clubs/Edit, фаза 7.3 op#2). Имя клуба нигде не
/// денормализовано (результаты ссылаются по ClubId, публичные выдачи джойнят), поэтому
/// «каскад» переименования — это инвалидация кэша агрегатов. Дедуп/удаление — отдельно (merge).
/// </summary>
public interface IClubAdminRepository
{
    /// <summary>Данные клуба для формы. null — не найден.</summary>
    Task<ClubEditDto?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Обновить имя/флаги клуба. Пустое имя — ошибка.</summary>
    Task<ClubSaveResult> UpdateAsync(int id, ClubInputDto input, CancellationToken ct = default);

    /// <summary>
    /// Удалить пустой клуб — мусор парсера из фильтра «Без пловцов» (0 пловцов И 0 результатов).
    /// Предикат перепроверяется на сервере: склеенный, псевдо- или непустой клуб удалить нельзя
    /// (склеенные — надгробия для старых ссылок `/clubs/{id}`, их удаление ломает историю).
    /// </summary>
    Task<ClubDeleteResult> DeleteEmptyAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Пакетная чистка: удалить все пустые клубы (весь список фильтра «Без пловцов», не только
    /// видимые топ-200). Каждый проходит ту же полную проверку; непрошедшие попадают в Skipped,
    /// остальные удаляются одной транзакцией.
    /// </summary>
    Task<ClubBulkDeleteResult> DeleteAllEmptyAsync(CancellationToken ct = default);
}
