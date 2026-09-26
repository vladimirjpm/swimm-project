using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Порядок состава на странице группы (таб Swimmers и дайджест Overview): **тренер первым, за
/// ним админы группы, потом остальные** — каждый слой в своём ручном порядке (SortOrder).
/// Решение Влада 26.09.2026.
///
/// «Тренер» — пловец состава с ролью <c>coach</c>. «Админ» — владелец группы или
/// <c>HubGroupAdmin</c>, чей аккаунт привязан к пловцу этого состава через
/// <c>AppUser.SwimmerId</c>. Привязку ставит админ сайта (/Admin/Users), поэтому ей можно
/// верить; primary favorite («Me») для этого НЕ годится — его ставит кто угодно себе сам
/// (правило «primary favorite недоверенный»). Нет привязки — админ стоит на своём месте.
/// </summary>
public static class HubGroupRosterOrder
{
    public const string CoachRole = "coach";

    /// <summary>
    /// Состав в порядке «тренер → админы → остальные» и с пометкой <c>IsAdmin</c> (чип
    /// «admin»). Сортировка устойчивая: внутри слоя сохраняется входной порядок (он уже по
    /// SortOrder). Тренер-админ — в слое тренеров, но чип admin получает тоже.
    /// </summary>
    public static List<HubGroupPublicMemberDto> Apply(
        IEnumerable<HubGroupPublicMemberDto> members, IReadOnlySet<int> adminSwimmerIds)
    {
        var list = members.ToList();
        foreach (var m in list) m.IsAdmin = adminSwimmerIds.Contains(m.SwimmerId);
        return list
            .OrderBy(m => m.Role == CoachRole ? 0 : m.IsAdmin ? 1 : 2)
            .ToList();
    }
}
