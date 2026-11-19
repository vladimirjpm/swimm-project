namespace Swimm.Infrastructure.Services.AppInsights;

/// <summary>Строки одной таблицы результата: колонка → значение (long, double, string, bool, DateTimeOffset или null).</summary>
internal sealed record AppInsightsRows(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);

/// <summary>
/// Шов между чтением и Azure SDK: настоящая реализация — <see cref="AzureLogsQueryRunner"/>
/// (Log Analytics под входом az), в тестах — подделка. Ошибки Azure не глотает: их разбирает
/// <see cref="AppInsightsErrorClassifier"/> в <see cref="AppInsightsReader"/>.
/// </summary>
internal interface IAppInsightsQueryRunner
{
    /// <summary>Выполнить запросы (параллельно, по одному); ключ ответа — <see cref="AppInsightsQuery.Name"/>.</summary>
    Task<IReadOnlyDictionary<string, AppInsightsRows>> RunAsync(
        string workspaceId, IReadOnlyList<AppInsightsQuery> queries, CancellationToken ct);
}
