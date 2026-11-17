using Azure;
using Azure.Identity;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.Infrastructure.Services.AppInsights;

/// <summary>
/// Сбой чтения → карточка подключения (§4.1 плана): «нет входа az» или «не удалось прочитать» с
/// текстом. Страница не падает ни от какого исключения Azure.
/// </summary>
internal static class AppInsightsErrorClassifier
{
    /// <summary>
    /// Коды ошибок Log Analytics «нет доступа»: у пакетного запроса HTTP-статуса нет (0), причина — в
    /// <see cref="RequestFailedException.ErrorCode"/>. Живая проверка 17.11: чужой/несуществующий
    /// WorkspaceId даёт <c>InvalidTokenError</c>.
    /// </summary>
    private static readonly HashSet<string> AccessCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "InvalidTokenError", "AuthorizationFailed", "InsufficientAccessError", "Forbidden", "PathNotFoundError", "WorkspaceNotFoundError",
    };

    public static AppInsightsConnection Classify(Exception ex, TimeSpan timeout) => ex switch
    {
        // az не установлен, не выполнен az login, или вход просрочен (AADSTS… «run az login»).
        CredentialUnavailableException or AuthenticationFailedException => AppInsightsConnections.NoLogin(Reason(ex.Message)),

        RequestFailedException rf when rf.Status is 401 or 403 or 404 || (rf.ErrorCode is { } code && AccessCodes.Contains(code))
            => AppInsightsConnections.Failed(
                $"Нет доступа к рабочей области ({Status(rf)}): проверь AppInsights:WorkspaceId — нужен customerId "
                + "swimhub-logs; у входа az должна быть роль чтения на неё, а текущая подписка — та же (az account show)."),
        RequestFailedException { Status: 429 } => AppInsightsConnections.Failed(
            "Log Analytics ограничил частоту запросов (429) — повтори через минуту."),
        RequestFailedException rf => AppInsightsConnections.Failed($"Log Analytics ответил ошибкой ({Status(rf)}): {Reason(rf.Message)}"),

        OperationCanceledException or TimeoutException => AppInsightsConnections.Failed(
            $"Log Analytics не ответил за {timeout.TotalSeconds:0} с — повтори позже."),

        _ when IsUnreadableResponse(ex) => AppInsightsConnections.Failed(
            "Log Analytics вернул ответ, который не удалось разобрать (повтор тоже не помог) — обнови через минуту. "
            + $"Подробности — в журнале API ({ex.GetType().Name})."),

        _ => AppInsightsConnections.Failed($"{ex.GetType().Name}: {Reason(ex.Message)}"),
    };

    /// <summary>
    /// SDK не смог разобрать ответ (живьём 17.11: <c>InvalidOperationException</c> «requires an element of
    /// type 'Object', but the target element has type 'String'» — плавающий, не воспроизвёлся). Это не
    /// «нет входа» и не «нет доступа»: такой сбой стоит один раз повторить.
    /// </summary>
    internal static bool IsUnreadableResponse(Exception ex) =>
        ex is InvalidOperationException or System.Text.Json.JsonException or FormatException
        && ex is not ObjectDisposedException;

    private static string Status(Exception ex) => ex is RequestFailedException rf
        ? string.Join(", ", new[] { rf.Status > 0 ? $"HTTP {rf.Status}" : null, rf.ErrorCode }.Where(s => !string.IsNullOrEmpty(s)))
          is { Length: > 0 } s ? s : "без кода"
        : "без кода";

    /// <summary>
    /// Сообщения Azure многострочные: заголовок пакета («Batch query with id '0' failed.»), строка
    /// «Код: причина», потом заголовки и сырой JSON (в нём id каталогов — не показываем). Берём первую
    /// содержательную строку.
    /// </summary>
    internal static string Reason(string message) =>
        message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => !line.StartsWith("Batch query with id", StringComparison.OrdinalIgnoreCase)) ?? "";
}
