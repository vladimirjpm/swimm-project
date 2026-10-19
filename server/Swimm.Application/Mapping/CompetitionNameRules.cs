namespace Swimm.Application.Mapping;

/// <summary>Правила по названию соревнования, общие для админки и витрины.</summary>
public static class CompetitionNameRules
{
    /// <summary>
    /// Чемпионат = чемпионат ИЗРАИЛЯ: «אליפות ישראל» (или английское Israel + Championship).
    /// Слова проверяются по отдельности, а не подстрокой «אליפות ישראל»: между ними бывает
    /// вставлен спонсор — «אליפות "ארנה" ישראל», «אליפות arena ישראל» (на реальных данных
    /// 2026-07-29 подстрока дала 39 из 42). Клубные/городские «אליפות מכבי», «אליפות חדרה»
    /// сюда НЕ попадают — это и есть смысл фильтра. Отборочные «מוקדמות אליפות … ישראל»
    /// попадают: они часть того же чемпионата.
    /// Отдельного признака в данных нет: у входящих с сайта, кроме имени и даты, ничего и нет,
    /// а категории (Kids/Youth/Masters…) — про возраст, не про ранг.
    /// Зовут его <c>CompetitionAdminRepository.IsChampionship</c> (админка, пакетный забор) и календарь главной.
    /// </summary>
    public static bool IsChampionship(string? name) =>
        !string.IsNullOrEmpty(name)
        && ((name.Contains("אליפות", StringComparison.Ordinal)
                && name.Contains("ישראל", StringComparison.Ordinal))
            || (name.Contains("championship", StringComparison.OrdinalIgnoreCase)
                && name.Contains("israel", StringComparison.OrdinalIgnoreCase)));
}
