using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Кто и сколько групп может создать. Единственное место, где настройки /Admin/Settings и
/// персональное исключение пользователя складываются в ответ: его читают и проверка при
/// создании (<c>HubGroupUserService</c>), и колонка «Группы» в /Admin/Users. Вторая копия
/// разбора разъехалась бы с первой, и админка показывала бы не тот лимит, что действует.
///
/// Порядок (решение Влада 10.09.2026, docs/hubgroups-architecture.md §3а):
///  1. site-админ — без лимита и мимо политики;
///  2. политика <see cref="PolicyKey"/> решает, КТО может создавать вообще (admin/coach/any);
///  3. лимит решает, СКОЛЬКО: персональный, если задан, иначе по роли (Coach →
///     <see cref="MaxPerCoachKey"/>, остальные → <see cref="MaxPerUserKey"/>).
/// Считаются группы, которыми пользователь ВЛАДЕЕТ, официальные тоже; админство в чужой
/// группе в лимит не идёт.
///
/// Персональный лимит политику НЕ обходит: политика — общий рубильник (например, закрыть
/// создание на время волны спама), и исключение по одному человеку его не пробивает. Пустить
/// конкретного не-тренера при политике coach — это выдать ему роль, а не лимит.
/// </summary>
public static class HubGroupCreationRules
{
    public const string PolicyKey = "HubGroupCreationPolicy";
    public const string MaxPerUserKey = "HubGroupMaxPerUser";
    public const string MaxPerCoachKey = "HubGroupMaxPerCoach";

    public const string PolicyAdmin = "admin";
    public const string PolicyCoach = "coach";
    public const string PolicyAny = "any";

    /// <summary>
    /// Дефолт политики — «создавать может любой вошедший, в пределах лимита». Он же рабочий
    /// режим: настройки живут в памяти и после рестарта возвращаются к дефолту.
    /// </summary>
    public const string DefaultPolicy = PolicyAny;

    /// <summary>Дефолт лимита по роли (и для пользователя, и для тренера).</summary>
    public const int DefaultLimit = 3;

    /// <summary>Потолок любого лимита — от опечатки «300» вместо «3».</summary>
    public const int MaxLimit = 100;

    /// <summary>Действующий лимит. null — без лимита (site-админ).</summary>
    public static int? EffectiveLimit(ISettingsService settings, bool isAdmin, bool isCoach, int? personalLimit)
    {
        if (isAdmin) return null;
        if (personalLimit is int personal) return Math.Clamp(personal, 0, MaxLimit);

        var roleLimit = isCoach
            ? settings.GetValue(MaxPerCoachKey, DefaultLimit)
            : settings.GetValue(MaxPerUserKey, DefaultLimit);
        return Math.Clamp(roleLimit, 0, MaxLimit);
    }

    /// <summary>
    /// Может ли пользователь создать ещё одну группу. <paramref name="owned"/> — сколько групп
    /// он уже ВЛАДЕЕТ. Тексты причин идут на витрину — поэтому по-английски (правило UI).
    /// </summary>
    public static HubGroupCreateEligibilityDto Evaluate(
        ISettingsService settings, bool isAdmin, bool isCoach, int? personalLimit, int owned)
    {
        var limit = EffectiveLimit(settings, isAdmin, isCoach, personalLimit);
        if (limit == null)
            return new HubGroupCreateEligibilityDto { CanCreate = true, Owned = owned };

        var result = new HubGroupCreateEligibilityDto { Owned = owned, Limit = limit, Remaining = 0 };

        var policy = settings.GetValue(PolicyKey, DefaultPolicy);
        if (policy == PolicyAdmin)
        {
            result.Reason = "Creating groups is currently limited to site admins.";
            return result;
        }
        if (policy == PolicyCoach && !isCoach)
        {
            result.Reason = "Creating groups is currently limited to coaches.";
            return result;
        }

        if (limit == 0)
        {
            result.Reason = "Creating groups is not available for your account.";
            return result;
        }

        // Лимит могли снизить ниже уже созданного: старые группы остаются, новые — нет.
        var remaining = Math.Max(0, limit.Value - owned);
        if (remaining == 0)
        {
            var noun = limit == 1 ? "group" : "groups";
            result.Reason = $"You have reached your limit of {limit} {noun}. Ask the site admin if you need more.";
            return result;
        }

        result.CanCreate = true;
        result.Remaining = remaining;
        return result;
    }
}
