using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Публичный read-путь групп (фазы 3–4): список, страница группы с агрегатами
/// (участники, последние заплывы, рекорды группы) и виртуальная группа
/// «Моё избранное» поверх избранного пользователя.
/// Видимость управляется настройкой HubGroupVisibility (public | private | perGroup).
/// </summary>
public interface IHubGroupPublicRepository
{
    /// <summary>Видимые группы для списка. При HubGroupVisibility=private — пусто.</summary>
    Task<IReadOnlyList<HubGroupListItemDto>> GetGroupsAsync();

    /// <summary>Страница группы по slug. null — нет или скрыта настройкой видимости.</summary>
    Task<HubGroupDetailsDto?> GetBySlugAsync(string slug);

    /// <summary>
    /// Виртуальная группа «Моё избранное»: участники = пловцы из избранного пользователя,
    /// те же агрегаты. Настройка видимости НЕ применяется — это личные данные пользователя.
    /// </summary>
    Task<HubGroupDetailsDto> GetFavoritesGroupAsync(int userId);
}
