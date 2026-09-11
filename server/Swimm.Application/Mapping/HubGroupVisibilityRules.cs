using Swimm.Application.Abstractions;

namespace Swimm.Application.Mapping;

/// <summary>
/// Приватная группа (решение Влада 11.09.2026, §6-6 плана docs/plans/hubgroup-club-subscription-plan.md):
/// её видят участники и управляющие, остальным — страница «только для участников», а не 404.
/// Раньше «приватная» отдавала 404 всем, включая владельца, — режимом нельзя было пользоваться.
///
/// Одно место на вопрос «приватна ли группа»: его задают каталог, страница группы, её
/// результаты, медиа и самозапись. Кто может СМОТРЕТЬ приватную — решает
/// <c>IHubGroupPublicRepository.GetAccessAsync</c> (там нужны таблицы членства).
/// </summary>
public static class HubGroupVisibilityRules
{
    public const string SettingKey = "HubGroupVisibility";

    /// <summary>Все группы открыты всем; флаг IsPublic у группы не действует.</summary>
    public const string Public = "public";

    /// <summary>Все группы — только для участников.</summary>
    public const string Private = "private";

    /// <summary>Решает флаг IsPublic у группы (галочка «Public group»).</summary>
    public const string PerGroup = "perGroup";

    /// <summary>
    /// Дефолт — perGroup: иначе галочка «Public group» в форме группы ни на что не влияла, и
    /// сделать группу приватной было нельзя. Настройки живут в памяти, дефолт = рабочий режим.
    /// </summary>
    public const string Default = PerGroup;

    public static string Current(ISettingsService settings) => settings.GetValue(SettingKey, Default);

    /// <summary>Группа только для участников: в каталоге её нет, не-участнику — заглушка.</summary>
    public static bool IsPrivate(string visibility, bool isPublic) => visibility switch
    {
        Private => true,
        PerGroup => !isPublic,
        _ => false,
    };
}
