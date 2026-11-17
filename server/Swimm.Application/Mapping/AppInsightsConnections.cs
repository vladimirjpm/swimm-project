using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Тексты карточки подключения (§4.1 плана): каждое состояние — понятной карточкой, а не ошибкой.
/// </summary>
public static class AppInsightsConnections
{
    public const string LoginCommand = "az login";

    public static AppInsightsConnection Ok() =>
        new(AppInsightsStates.Ok, "Подключено", "Данные читаются из рабочей области Log Analytics под входом az.", null);

    /// <param name="missingKeys">Каких ключей не хватает (<c>AppInsights:WorkspaceId</c>, <c>AppInsights:ResourceId</c>).</param>
    public static AppInsightsConnection NotConfigured(IReadOnlyList<string> missingKeys) =>
        new(AppInsightsStates.NotConfigured, "Не настроено",
            $"Нет {(missingKeys.Count == 1 ? "ключа" : "ключей")} {string.Join(", ", missingKeys)}. Впиши значения в "
            + "appsettings.Local.json (Development) или appsettings.ProdAdmin.Local.json (ProdAdmin) — эти файлы "
            + "в git не попадают — и перезапусти API.",
            "az monitor log-analytics workspace show -g swimm-rg -n swimhub-logs --query customerId -o tsv\n"
            + "az resource show -g swimm-rg -n swimhub-ai --resource-type microsoft.insights/components --query id -o tsv");

    /// <param name="detail">Что ответил <c>az</c> — коротко.</param>
    public static AppInsightsConnection NoLogin(string? detail) =>
        new(AppInsightsStates.NoLogin, "Нет входа az",
            "Чтение идёт под входом Azure CLI на этой машине, а он не сделан или просрочен. Выполни в терминале "
            + "команду ниже и обнови страницу." + Detail(detail),
            LoginCommand);

    /// <param name="detail">Причина: таймаут, нет прав, неверный id, ответ Azure.</param>
    public static AppInsightsConnection Failed(string detail) =>
        new(AppInsightsStates.Failed, "Не удалось прочитать", Clip(detail, 400), null);

    private static string Detail(string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? "" : $" Ответ az: {Clip(detail.Trim(), 300)}";

    internal static string Clip(string text, int max) => text.Length > max ? text[..max] + "…" : text;
}
