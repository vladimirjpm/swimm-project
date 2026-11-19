using System.Text.Json;
using Azure;
using Azure.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Services.AppInsights;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Чтение Application Insights (план Б1) — <see cref="AppInsightsReader"/> поверх подделки Azure:
/// три состояния из §4.1 (не настроено / нет входа / работает) и «не удалось прочитать», таймаут,
/// кэш 2 минуты (сбой не кэшируется), разбор строк KQL.
/// </summary>
public class AppInsightsReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset At { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => At;
    }

    private sealed class FakeRunner : IAppInsightsQueryRunner
    {
        public List<IReadOnlyList<AppInsightsQuery>> Calls { get; } = [];
        public Exception? Throw { get; set; }
        /// <summary>Бросать <see cref="Throw"/> только первые N вызовов (null — всегда).</summary>
        public int? ThrowTimes { get; set; }
        public TimeSpan Delay { get; set; }
        public Func<IReadOnlyList<AppInsightsQuery>, IReadOnlyDictionary<string, AppInsightsRows>>? Answer { get; set; }

        public async Task<IReadOnlyDictionary<string, AppInsightsRows>> RunAsync(
            string workspaceId, IReadOnlyList<AppInsightsQuery> queries, CancellationToken ct)
        {
            Calls.Add(queries);
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
            if (Throw is not null && (ThrowTimes is null || Calls.Count <= ThrowTimes)) throw Throw;
            return (Answer ?? DefaultAnswer)(queries);
        }
    }

    private static Dictionary<string, object?> TotalsRow(long requests = 66, long errors = 0) => new()
    {
        ["Requests"] = requests, ["Errors5xx"] = errors, ["P50Ms"] = 34.7, ["P95Ms"] = 637.3, ["SlowOperations"] = 2L,
        ["DbCalls"] = 497L, ["DbP95Ms"] = 70.4, ["DbFailed"] = 0L, ["Exceptions"] = 0L, ["BackgroundFailures"] = 0L,
        ["AdminFailures"] = 0L, ["LastTelemetryAt"] = Now.AddMinutes(-3), ["IngestedTodayMb"] = 0.1,
    };

    private static IReadOnlyDictionary<string, AppInsightsRows> DefaultAnswer(IReadOnlyList<AppInsightsQuery> queries) =>
        queries.ToDictionary(q => q.Name, q => new AppInsightsRows(q.Name == AppInsightsQueries.TotalsName
            ? [TotalsRow()]
            : q.Name == AppInsightsQueries.SlowName
                ? [new Dictionary<string, object?> { ["Operation"] = "GET api/clubs/{id:int}/overview", ["Count"] = 1L, ["P50Ms"] = 4492.6, ["P95Ms"] = 4492.6, ["Errors5xx"] = 0L }]
                : []));

    private static AppInsightsOptions Configured() => new() { WorkspaceId = "ws-test", ResourceId = "/subscriptions/sub-test/resourceGroups/rg/providers/microsoft.insights/components/ai" };

    private static (AppInsightsReader Reader, FakeRunner Runner, FixedTime Time) Make(AppInsightsOptions? options = null)
    {
        var runner = new FakeRunner();
        var time = new FixedTime(Now);
        var reader = new AppInsightsReader(Options.Create(options ?? Configured()), runner,
            new MemoryCache(new MemoryCacheOptions()), new AppInsightsProcessInfo(true, "swimhub-prodadmin"),
            NullLogger<AppInsightsReader>.Instance, time);
        return (reader, runner, time);
    }

    // ── Три состояния §4.1 ──────────────────────────────────────────────────

    [Fact]
    public async Task Not_configured_does_not_call_azure()
    {
        var (reader, runner, _) = Make(new AppInsightsOptions { ResourceId = "/subscriptions/x" });

        var overview = await reader.GetOverviewAsync(TimeSpan.FromHours(24), "web");
        var summary = await reader.GetSummaryAsync();

        Assert.Equal(AppInsightsStates.NotConfigured, overview.Connection.State);
        Assert.Contains(AppInsightsOptions.WorkspaceIdKey, overview.Connection.Text);
        Assert.DoesNotContain(AppInsightsOptions.ResourceIdKey, overview.Connection.Text);
        Assert.Equal(AppInsightsStates.NotConfigured, summary.Connection.State);
        Assert.Equal("neutral", summary.Tone);
        Assert.Empty(runner.Calls);
    }

    [Theory]
    [InlineData("credential")]
    [InlineData("auth")]
    public async Task Missing_or_expired_az_login_is_no_login_card(string kind)
    {
        var (reader, runner, _) = Make();
        runner.Throw = kind == "credential"
            ? new CredentialUnavailableException("Please run 'az login' to set up account.\nmore")
            : new AuthenticationFailedException("AADSTS700082: The refresh token has expired. Run az login.");

        var overview = await reader.GetOverviewAsync(TimeSpan.FromHours(1), "web");

        Assert.Equal(AppInsightsStates.NoLogin, overview.Connection.State);
        Assert.Equal("az login", overview.Connection.Hint);
        Assert.Empty(overview.Tiles);
        Assert.Equal("neutral", overview.Tone);
    }

    [Fact]
    public async Task Working_overview_maps_rows_and_sends_five_queries_for_the_role()
    {
        var (reader, runner, _) = Make();

        var o = await reader.GetOverviewAsync(TimeSpan.FromHours(24), "prodadmin");

        Assert.Equal(AppInsightsStates.Ok, o.Connection.State);
        Assert.Equal("prodadmin", o.Role);
        Assert.True(o.Process.Sending);
        Assert.Equal(8, o.Tiles.Count);
        Assert.Equal("66", o.Tiles.Single(t => t.Key == "requests").Value);
        Assert.Equal("GET api/clubs/{id:int}/overview", Assert.Single(o.SlowOperations).Operation);
        Assert.Equal(Now.AddMinutes(-3).UtcDateTime, o.LastTelemetryAt);
        Assert.NotNull(o.Links);
        Assert.EndsWith("/components/ai/failures", o.Links!.Failures);

        var queries = Assert.Single(runner.Calls);
        Assert.Equal(5, queries.Count);
        Assert.All(queries, q => Assert.Contains("'swimhub-prodadmin'", q.Kql));
    }

    [Fact]
    public async Task Summary_reads_site_for_24h_with_one_query()
    {
        var (reader, runner, _) = Make();
        runner.Answer = q => q.ToDictionary(x => x.Name, _ => new AppInsightsRows([TotalsRow(requests: 200, errors: 20)]));

        var s = await reader.GetSummaryAsync();

        Assert.Equal(AppInsightsStates.Ok, s.Connection.State);
        Assert.Equal("20", s.Value);
        Assert.Equal("danger", s.Tone);   // 10 % 5xx
        var query = Assert.Single(Assert.Single(runner.Calls));
        Assert.Contains("'swimhub-web'", query.Kql);
        Assert.Contains("let span = 1440m;", query.Kql);
    }

    // ── Сбои ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(403, null, "Нет доступа к рабочей области (HTTP 403)")]
    [InlineData(404, null, "Нет доступа к рабочей области (HTTP 404)")]
    [InlineData(0, "InvalidTokenError", "Нет доступа к рабочей области (InvalidTokenError)")]   // чужой WorkspaceId в пакете — живая проверка 17.11
    [InlineData(429, null, "ограничил частоту")]
    [InlineData(400, null, "ответил ошибкой (HTTP 400): Azure says no")]
    [InlineData(0, "BadArgumentError", "ответил ошибкой (BadArgumentError): Azure says no")]
    public async Task Azure_errors_become_failed_card(int status, string? code, string text)
    {
        var (reader, runner, _) = Make();
        runner.Throw = new RequestFailedException(status,
            "Batch query with id '0' failed.\nAzure says no\n\nRaw:\n{\"innererror\":\"tenant 3b9b…\"}", code, null);

        var o = await reader.GetOverviewAsync(TimeSpan.FromHours(24), "web");

        Assert.Equal(AppInsightsStates.Failed, o.Connection.State);
        Assert.Contains(text, o.Connection.Text);
        Assert.DoesNotContain("Raw", o.Connection.Text);
        Assert.DoesNotContain("tenant", o.Connection.Text);
    }

    [Fact]
    public async Task Slow_azure_hits_timeout_and_returns_failed_card()
    {
        var (reader, runner, _) = Make();
        runner.Delay = TimeSpan.FromSeconds(30);   // дольше таймаута 15 с — отмена придёт от читателя

        var started = DateTime.UtcNow;
        var s = await reader.GetSummaryAsync();

        Assert.Equal(AppInsightsStates.Failed, s.Connection.State);
        Assert.Contains("не ответил за 15 с", s.Connection.Text);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(25));
    }

    [Fact]
    public async Task Cancelled_by_client_is_rethrown_not_turned_into_card()
    {
        var (reader, runner, _) = Make();
        runner.Delay = TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.GetSummaryAsync(cts.Token));
    }

    // Живьём 17.11 и 19.11: SDK не разобрал ответ $batch («requires an element of type 'Object',
    // but the target element has type 'String'»). С 19.11 пакета нет (AzureLogsQueryRunner), повтор
    // оставлен как страховка на такой же ответ одиночного /query.
    private static readonly InvalidOperationException Unreadable =
        new("The requested operation requires an element of type 'Object', but the target element has type 'String'.");

    [Fact]
    public async Task Unreadable_response_is_retried_once_and_then_succeeds()
    {
        var (reader, runner, _) = Make();
        runner.Throw = Unreadable;
        runner.ThrowTimes = 1;

        var s = await reader.GetSummaryAsync();

        Assert.Equal(AppInsightsStates.Ok, s.Connection.State);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task Unreadable_response_twice_is_no_data_yet_not_error()
    {
        var (reader, runner, _) = Make();
        runner.Throw = Unreadable;

        var o = await reader.GetOverviewAsync(TimeSpan.FromHours(1), "web");

        Assert.Equal(AppInsightsStates.NoData, o.Connection.State);
        Assert.Equal("No data yet", o.Connection.Title);
        Assert.DoesNotContain("target element", o.Connection.Text);
        Assert.Equal(2, runner.Calls.Count);

        runner.Throw = null;   // «данных пока нет» не кэшируется — следующая загрузка читает заново
        Assert.Equal(AppInsightsStates.Ok, (await reader.GetOverviewAsync(TimeSpan.FromHours(1), "web")).Connection.State);
    }

    // Решение Влада 19.11: пустой ответ (первые минуты после выкатки / перезапуска ProdAdmin) — «No data yet»,
    // а не «Не удалось прочитать». На дашборде — «—» и нейтральный тон, без причин.
    [Fact]
    public async Task Empty_totals_answer_is_no_data_yet_on_card_and_page()
    {
        var (reader, runner, _) = Make();
        runner.Answer = q => q.ToDictionary(x => x.Name, _ => new AppInsightsRows([]));

        var s = await reader.GetSummaryAsync();
        Assert.Equal(AppInsightsStates.NoData, s.Connection.State);
        Assert.Equal("—", s.Value);
        Assert.Equal("neutral", s.Tone);
        Assert.Empty(s.Reasons);
        Assert.Equal("No data yet", s.Caption);

        var o = await reader.GetOverviewAsync(TimeSpan.FromHours(24), "web");
        Assert.Equal(AppInsightsStates.NoData, o.Connection.State);

        runner.Answer = null;
        Assert.Equal(AppInsightsStates.Ok, (await reader.GetSummaryAsync()).Connection.State);
    }

    [Fact]
    public async Task Real_azure_error_stays_failed_not_no_data()
    {
        var (reader, runner, _) = Make();
        runner.Throw = new RequestFailedException(400, "Query could not be parsed", "BadArgumentError", null);

        var s = await reader.GetSummaryAsync();

        Assert.Equal(AppInsightsStates.Failed, s.Connection.State);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task Login_and_access_errors_are_not_retried()
    {
        var (reader, runner, _) = Make();
        runner.Throw = new CredentialUnavailableException("Please run 'az login'");
        await reader.GetSummaryAsync();
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task Missing_result_in_answer_is_no_data_yet()
    {
        var (reader, runner, _) = Make();
        runner.Answer = q => DefaultAnswer(q).Where(x => x.Key != AppInsightsQueries.ErrorsName)
            .ToDictionary(x => x.Key, x => x.Value);

        var o = await reader.GetOverviewAsync(TimeSpan.FromHours(24), "web");

        Assert.Equal(AppInsightsStates.NoData, o.Connection.State);
    }

    // ── Кэш ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ok_answer_is_cached_per_range_and_role_refresh_bypasses()
    {
        var (reader, runner, _) = Make();

        await reader.GetOverviewAsync(TimeSpan.FromHours(24), "web");
        await reader.GetOverviewAsync(TimeSpan.FromHours(24), "WEB");
        Assert.Single(runner.Calls);

        await reader.GetOverviewAsync(TimeSpan.FromHours(1), "web");
        await reader.GetOverviewAsync(TimeSpan.FromHours(24), "prodadmin");
        Assert.Equal(3, runner.Calls.Count);

        await reader.GetOverviewAsync(TimeSpan.FromHours(24), "web", refresh: true);
        Assert.Equal(4, runner.Calls.Count);

        await reader.GetSummaryAsync();
        await reader.GetSummaryAsync();
        Assert.Equal(5, runner.Calls.Count);
    }

    [Fact]
    public async Task Failure_is_not_cached_so_page_recovers_right_after_az_login()
    {
        var (reader, runner, _) = Make();
        runner.Throw = new CredentialUnavailableException("Please run 'az login'");
        Assert.Equal(AppInsightsStates.NoLogin, (await reader.GetSummaryAsync()).Connection.State);

        runner.Throw = null;
        Assert.Equal(AppInsightsStates.Ok, (await reader.GetSummaryAsync()).Connection.State);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task Unknown_role_is_rejected()
    {
        var (reader, _, _) = Make();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reader.GetOverviewAsync(TimeSpan.FromHours(1), "swimhub-dev"));
    }

    // ── Разбор строк ────────────────────────────────────────────────────────

    [Fact]
    public void RowMapper_reads_json_elements_and_typed_values()
    {
        var json = JsonDocument.Parse("""{"n":66,"d":637.27,"s":"x","b":true,"t":"2026-11-17T12:08:21.4084923Z","nul":null}""").RootElement;
        var row = new Dictionary<string, object?>
        {
            ["Requests"] = json.GetProperty("n"), ["P95Ms"] = json.GetProperty("d"), ["ExceptionType"] = json.GetProperty("s"),
            ["ServerError"] = json.GetProperty("b"), ["LastTelemetryAt"] = json.GetProperty("t"), ["P50Ms"] = json.GetProperty("nul"),
            ["Errors5xx"] = 3, ["DbP95Ms"] = double.NaN, ["IngestedTodayMb"] = 0.25m,
        };

        Assert.Equal(66, AppInsightsRowMapper.Long(row, "Requests"));
        Assert.Equal(3, AppInsightsRowMapper.Long(row, "Errors5xx"));
        Assert.Equal(637.27, AppInsightsRowMapper.Double(row, "P95Ms"));
        Assert.Null(AppInsightsRowMapper.Double(row, "P50Ms"));
        Assert.Null(AppInsightsRowMapper.Double(row, "DbP95Ms"));   // NaN перцентиля на пустых данных
        Assert.Equal(0.25, AppInsightsRowMapper.Double(row, "IngestedTodayMb"));
        Assert.Equal("x", AppInsightsRowMapper.Text(row, "ExceptionType"));
        Assert.True(AppInsightsRowMapper.Bool(row, "ServerError"));
        var at = AppInsightsRowMapper.Date(row, "LastTelemetryAt");
        Assert.Equal(new DateTime(2026, 11, 17, 12, 8, 21, DateTimeKind.Utc), at!.Value.AddTicks(-(at.Value.Ticks % TimeSpan.TicksPerSecond)));
        Assert.Equal(DateTimeKind.Utc, at.Value.Kind);
        Assert.Equal(0, AppInsightsRowMapper.Long(row, "NoSuchColumn"));
        Assert.Equal("", AppInsightsRowMapper.Text(row, "NoSuchColumn"));
    }

    /// <summary>
    /// Ответ запроса итогов в форме прода (живой /query 19.11.2026 08:00 UTC, после меток PR #225):
    /// 18 колонок, значения — JsonElement. Колонки сходятся с маппером, карточка дашборда — с цифрами.
    /// </summary>
    [Fact]
    public void Prod_shaped_totals_row_19_11_fills_dashboard_card()
    {
        const string table = """
            {"columns":[{"name":"Requests"},{"name":"Errors5xx"},{"name":"P50Ms"},{"name":"P95Ms"},{"name":"SlowOperations"},
              {"name":"DbCalls"},{"name":"DbP95Ms"},{"name":"DbFailed"},{"name":"Exceptions"},{"name":"BackgroundFailures"},
              {"name":"AdminFailures"},{"name":"LastTelemetryAt"},{"name":"IngestedTodayMb"},{"name":"Visitors"},{"name":"Bots"},
              {"name":"Internal"},{"name":"Cold"},{"name":"Starts"}],
             "rows":[[1318,0,18.922792486145404,2258.34065,42,10194,111.90793948412698,0,0,0,11,"2026-11-19T07:57:22.9698135Z",16.855593,1272,15,31,0,15]]}
            """;
        var doc = JsonDocument.Parse(table).RootElement;
        var names = doc.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("name").GetString()!).ToList();
        var rows = doc.GetProperty("rows").EnumerateArray()
            .Select(r => (IReadOnlyDictionary<string, object?>)names.Zip(r.EnumerateArray(), (n, v) => (n, v))
                .ToDictionary(x => x.n, x => (object?)x.v))
            .ToList();

        var t = AppInsightsRowMapper.Totals(new AppInsightsRows(rows));
        Assert.Equal(1318, t.Requests);
        Assert.Equal(2258.34065, t.P95Ms);
        Assert.Equal(42, t.SlowOperations);
        Assert.Equal(11, t.AdminFailures);
        Assert.Equal((1272L, 15L, 31L, 0L, 15L), (t.Visitors, t.Bots, t.Internal, t.Cold, t.Starts));
        Assert.Equal(new DateTime(2026, 11, 19, 7, 57, 22, DateTimeKind.Utc), t.LastTelemetryAt!.Value.AddTicks(-(t.LastTelemetryAt.Value.Ticks % TimeSpan.TicksPerSecond)));

        var s = AppInsightsOverviewBuilder.Summary(
            AppInsightsConnections.Ok(), t, 50, new DateTime(2026, 11, 19, 7, 58, 42, DateTimeKind.Utc));
        Assert.Equal(AppInsightsStates.Ok, s.Connection.State);
        Assert.NotEqual("—", s.Value);
        Assert.Equal(1318, s.Requests);
        Assert.Contains("1318 запросов", s.Caption);
        Assert.NotEmpty(s.Metrics);
    }

    // ── Параллельные запросы вместо $batch ─────────────────────────────────

    [Fact]
    public async Task RunEach_returns_every_query_by_name()
    {
        var queries = AppInsightsQueries.Overview(AppInsightsRoles.WebRoleName, TimeSpan.FromHours(24), 0);
        var rows = await AzureLogsQueryRunner.RunEachAsync(queries, async (q, ct) =>
        {
            await Task.Delay(5, ct);
            return new AppInsightsRows([new Dictionary<string, object?> { ["Name"] = q.Name }]);
        }, CancellationToken.None);

        Assert.Equal(queries.Select(q => q.Name).Order(), rows.Keys.Order());
        Assert.All(rows, kv => Assert.Equal(kv.Key, kv.Value.Rows[0]["Name"]));
    }

    [Fact]
    public async Task RunEach_rethrows_the_real_failure_and_cancels_the_rest()
    {
        var queries = AppInsightsQueries.Overview(AppInsightsRoles.WebRoleName, TimeSpan.FromHours(24), 0);
        var cancelled = 0;
        var ex = await Assert.ThrowsAsync<RequestFailedException>(() => AzureLogsQueryRunner.RunEachAsync(queries, async (q, ct) =>
        {
            if (q.Name == AppInsightsQueries.ErrorsName)
            {
                await Task.Delay(10, CancellationToken.None);
                throw new RequestFailedException(504, "Gateway timeout");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
            catch (OperationCanceledException) { Interlocked.Increment(ref cancelled); throw; }
            return new AppInsightsRows([]);
        }, CancellationToken.None));

        Assert.Equal(504, ex.Status);   // не AggregateException и не отмена соседей
        Assert.Equal(queries.Count - 1, cancelled);
    }

    [Fact]
    public void RowMapper_empty_totals_mean_nothing_happened()
    {
        var t = AppInsightsRowMapper.Totals(new AppInsightsRows([]));
        Assert.Equal(0, t.Requests);
        Assert.Null(t.P95Ms);
        Assert.Null(t.LastTelemetryAt);
        Assert.Equal(0, t.IngestedTodayMb);
    }

    // ── Ссылки в портал ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("swimhub-ai")]
    [InlineData("/subscriptions/x\"><script>")]
    public void Portal_links_need_real_resource_id(string? id) => Assert.Null(AppInsightsPortalLinks.Build(id));

    [Fact]
    public void Portal_links_are_built_from_resource_id()
    {
        var links = AppInsightsPortalLinks.Build("/subscriptions/s/resourceGroups/rg/providers/microsoft.insights/components/ai/")!;
        Assert.Equal("https://portal.azure.com/#@/resource/subscriptions/s/resourceGroups/rg/providers/microsoft.insights/components/ai/overview", links.Overview);
        Assert.EndsWith("/ai/quickPulse", links.LiveMetrics);
        Assert.EndsWith("/ai/logs", links.Logs);
        Assert.EndsWith("/ai/performance", links.Performance);
    }
}
