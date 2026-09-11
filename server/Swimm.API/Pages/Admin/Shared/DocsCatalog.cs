namespace Swimm.API.Pages.Admin.Shared;

/// <summary>Документ раздела Docs: путь от корня репозитория (прямые слэши), заголовок, о чём он.</summary>
public sealed record DocEntry(string Path, string Title, string About);

/// <summary>Подкатегория раздела Docs — пункт сайдбара <c>/Admin/Docs/{Key}</c>.</summary>
public sealed record DocSection(string Key, string Title, string Subtitle, IReadOnlyList<DocEntry> Docs);

/// <summary>
/// Раздел админки «Документация» (решение Влада 11.09.2026): какие MD из репозитория
/// показывать и в какой подкатегории. Добавить документ = строка здесь — как ApiCatalog
/// для /Admin/Api. Сами тексты живут в репозитории и правятся там; админка их только
/// показывает, копий нет — поэтому и устареть отдельно от репозитория им нечему.
/// </summary>
public static class DocsCatalog
{
    public static readonly IReadOnlyList<DocSection> Sections =
    [
        new("important", "Самое важное", "выжимка правил и решений, которые нельзя потерять",
        [
            new("docs/important.md", "Самое важное", "одна страница: имена, медали, сезон, качество данных, кэш, как работаем"),
            new("README.md", "Что это за проект", "обзор и быстрый старт"),
            new("docs/ROADMAP.md", "Роадмап", "фазы, этапы и их статусы"),
            new("docs/plans/README.md", "Рабочие планы", "активные планы и открытые решения, ждущие ответа"),
        ]),
        new("rules", "Правила", "как устроена работа с кодом и что обязано держаться",
        [
            new("CLAUDE.md", "Правила репозитория", "сборка, база, миграции, auth, грабли — главный гайд"),
            new("docs/pre-push-rules.md", "Перед push", "«поменял X → обнови Y»"),
            new("client/CLAUDE.md", "Фронтенд", "стек, маршруты, компоненты, темы, время заплыва"),
            new("server/Swimm.Application/CLAUDE.md", "Граница Clean Architecture", "что можно слою Application"),
            new(".github/copilot-instructions.md", "Архитектура и конвенции кода", "где какой код и как называть"),
            new("docs/season-boundary-rule.md", "Сезон", "календарный и витринный сезон"),
            new("docs/ui-components.md", "Реестр UI-компонентов", "что уже есть — перед тем, как верстать своё"),
            new("docs/admin-pages/README.md", "Карта админки", "одна справка на страницу"),
        ]),
        new("decisions", "Решения", "что решено, почему, и журналы решений",
        [
            new("docs/ARCHITECTURE.md", "Архитектура", "принципиальные решения, швы, кэш"),
            new("docs/data-integrity.md", "Целостность данных", "инварианты, журнал решений и инцидентов"),
            new("docs/competition-overview-cards.md", "Места, медали, очки", "два набора мест, что считается медалью"),
            new("docs/relays.md", "Эстафеты", "модель RelayMembers, журнал фиксов"),
            new("docs/media-page.md", "Медиа", "My media, публикации, видимость"),
            new("docs/plans/cache-tags-plan.md", "Кэш", "сброс по меткам, готовность к Redis"),
            new("docs/plans/club-subscribers-plan.md", "Клуб — не группа", "почему у клуба нет подписчиков"),
        ]),
    ];

    /// <summary>
    /// Файлы вне <c>docs/</c>, которые можно открыть по ссылке из документа. Остальное вне
    /// <c>docs/</c> админка не показывает: страница читает файлы с диска, и белый список —
    /// граница того, что она вообще может прочитать.
    /// </summary>
    private static readonly HashSet<string> ExtraAllowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "README.md",
        "CLAUDE.md",
        "AGENTS.md",
        "client/CLAUDE.md",
        "server/Swimm.Application/CLAUDE.md",
        ".github/copilot-instructions.md",
    };

    /// <summary>Можно ли показать файл: MD под <c>docs/</c> или из белого списка, без выхода вверх.</summary>
    public static bool IsAllowed(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        && !path.Contains("..")
        && !path.StartsWith('/')
        && !path.Contains('\\')
        && (path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase) || ExtraAllowed.Contains(path));
}
