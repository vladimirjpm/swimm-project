using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Медиа группы (только ссылки, HubGroupMedia): публичная галерея (TrainingId == null) +
/// CRUD-мутации. Авторизацию мутаций (владелец/админ группы/site-админ) решает контроллер
/// через <see cref="IHubGroupPermissionService"/>, сервис данные приватности не проверяет.
/// </summary>
public interface IHubGroupMediaService
{
    /// <summary>Публичная галерея группы (TrainingId == null, Visibility=public), в порядке добавления.</summary>
    Task<List<HubGroupMediaDto>> GetGalleryAsync(int hubGroupId);

    /// <summary>
    /// Members-слой (2B′): тренерские разборы (Visibility=members, вне тренировок) с контекстом
    /// якоря (пловец/заплыв). Авторизацию (активный член группы / админ) решает контроллер.
    /// </summary>
    Task<List<HubGroupMemberMediaDto>> GetMembersMediaAsync(int hubGroupId);

    /// <summary>
    /// Добавить медиа. Валидирует media_type/source_type/album-инвариант/https-url,
    /// training_id — принадлежность тренировки группе; для visibility=members — что группа
    /// официальная и якоря (swimmer_id/result_id) корректны; якорь при public запрещён.
    /// </summary>
    Task<(bool Success, string? Error, int Id)> AddAsync(int hubGroupId, HubGroupMediaInputDto input, int createdByUserId);

    /// <summary>Удалить медиа. false — если записи нет или она принадлежит другой группе.</summary>
    Task<bool> DeleteAsync(int hubGroupId, int mediaId);
}
