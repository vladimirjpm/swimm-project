using Azure.Identity;
using Azure.Monitor.Query.Logs;
using Azure.Monitor.Query.Logs.Models;

namespace Swimm.Infrastructure.Services.AppInsights;

/// <summary>
/// Запросы в Log Analytics через <c>Azure.Monitor.Query.Logs</c> под входом Azure CLI
/// (<see cref="AzureCliCredential"/>): ни ключей, ни секретов — админка живёт только на машине Влада (В17).
/// Клиент один на процесс — токен az кэшируется внутри него, <c>az account get-access-token</c>
/// не запускается на каждый запрос.
///
/// ⚠ Запросы идут ПО ОДНОМУ и параллельно (<c>/query</c>), а не пакетом (<c>$batch</c>). Пакет был до
/// 19.11.2026 и плавающе ломался: Log Analytics иногда отдаёт элемент пакета с <c>body</c> строкой вместо
/// объекта, SDK падает в <c>LogsBatchQueryResult.DeserializeLogsBatchQueryResult</c>
/// (<c>InvalidOperationException</c> «requires an element of type 'Object', but … 'String'»), а повтор
/// того же пакета получает тот же ответ из кэша сервера — карточка App Insights пустела на минуту-две
/// (17.11 12:51, 19.11 07:50). Одиночный запрос такую ошибку отдаёт обычным HTTP-ответом с кодом —
/// SDK превращает её в <c>RequestFailedException</c>, и карточка показывает причину.
/// </summary>
internal sealed class AzureLogsQueryRunner : IAppInsightsQueryRunner
{
    /// <summary>Сервер Log Analytics прервёт запрос сам — не ждём дольше таймаута чтения (план Б1: 15 с).</summary>
    private static readonly TimeSpan ServerTimeout = TimeSpan.FromSeconds(15);

    private readonly LogsQueryClient _client;

    public AzureLogsQueryRunner()
    {
        // az на Windows отвечает 2–5 с; 12 с — чтобы успеть до общего таймаута чтения.
        var credential = new AzureCliCredential(new AzureCliCredentialOptions { ProcessTimeout = TimeSpan.FromSeconds(12) });
        var options = new LogsQueryClientOptions();
        options.Retry.MaxRetries = 1;
        options.Retry.NetworkTimeout = ServerTimeout;
        _client = new LogsQueryClient(credential, options);
    }

    public Task<IReadOnlyDictionary<string, AppInsightsRows>> RunAsync(
        string workspaceId, IReadOnlyList<AppInsightsQuery> queries, CancellationToken ct)
    {
        var range = new LogsQueryTimeRange(AppInsightsQueries.QueryWindow);
        var options = new LogsQueryOptions { ServerTimeout = ServerTimeout };
        return RunEachAsync(queries, async (q, token) =>
        {
            // Ошибка запроса (синтаксис, права, сбой сервера) — RequestFailedException; её разбирает читатель.
            var response = await _client.QueryWorkspaceAsync(workspaceId, q.Kql, range, options, token);
            return new AppInsightsRows(ToRows(response.Value.Table));
        }, ct);
    }

    /// <summary>
    /// Все запросы параллельно; ответ — по имени запроса. Первый сбой отменяет остальные и
    /// пробрасывается как есть (не <see cref="AggregateException"/>) — его классифицирует читатель.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<string, AppInsightsRows>> RunEachAsync(
        IReadOnlyList<AppInsightsQuery> queries,
        Func<AppInsightsQuery, CancellationToken, Task<AppInsightsRows>> runOne,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tasks = queries.Select(async q =>
        {
            try { return (q.Name, Rows: await runOne(q, cts.Token)); }
            catch { await cts.CancelAsync(); throw; }
        }).ToList();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            // Настоящая причина — первый сбой, не отмена, которую он вызвал у соседей.
            var first = tasks.Where(t => t.IsFaulted).Select(t => t.Exception!.InnerException!)
                .OrderBy(e => e is OperationCanceledException ? 1 : 0).FirstOrDefault();
            if (first is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(first);
            throw;
        }

        return tasks.ToDictionary(t => t.Result.Name, t => t.Result.Rows, StringComparer.Ordinal);
    }

    private static List<IReadOnlyDictionary<string, object?>> ToRows(LogsTable table)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            var dict = new Dictionary<string, object?>(table.Columns.Count, StringComparer.Ordinal);
            for (var i = 0; i < table.Columns.Count; i++)
                dict[table.Columns[i].Name] = row[i];
            rows.Add(dict);
        }
        return rows;
    }
}
