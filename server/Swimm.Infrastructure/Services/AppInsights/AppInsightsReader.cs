using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.Infrastructure.Services.AppInsights;

/// <summary>
/// <see cref="IAppInsightsReader"/>: телеметрия прод-сайта из Log Analytics для /Admin/AppInsights и
/// блока дашборда (docs/plans/application-insights-plan.md, Б1). Только чтение.
/// <list type="bullet">
/// <item>нет ключей — «не настроено», в Azure не ходим;</item>
/// <item>таймаут <see cref="Timeout"/>; любой сбой Azure — карточка состояния, не исключение;</item>
/// <item>удачный ответ кэшируется в памяти на <see cref="CacheFor"/>: запрос идёт 1–3 с и у Log Analytics
///   лимиты частоты. Сбой не кэшируется — после <c>az login</c> страница оживает сразу.</item>
/// </list>
/// Регистрируется только при включённой админке (<c>Admin:Enabled</c>) — на проде не исполняется.
/// </summary>
internal sealed class AppInsightsReader(
    IOptions<AppInsightsOptions> options,
    IAppInsightsQueryRunner runner,
    IMemoryCache cache,
    AppInsightsProcessInfo process,
    ILogger<AppInsightsReader> logger,
    TimeProvider? time = null) : IAppInsightsReader
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private const string CachePrefix = "appinsights:";

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private AppInsightsOptions Options => options.Value;

    public async Task<AppInsightsOverview> GetOverviewAsync(TimeSpan range, string role, bool refresh = false, CancellationToken ct = default)
    {
        var roleKey = AppInsightsRoles.Normalize(role)
                      ?? throw new ArgumentOutOfRangeException(nameof(role), role, "role — web или prodadmin");
        var rangeKey = AppInsightsRanges.Key(range);
        range = AppInsightsRanges.Parse(rangeKey)!.Value;
        var o = Options;
        var links = AppInsightsPortalLinks.Build(o.ResourceId);

        AppInsightsOverview Build(AppInsightsConnection connection, AppInsightsOverviewData? data) =>
            AppInsightsOverviewBuilder.Overview(connection, process, range, roleKey, data, links,
                o.DailyCapMb, o.CapResetHourUtc, Now());

        var key = $"{CachePrefix}overview:{rangeKey}:{roleKey}";
        if (!refresh && cache.TryGetValue(key, out AppInsightsOverview? cached) && cached is not null)
            return cached;

        var missing = o.MissingKeys();
        if (missing.Count > 0) return Build(AppInsightsConnections.NotConfigured(missing), null);

        var queries = AppInsightsQueries.Overview(AppInsightsRoles.RoleName(roleKey)!, range, o.CapResetHourUtc);
        var (rows, failure) = await RunAsync(o.WorkspaceId!, queries, ct);
        if (failure is not null) return Build(failure, null);

        var data = new AppInsightsOverviewData(
            AppInsightsRowMapper.Totals(rows![AppInsightsQueries.TotalsName]),
            AppInsightsRowMapper.Exceptions(rows[AppInsightsQueries.ExceptionsName]),
            AppInsightsRowMapper.Slow(rows[AppInsightsQueries.SlowName]),
            AppInsightsRowMapper.Errors(rows[AppInsightsQueries.ErrorsName]),
            AppInsightsRowMapper.Background(rows[AppInsightsQueries.BackgroundName]));
        var overview = Build(AppInsightsConnections.Ok(), data);
        cache.Set(key, overview, CacheFor);
        return overview;
    }

    public async Task<AppInsightsSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        const string key = CachePrefix + "summary";
        if (cache.TryGetValue(key, out AppInsightsSummary? cached) && cached is not null)
            return cached;

        var o = Options;
        var missing = o.MissingKeys();
        if (missing.Count > 0)
            return AppInsightsOverviewBuilder.Summary(AppInsightsConnections.NotConfigured(missing), null, o.DailyCapMb, Now());

        var (rows, failure) = await RunAsync(o.WorkspaceId!, AppInsightsQueries.Summary(o.CapResetHourUtc), ct);
        if (failure is not null)
            return AppInsightsOverviewBuilder.Summary(failure, null, o.DailyCapMb, Now());

        var summary = AppInsightsOverviewBuilder.Summary(AppInsightsConnections.Ok(),
            AppInsightsRowMapper.Totals(rows![AppInsightsQueries.TotalsName]), o.DailyCapMb, Now());
        cache.Set(key, summary, CacheFor);
        return summary;
    }

    /// <summary>Пакет запросов с таймаутом; сбой — карточка состояния. Отмена самим клиентом пробрасывается.</summary>
    private async Task<(IReadOnlyDictionary<string, AppInsightsRows>? Rows, AppInsightsConnection? Failure)> RunAsync(
        string workspaceId, IReadOnlyList<AppInsightsQuery> queries, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var rows = await runner.RunAsync(workspaceId.Trim(), queries, cts.Token);
                var lost = queries.Select(q => q.Name).Where(n => !rows.ContainsKey(n)).ToList();
                return lost.Count == 0
                    ? (rows, null)
                    : (null, AppInsightsConnections.Failed($"В ответе Log Analytics нет результатов: {string.Join(", ", lost)}."));
            }
            catch (Exception ex) when (attempt == 1 && !cts.IsCancellationRequested && AppInsightsErrorClassifier.IsUnreadableResponse(ex))
            {
                // Плавающий сбой 17.11 и 19.11: SDK не разобрал ответ $batch (body строкой) — с 19.11 пакет
                // не используем (AzureLogsQueryRunner), повтор оставлен на случай такого же ответа /query.
                // Один повтор в пределах того же таймаута; стек — в журнал (в исключении разбора данных
                // аккаунта нет).
                logger.LogWarning(ex, "App Insights: ответ Log Analytics не разобран, повторяю");
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                var failure = AppInsightsErrorClassifier.Classify(ex, Timeout);
                // Текст ответа Azure в журнал не пишем (в нём бывает имя учётной записи): только вид сбоя,
                // а у ответа с ошибкой — HTTP-статус и код Log Analytics (в них учётных данных нет).
                if (failure.State == AppInsightsStates.NoLogin)
                    logger.LogInformation("App Insights: нет входа az ({Type})", ex.GetType().Name);
                else if (ex is Azure.RequestFailedException rf)
                    logger.LogWarning("App Insights: чтение не удалось ({Type}, HTTP {Status}, {Code})",
                        ex.GetType().Name, rf.Status, rf.ErrorCode ?? "без кода");
                else
                    logger.LogWarning("App Insights: чтение не удалось ({Type})", ex.GetType().Name);
                return (null, failure);
            }
        }
    }

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;
}
