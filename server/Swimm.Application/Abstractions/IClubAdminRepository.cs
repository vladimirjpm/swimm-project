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
}
