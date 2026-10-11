namespace Swimm.Application.Mapping;

/// <summary>
/// Тестовые аккаунты и тестовые группы (docs/plans/test-personas-plan.md, решения 24.09.2026).
///
/// Тестовый аккаунт — email с префиксом <see cref="Prefix"/> В ЗОНЕ
/// <see cref="TestPersonas.EmailDomain"/> (<c>utest-member@swimm.test</c>). Нужны оба признака:
/// по одному префиксу настоящий <c>utest.someone@gmail.com</c> в проде видел бы тестовые группы.
/// Адрес в зоне <c>.test</c> снаружи получить нельзя: Google такой не выдаст, а локальный вход
/// требует подтверждённого email, письмо на <c>.test</c> не доходит (RFC 2606).
/// Отдельной роли «тестер» нет, тестер работает через переключатель персонажей. Тестовую группу
/// (<c>HubGroup.IsTest</c>) видят site-админ и тестовые аккаунты; для остальных её нет.
/// </summary>
public static class TestAccountRules
{
    /// <summary>Префикс email тестового аккаунта. Сравнение без учёта регистра.</summary>
    public const string Prefix = "utest";

    private static readonly string DomainSuffix = "@" + TestPersonas.EmailDomain;

    public static bool IsTestEmail(string? email) =>
        email != null
        && email.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
        && email.EndsWith(DomainSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Может ли зритель видеть тестовые группы.</summary>
    public static bool CanSeeTestGroups(bool isSiteAdmin, string? email) =>
        isSiteAdmin || IsTestEmail(email);
}
