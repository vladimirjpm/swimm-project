using Swimm.Application.Abstractions;

namespace Swimm.Application.Mapping;

/// <summary>
/// Сколько пловцов и клубов можно держать в избранном (решение Влада 10.09.2026,
/// docs/plans/hubgroup-club-subscription-plan.md §1-5). Единственное место, где настройки
/// /Admin/Settings превращаются в лимит и текст отказа: его читают и проверка при добавлении
/// (<c>UserFavoriteRepository</c>), и <c>/api/client-config</c>, откуда клиент берёт лимит,
/// чтобы погасить сердечко ДО клика. Разъедутся копии — сердечко будет гаснуть не там, где
/// сервер отказывает.
///
/// Зачем лимит: если отмечать сердечком сотни пловцов, отметка перестаёт что-то выделять.
/// «Весь клуб одной страницей» — это группа, а не избранное; поэтому и избранный клуб в
/// пловцов не разворачивается (иначе один клуб = 160 сердечек).
///
/// Лимиты раздельные по типу. Звезда «это я» (primary) — тоже пловец в избранном и идёт в
/// счёт пловцов. Кто уже выше лимита (лимит снизили), ничего не теряет — только не может
/// добавить. Site-админ не исключение: смысл лимита не в правах, а в том, чтобы сердечко
/// оставалось отметкой.
/// </summary>
public static class FavoritesRules
{
    public const string MaxSwimmersKey = "FavoritesMaxSwimmers";
    public const string MaxClubsKey = "FavoritesMaxClubs";

    public const string TargetSwimmer = "swimmer";
    public const string TargetClub = "club";

    public const int DefaultMaxSwimmers = 30;
    public const int DefaultMaxClubs = 3;

    /// <summary>
    /// Диапазон любого лимита. Ноль сознательно нельзя: «избранное выключено» — это не лимит,
    /// а другая фича. Потолок — от опечатки «3000» вместо «30».
    /// </summary>
    public const int MinLimit = 1;
    public const int MaxLimit = 200;

    /// <summary>Машиночитаемый код отказа в ответе 422 — клиент узнаёт по нему лимит.</summary>
    public const string LimitErrorCode = "favorites_limit";

    public static bool IsValidLimit(int value) => value is >= MinLimit and <= MaxLimit;

    /// <summary>Действующий лимит для типа избранного (<see cref="TargetSwimmer"/> / <see cref="TargetClub"/>).</summary>
    public static int LimitFor(ISettingsService settings, string targetType)
    {
        var raw = targetType == TargetClub
            ? settings.GetValue(MaxClubsKey, DefaultMaxClubs)
            : settings.GetValue(MaxSwimmersKey, DefaultMaxSwimmers);
        return Math.Clamp(raw, MinLimit, MaxLimit);
    }

    /// <summary>
    /// Подсказка у погашенного сердечка и текст отказа. Идёт на витрину — поэтому
    /// по-английски (правило UI). Для пловцов подсказка называет выход: большой список — это группа.
    /// </summary>
    public static string FullHint(string targetType, int limit)
    {
        if (targetType == TargetClub)
            return limit == 1 ? "Up to 1 club." : $"Up to {limit} clubs.";

        var noun = limit == 1 ? "swimmer" : "swimmers";
        return $"Up to {limit} {noun} — use a group for bigger lists.";
    }
}
