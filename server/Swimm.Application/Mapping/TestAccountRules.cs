namespace Swimm.Application.Mapping;

/// <summary>
/// Тестовые аккаунты и тестовые группы (docs/plans/test-personas-plan.md, решения 24.09.2026).
///
/// Тестовый аккаунт — email с префиксом <see cref="Prefix"/> (<c>utest-member@swimm.test</c>).
/// Префикс — единственный признак: отдельной роли «тестер» нет, тестер работает через
/// переключатель персонажей. Тестовую группу (<c>HubGroup.IsTest</c>) видят site-админ и
/// тестовые аккаунты; для остальных её нет.
/// </summary>
public static class TestAccountRules
{
    /// <summary>Префикс email тестового аккаунта. Сравнение без учёта регистра.</summary>
    public const string Prefix = "utest";

    public static bool IsTestEmail(string? email) =>
        email != null && email.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Может ли зритель видеть тестовые группы.</summary>
    public static bool CanSeeTestGroups(bool isSiteAdmin, string? email) =>
        isSiteAdmin || IsTestEmail(email);
}
