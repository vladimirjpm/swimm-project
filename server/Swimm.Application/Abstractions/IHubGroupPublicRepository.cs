using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Публичный read-путь групп (фазы 3–4): список, страница группы с агрегатами
/// (участники, последние заплывы, рекорды группы) и виртуальная группа
/// «Моё избранное» поверх избранного пользователя.
///
/// Видимость — настройка HubGroupVisibility (public | private | perGroup, правило —
/// <c>HubGroupVisibilityRules</c>). Приватная группа отсюда отдаётся ЦЕЛИКОМ: кому её показывать,
/// решает вызывающий по <see cref="GetAccessAsync"/> (участнику — страница, остальным —
/// <see cref="GetMembersOnlyStubAsync"/>). Каталог приватные не показывает никому.
/// </summary>
public interface IHubGroupPublicRepository
{
    /// <summary>Группы для каталога: без приватных и без копий клуба с официальной группой.</summary>
    Task<IReadOnlyList<HubGroupListItemDto>> GetGroupsAsync();

    /// <summary>Страница группы по slug (и приватной тоже — см. <see cref="GetAccessAsync"/>). null — нет.</summary>
    Task<HubGroupDetailsDto?> GetBySlugAsync(string slug);

    /// <summary>
    /// Может ли зритель смотреть группу: публичную — любой; приватную — активный участник-аккаунт,
    /// владелец, админ группы, админ сайта. Заявка (pending) не в счёт. null — группы нет.
    /// </summary>
    Task<HubGroupAccessDto?> GetAccessAsync(string slug, int? userId, bool isSiteAdmin);

    /// <summary>
    /// Заглушка приватной группы для не-участника: имя, иконка, как вступить (всегда заявкой) —
    /// ни состава, ни результатов, ни медиа. null — группы нет.
    /// </summary>
    Task<HubGroupDetailsDto?> GetMembersOnlyStubAsync(string slug);

    /// <summary>
    /// Виртуальная группа «Моё избранное»: участники = пловцы из избранного пользователя,
    /// те же агрегаты. Настройка видимости НЕ применяется — это личные данные пользователя.
    /// </summary>
    Task<HubGroupDetailsDto> GetFavoritesGroupAsync(int userId);

    /// <summary>
    /// SwimmerId ростера группы (HubGroupMembers) для вкладки «Competitions» (публичный
    /// эндпоинт результатов с фильтрами). null — группы нет. Доступ к приватной решает вызывающий.
    /// </summary>
    Task<List<int>?> GetRosterSwimmerIdsAsync(string slug);
}
