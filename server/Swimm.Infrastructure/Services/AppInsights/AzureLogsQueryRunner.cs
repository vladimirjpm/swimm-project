using Azure.Identity;
using Azure.Monitor.Query.Logs;
using Azure.Monitor.Query.Logs.Models;

namespace Swimm.Infrastructure.Services.AppInsights;

/// <summary>
/// Запросы в Log Analytics через <c>Azure.Monitor.Query.Logs</c> под входом Azure CLI
/// (<see cref="AzureCliCredential"/>): ни ключей, ни секретов — админка живёт только на машине Влада (В17).
/// Все запросы страницы — одним пакетом (<c>QueryBatch</c>): один HTTP-вызов на ответ.
/// Клиент один на процесс — токен az кэшируется внутри него, <c>az account get-access-token</c>
/// не запускается на каждый запрос.
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

    public async Task<IReadOnlyDictionary<string, AppInsightsRows>> RunAsync(
        string workspaceId, IReadOnlyList<AppInsightsQuery> queries, CancellationToken ct)
    {
        var batch = new LogsBatchQuery();
        var range = new LogsQueryTimeRange(AppInsightsQueries.QueryWindow);
        var ids = queries
            .Select(q => (q.Name, Id: batch.AddWorkspaceQuery(workspaceId, q.Kql, range,
                new LogsQueryOptions { ServerTimeout = ServerTimeout })))
            .ToList();

        var response = await _client.QueryBatchAsync(batch, ct);
        var results = new Dictionary<string, AppInsightsRows>(ids.Count);
        foreach (var (name, id) in ids)
        {
            // GetResult бросает RequestFailedException, если этот запрос пакета упал (синтаксис, права).
            var result = response.Value.GetResult(id);
            results[name] = new AppInsightsRows(ToRows(result.Table));
        }
        return results;
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
