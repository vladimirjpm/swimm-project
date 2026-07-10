using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Порт админского CRUD групп (Admin/HubGroups) — сущность, участники, привязка к клубу.
/// Публичная страница группы, публичный API и мапинг в SwimmReadDbContext — вне скоупа (фаза 3).
/// </summary>
public interface IHubGroupAdminService
{
    Task<IReadOnlyList<HubGroupAdminRowDto>> GetAllAsync();

    /// <summary>Данные для формы Edit (включая участников). null — не найдено.</summary>
    Task<HubGroupEditDto?> GetByIdAsync(int id);

    /// <summary>Справочник клубов для select в форме.</summary>
    Task<IReadOnlyList<ClubOptionDto>> GetClubOptionsAsync();

    /// <summary>Создать группу. ownerUserId — текущий залогиненный админ.</summary>
    Task<HubGroupSaveResult> CreateAsync(HubGroupInputDto input, int ownerUserId);

    /// <summary>Обновить. Ошибка — при пустом/занятом Slug или пустом Name.</summary>
    Task<HubGroupSaveResult> UpdateAsync(int id, HubGroupInputDto input);

    /// <summary>Удалить группу (участники удаляются каскадно на уровне БД).</summary>
    Task<HubGroupSaveResult> DeleteAsync(int id);

    /// <summary>Поиск пловцов по подстроке в фамилии/имени (RU/EN), top-20 — для добавления участника.</summary>
    Task<IReadOnlyList<SwimmerSearchResultDto>> SearchSwimmersAsync(string query);

    /// <summary>Добавить участника. Отказ — если пловец уже состоит в группе (unique-пара).</summary>
    Task<HubGroupMemberSaveResult> AddMemberAsync(int hubGroupId, int swimmerId, string role);

    /// <summary>Изменить роль/порядок участника. Отказ, если участник не принадлежит hubGroupId.</summary>
    Task<HubGroupMemberSaveResult> UpdateMemberAsync(int hubGroupId, int memberId, string role, int sortOrder);

    /// <summary>Удалить участника из группы. Отказ, если участник не принадлежит hubGroupId.</summary>
    Task<HubGroupMemberSaveResult> RemoveMemberAsync(int hubGroupId, int memberId);
}
