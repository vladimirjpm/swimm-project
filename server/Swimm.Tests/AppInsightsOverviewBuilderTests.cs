using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Плитки /Admin/AppInsights, тон здоровья и главная цифра блока дашборда
/// (docs/plans/application-insights-plan.md, Б1–Б3) — <see cref="AppInsightsOverviewBuilder"/>.
/// Пороги — <see cref="AppInsightsThresholds"/>, согласованы с оповещениями А4.
/// </summary>
public class AppInsightsOverviewBuilderTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
    private const double CapMb = 150;

    private static AppInsightsTotals Totals(
        long requests = 100, long errors = 0, double? p50 = 40, double? p95 = 300, long slow = 0,
        long dbCalls = 500, double? dbP95 = 30, long dbFailed = 0, long exceptions = 0, long background = 0,
        long admin = 0, DateTime? last = null, double ingestedMb = 10, bool noLast = false) =>
        new(requests, errors, p50, p95, slow, dbCalls, dbP95, dbFailed, exceptions, background, admin,
            noLast ? null : last ?? Now.AddMinutes(-2), ingestedMb);

    private static readonly AppInsightsProcessInfo Process = new(false, "swimhub-dev");

    private static OverviewTile Tile(IReadOnlyList<OverviewTile> tiles, string key) => tiles.Single(t => t.Key == key);

    // ── Тоны по отдельности ─────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 100, "ok")]
    [InlineData(1, 100, "warning")]
    [InlineData(5, 100, "warning")]   // ровно 5 % — ещё жёлтое
    [InlineData(6, 100, "danger")]
    [InlineData(1, 3, "danger")]      // мало трафика — доля всё равно считается
    [InlineData(0, 0, "ok")]
    public void ErrorTone_by_share_of_5xx(long errors, long requests, string tone) =>
        Assert.Equal(tone, AppInsightsOverviewBuilder.ErrorTone(errors, requests));

    [Theory]
    [InlineData(2, "ok")]
    [InlineData(15, "ok")]
    [InlineData(16, "warning")]
    [InlineData(30, "warning")]
    [InlineData(31, "danger")]
    public void SilenceTone_for_site_by_minutes(int minutes, string tone) =>
        Assert.Equal(tone, AppInsightsOverviewBuilder.SilenceTone(Now.AddMinutes(-minutes), Now, watch: true));

    [Fact]
    public void SilenceTone_no_telemetry_at_all_is_danger_for_site_and_neutral_for_prodadmin()
    {
        Assert.Equal("danger", AppInsightsOverviewBuilder.SilenceTone(null, Now, watch: true));
        Assert.Equal("neutral", AppInsightsOverviewBuilder.SilenceTone(null, Now, watch: false));
        Assert.Equal("neutral", AppInsightsOverviewBuilder.SilenceTone(Now.AddDays(-3), Now, watch: false));
    }

    [Theory]
    [InlineData(10, "ok")]
    [InlineData(119.9, "ok")]
    [InlineData(120, "warning")]   // 80 % — как письмо «Объём» (mb > 120)
    [InlineData(150, "danger")]    // лимит исчерпан — приём стоит
    public void IngestionTone_against_daily_cap(double mb, string tone) =>
        Assert.Equal(tone, AppInsightsOverviewBuilder.IngestionTone(mb, CapMb));

    // ── Здоровье (тон страницы и блока) ─────────────────────────────────────

    [Fact]
    public void Health_all_quiet_is_ok_without_reasons()
    {
        var (tone, reasons) = AppInsightsOverviewBuilder.Health(Totals(), true, true, CapMb, Now);
        Assert.Equal("ok", tone);
        Assert.Empty(reasons);
    }

    [Fact]
    public void Health_silent_site_over_30_min_is_danger()
    {
        var (tone, reasons) = AppInsightsOverviewBuilder.Health(Totals(last: Now.AddMinutes(-45)), true, true, CapMb, Now);
        Assert.Equal("danger", tone);
        Assert.Contains(reasons, r => r.Contains("сайт молчит"));
    }

    [Fact]
    public void Health_error_rate_over_5_percent_is_danger_and_goes_first()
    {
        var (tone, reasons) = AppInsightsOverviewBuilder.Health(Totals(errors: 10, exceptions: 2), true, true, CapMb, Now);
        Assert.Equal("danger", tone);
        Assert.StartsWith("ошибок сервера (5xx): 10 из 100", reasons[0]);
        Assert.Contains(reasons, r => r == "исключений: 2");
    }

    [Theory]
    [InlineData(1, 0, 0, 0, 10)]     // одна 5xx из 100
    [InlineData(0, 1, 0, 0, 10)]     // исключение
    [InlineData(0, 0, 1, 0, 10)]     // сбой вне запроса
    [InlineData(0, 0, 0, 1, 10)]     // ошибка ProdAdmin (блок дашборда)
    [InlineData(0, 0, 0, 0, 130)]    // приём выше 80 %
    public void Health_minor_problems_are_warning(long errors, long exceptions, long background, long admin, double mb)
    {
        var t = Totals(errors: errors, exceptions: exceptions, background: background, admin: admin, ingestedMb: mb);
        Assert.Equal("warning", AppInsightsOverviewBuilder.Health(t, true, includeAdmin: true, CapMb, Now).Tone);
    }

    [Fact]
    public void Health_admin_failures_count_only_when_asked()
    {
        var t = Totals(admin: 3);
        Assert.Equal("ok", AppInsightsOverviewBuilder.Health(t, true, includeAdmin: false, CapMb, Now).Tone);
        Assert.Equal("warning", AppInsightsOverviewBuilder.Health(t, true, includeAdmin: true, CapMb, Now).Tone);
    }

    [Fact]
    public void Health_prodadmin_silence_is_not_a_problem()
    {
        var (tone, _) = AppInsightsOverviewBuilder.Health(Totals(noLast: true, requests: 0, dbCalls: 0), false, false, CapMb, Now);
        Assert.Equal("ok", tone);
    }

    // ── Плитки страницы ─────────────────────────────────────────────────────

    [Fact]
    public void Tiles_cover_plan_list_in_order()
    {
        var tiles = AppInsightsOverviewBuilder.Tiles(Totals(), TimeSpan.FromHours(24), true, CapMb, 10, Now);
        Assert.Equal(["requests", "errors", "latency", "exceptions", "db", "slow", "ingestion", "last"], tiles.Select(t => t.Key));
        Assert.All(tiles, t => Assert.StartsWith("#", t.Href));
    }

    [Fact]
    public void Tiles_values_and_tones_for_typical_day()
    {
        var tiles = AppInsightsOverviewBuilder.Tiles(
            Totals(requests: 200, errors: 1, p50: 35.4, p95: 637.3, slow: 2, dbP95: 70.4, ingestedMb: 0.1),
            TimeSpan.FromHours(24), true, CapMb, 10, Now);

        Assert.Equal("200", Tile(tiles, "requests").Value);
        Assert.Equal("за 24 ч", Tile(tiles, "requests").Sub);
        Assert.Equal("0,5 %", Tile(tiles, "errors").Value);
        Assert.Equal("1 из 200", Tile(tiles, "errors").Sub);
        Assert.Equal("warning", Tile(tiles, "errors").Tone);
        Assert.Equal("637 мс", Tile(tiles, "latency").Value);
        Assert.Equal("медиана 35 мс", Tile(tiles, "latency").Sub);
        Assert.Equal("ok", Tile(tiles, "latency").Tone);
        Assert.Equal("70 мс", Tile(tiles, "db").Value);
        Assert.Equal("ok", Tile(tiles, "db").Tone);
        Assert.Equal("info", Tile(tiles, "slow").Tone);
        Assert.Equal("0,1 / 150 МБ", Tile(tiles, "ingestion").Value);
        Assert.Contains("сброс в 10:00 UTC", Tile(tiles, "ingestion").Sub);
        Assert.Equal("2 мин назад", Tile(tiles, "last").Value);
        Assert.Equal("ok", Tile(tiles, "last").Tone);
    }

    [Fact]
    public void Tiles_without_requests_are_neutral_dashes()
    {
        var tiles = AppInsightsOverviewBuilder.Tiles(
            Totals(requests: 0, p50: null, p95: null, dbCalls: 0, dbP95: null), TimeSpan.FromHours(1), true, CapMb, 10, Now);
        Assert.Equal("—", Tile(tiles, "errors").Value);
        Assert.Equal("neutral", Tile(tiles, "errors").Tone);
        Assert.Equal("—", Tile(tiles, "latency").Value);
        Assert.Equal("neutral", Tile(tiles, "latency").Tone);
        Assert.Equal("neutral", Tile(tiles, "db").Tone);
        Assert.Equal("за 1 ч", Tile(tiles, "requests").Sub);
    }

    [Fact]
    public void Tiles_slow_response_and_db_failures_are_warning()
    {
        var tiles = AppInsightsOverviewBuilder.Tiles(Totals(p95: 4500, dbFailed: 1), TimeSpan.FromDays(7), true, CapMb, 10, Now);
        Assert.Equal("4500 мс", Tile(tiles, "latency").Value);
        Assert.Equal("warning", Tile(tiles, "latency").Tone);
        Assert.Equal("warning", Tile(tiles, "db").Tone);
        Assert.Equal("за 7 дней", Tile(tiles, "requests").Sub);
    }

    [Fact]
    public void Tiles_last_telemetry_for_prodadmin_is_neutral()
    {
        var tiles = AppInsightsOverviewBuilder.Tiles(Totals(noLast: true), TimeSpan.FromHours(24), false, CapMb, 10, Now);
        Assert.Equal("нет за 7 дней", Tile(tiles, "last").Value);
        Assert.Equal("ProdAdmin не запускался", Tile(tiles, "last").Sub);
        Assert.Equal("neutral", Tile(tiles, "last").Tone);
    }

    // ── Ответ страницы ──────────────────────────────────────────────────────

    [Fact]
    public void Overview_without_data_is_neutral_card_with_empty_tables()
    {
        var o = AppInsightsOverviewBuilder.Overview(AppInsightsConnections.NoLogin("Please run 'az login'"), Process,
            TimeSpan.FromHours(1), "prodadmin", null, null, CapMb, 10, Now);

        Assert.Equal(AppInsightsStates.NoLogin, o.Connection.State);
        Assert.Equal("az login", o.Connection.Hint);
        Assert.Equal("neutral", o.Tone);
        Assert.Equal("1h", o.Range);
        Assert.Equal("prodadmin", o.Role);
        Assert.Equal("swimhub-prodadmin", o.RoleName);
        Assert.Empty(o.Tiles);
        Assert.Empty(o.Exceptions);
        Assert.Null(o.LastTelemetryAt);
    }

    [Fact]
    public void Overview_with_data_carries_tone_tiles_and_tables()
    {
        var data = new AppInsightsOverviewData(Totals(exceptions: 1),
            [new("System.Exception", "boom", 1, Now.AddMinutes(-5), "GET api/x")], [], [], []);
        var o = AppInsightsOverviewBuilder.Overview(AppInsightsConnections.Ok(), Process, TimeSpan.FromHours(24), "web",
            data, null, CapMb, 10, Now);

        Assert.Equal(AppInsightsStates.Ok, o.Connection.State);
        Assert.Equal("warning", o.Tone);
        Assert.Equal("24h", o.Range);
        Assert.Equal("swimhub-web", o.RoleName);
        Assert.Equal(8, o.Tiles.Count);
        Assert.Single(o.Exceptions);
        Assert.Equal("2 мин назад", o.LastTelemetryAgo);
    }

    // ── Блок дашборда ───────────────────────────────────────────────────────

    [Fact]
    public void Summary_zero_errors_is_green_check_with_caption()
    {
        var s = AppInsightsOverviewBuilder.Summary(AppInsightsConnections.Ok(), Totals(requests: 1234, p95: 320), CapMb, Now);
        Assert.Equal("✓", s.Value);
        Assert.Equal("ok", s.ValueTone);
        Assert.Equal("ok", s.Tone);
        Assert.Equal("1234 запроса · p95 320 мс · телеметрия 2 мин назад", s.Caption);
        Assert.Equal(["errors", "exceptions", "background", "slow", "ingestion"], s.Metrics.Select(m => m.Key));
    }

    [Theory]
    [InlineData(1, "запрос")]
    [InlineData(2, "запроса")]
    [InlineData(5, "запросов")]
    [InlineData(11, "запросов")]
    [InlineData(21, "запрос")]
    public void Summary_caption_plural(long requests, string word) =>
        Assert.StartsWith($"{requests} {word} ·",
            AppInsightsOverviewBuilder.Summary(AppInsightsConnections.Ok(), Totals(requests: requests), CapMb, Now).Caption);

    [Fact]
    public void Summary_errors_show_count_and_admin_failures_make_it_yellow()
    {
        var s = AppInsightsOverviewBuilder.Summary(AppInsightsConnections.Ok(), Totals(errors: 3, admin: 2), CapMb, Now);
        Assert.Equal("3", s.Value);
        Assert.Equal("warning", s.ValueTone);
        Assert.Equal("warning", s.Tone);
        Assert.Equal("2", s.Metrics.Single(m => m.Key == "background").Value);
    }

    [Fact]
    public void Summary_silent_site_is_red_even_without_errors()
    {
        var s = AppInsightsOverviewBuilder.Summary(AppInsightsConnections.Ok(), Totals(last: Now.AddHours(-2)), CapMb, Now);
        Assert.Equal("✓", s.Value);
        Assert.Equal("danger", s.Tone);
    }

    [Theory]
    [InlineData(AppInsightsStates.NotConfigured)]
    [InlineData(AppInsightsStates.NoLogin)]
    [InlineData(AppInsightsStates.Failed)]
    public void Summary_without_data_is_gray_not_red(string state)
    {
        var connection = state switch
        {
            AppInsightsStates.NotConfigured => AppInsightsConnections.NotConfigured(["AppInsights:WorkspaceId"]),
            AppInsightsStates.NoLogin => AppInsightsConnections.NoLogin(null),
            _ => AppInsightsConnections.Failed("timeout"),
        };
        var s = AppInsightsOverviewBuilder.Summary(connection, null, CapMb, Now);
        Assert.Equal("neutral", s.Tone);
        Assert.Equal("—", s.Value);
        Assert.Equal(connection.Title, s.Caption);
        Assert.Empty(s.Metrics);
    }

    // ── Формат и тексты ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "—")]
    [InlineData(0.4, "0 мс")]
    [InlineData(320.6, "321 мс")]
    [InlineData(12_345.0, "12,3 с")]
    public void Ms_format(double? ms, string text) => Assert.Equal(text, AppInsightsOverviewBuilder.Ms(ms));

    [Theory]
    [InlineData(0.0, "0 %")]
    [InlineData(0.0042, "0,4 %")]
    [InlineData(0.123, "12 %")]
    public void Percent_format(double share, string text) => Assert.Equal(text, AppInsightsOverviewBuilder.Percent(share));

    [Fact]
    public void NotConfigured_lists_missing_keys_and_commands_without_ids()
    {
        var c = AppInsightsConnections.NotConfigured(["AppInsights:WorkspaceId", "AppInsights:ResourceId"]);
        Assert.Equal(AppInsightsStates.NotConfigured, c.State);
        Assert.Contains("AppInsights:WorkspaceId, AppInsights:ResourceId", c.Text);
        Assert.Contains("appsettings.Local.json", c.Text);
        Assert.Contains("az monitor log-analytics workspace show", c.Hint);
        Assert.DoesNotContain("/subscriptions/", c.Hint);
    }
}
