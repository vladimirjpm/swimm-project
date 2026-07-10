using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Медиа группы (только ссылки, HubGroupMedia): публичная галерея (TrainingId == null) +
/// CRUD-мутации. Авторизацию мутаций (владелец/админ группы/site-админ) решает контроллер
/// через <see cref="IHubGroupPermissionService"/>, сервис данные приватности не проверяет.
/// </summary>
public interface IHubGroupMediaService
{
    /// <summary>Публичная галерея группы (TrainingId == null), в порядке добавления.</summary>
    Task<List<HubGroupMediaDto>> GetGalleryAsync(int hubGroupId);

    /// <summary>
    /// Добавить медиа. Валидирует media_type/source_type/album-инвариант/https-url,
    /// а если задан training_id — что тренировка принадлежит этой же группе.
    /// </summary>
    Task<(bool Success, string? Error, int Id)> AddAsync(int hubGroupId, HubGroupMediaInputDto input, int createdByUserId);

    /// <summary>Удалить медиа. false — если записи нет или она принадлежит другой группе.</summary>
    Task<bool> DeleteAsync(int hubGroupId, int mediaId);
}
