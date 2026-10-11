using Swimm.Application.Abstractions;

namespace Swimm.Application.Mapping;

/// <summary>
/// Потолки групп (этап Ш3.0, решение Влада 28.09.2026, docs/plans/entity-hero-roles-plan.md §5):
/// аккаунты дешёвые, поэтому у каждой самообслуживаемой вставки есть потолок, а у мутаций групп —
/// rate limit (политика <see cref="RateLimitPolicy"/> в Program.cs). Числа — настройки
/// /Admin/Settings (живут в памяти, после рестарта — дефолты ниже). Тексты отказов видит
/// пользователь — поэтому по-английски, одно место на все пути вставки.
///
/// Потолок — свойство группы или аккаунта, а не того, кто действует: site-админ упирается в него
/// так же и поднимает настройку, если нужно. Проверка «посчитал → вставил» без блокировки: при
/// гонке параллельных запросов потолок можно превысить на единицы — это не нарушает смысла
/// (защита от сотен, не от одного лишнего), а параллельность и так режет rate limit.
/// </summary>
public static class HubGroupQuotaRules
{
    /// <summary>Сколько аккаунтов (active + pending) может быть в одной группе.</summary>
    public const string MaxAccountMembersKey = "HubGroupMaxAccountMembers";
    public const int DefaultMaxAccountMembers = 150;

    /// <summary>
    /// Сколько пловцов владелец может добавить в состав РУКАМИ. Клубные строки (подписка на
    /// клуб) не в счёт: подписка одна на группу и ограничена размером клуба.
    /// </summary>
    public const string MaxManualSwimmersKey = "HubGroupMaxManualSwimmers";
    public const int DefaultMaxManualSwimmers = 200;

    /// <summary>В скольких группах аккаунт может состоять (active + pending), владение не в счёт.</summary>
    public const string MaxMembershipsPerUserKey = "HubGroupMaxMembershipsPerUser";
    public const int DefaultMaxMembershipsPerUser = 20;

    /// <summary>Сколько заявок на вступление (pending) аккаунт может держать одновременно.</summary>
    public const string MaxPendingPerUserKey = "HubGroupMaxPendingPerUser";
    public const int DefaultMaxPendingPerUser = 5;

    /// <summary>Рубильник самозаписи в группы: false — «Join» закрыт всем (волна спама).</summary>
    public const string SelfJoinEnabledKey = "HubGroupSelfJoinEnabled";

    public const int MinLimit = 1;
    public const int MaxLimit = 5000;

    /// <summary>Имя политики rate limit на мутации групп (Program.cs).</summary>
    public const string RateLimitPolicy = "hubgroups";

    /// <summary>Мутаций групп в минуту на пользователя: тренер, правящий уровни 40 пловцам подряд, не упирается.</summary>
    public const int RateLimitPerMinute = 60;

    public static readonly string[] LimitKeys =
        [MaxAccountMembersKey, MaxManualSwimmersKey, MaxMembershipsPerUserKey, MaxPendingPerUserKey];

    public static bool IsValidLimit(int value) => value is >= MinLimit and <= MaxLimit;

    public static int Limit(ISettingsService settings, string key) =>
        Math.Clamp(settings.GetValue(key, DefaultFor(key)), MinLimit, MaxLimit);

    public static bool SelfJoinEnabled(ISettingsService settings) =>
        settings.GetValue(SelfJoinEnabledKey, true);

    public static int DefaultFor(string key) => key switch
    {
        MaxAccountMembersKey => DefaultMaxAccountMembers,
        MaxManualSwimmersKey => DefaultMaxManualSwimmers,
        MaxMembershipsPerUserKey => DefaultMaxMembershipsPerUser,
        MaxPendingPerUserKey => DefaultMaxPendingPerUser,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown hub group quota key")
    };

    // ── Тексты отказов (видит пользователь) ─────────────────────────────────

    public const string SelfJoinClosedError = "Joining groups is temporarily closed. Please try again later.";

    public static string GroupFullError(int limit) =>
        $"This group is full ({limit} members). Ask the group admin.";

    public static string TooManyMembershipsError(int limit) =>
        $"You can be a member of up to {limit} groups. Leave a group to join another.";

    public static string UserTooManyMembershipsError(int limit) =>
        $"This user is already a member of {limit} groups — the maximum.";

    public static string TooManyPendingError(int limit) =>
        $"You have {limit} join requests waiting. Wait for an answer or cancel one.";

    public static string RosterFullError(int limit) =>
        $"A group can have up to {limit} swimmers added by hand. Remove someone first.";

    public const string RateLimitedError = "Too many requests — wait a minute and try again.";
}
