using Microsoft.AspNetCore.Mvc;
using Moq;
using Swimm.API.Controllers;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Services.AppInsights;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// KQL страницы (<see cref="AppInsightsQueries"/>) и разбор параметров адреса — ввод пользователя в
/// запрос не попадает: роль и период только из белых списков. Сам KQL сверен на живых данных
/// 17.11.2026 (план Б1); здесь — что каждый запрос ограничен ролью, периодом и правилами данных.
/// </summary>
public class AppInsightsQueriesTests
{
    [Fact]
    public void Overview_queries_are_scoped_to_role_and_range()
    {
        var queries = AppInsightsQueries.Overview(AppInsightsRoles.WebRoleName, TimeSpan.FromHours(1), 10);

        Assert.Equal(
            [AppInsightsQueries.TotalsName, AppInsightsQueries.ExceptionsName, AppInsightsQueries.SlowName,
             AppInsightsQueries.ErrorsName, AppInsightsQueries.BackgroundName],
            queries.Select(q => q.Name));
        Assert.All(queries, q =>
        {
            Assert.Contains("let role = 'swimhub-web';", q.Kql);
            Assert.Contains("let span = 60m;", q.Kql);
            Assert.Contains("AppRoleName == role", q.Kql);
        });
    }

    [Fact]
    public void Server_errors_are_counted_by_result_code_not_success_flag()
    {
        // Success=false стоит и у 404 (живая проверка А2) — ошибки сервера только по коду 5xx.
        var kql = AppInsightsQueries.Totals(AppInsightsRoles.WebRoleName, TimeSpan.FromHours(24), 10);
        Assert.Contains("countif(toint(ResultCode) >= 500)", kql);
        Assert.DoesNotContain("Errors5xx=countif(Success", kql);
        Assert.Contains("DbFailed=countif(Success == false)", kql);   // у Postgres Success — честный
    }

    [Fact]
    public void Background_means_empty_operation_id_and_admin_failures_match_alert_rule()
    {
        var totals = AppInsightsQueries.Totals(AppInsightsRoles.WebRoleName, TimeSpan.FromHours(24), 10);
        Assert.Contains("isempty(OperationId)", totals);
        Assert.Contains("AppRoleName == 'swimhub-prodadmin' | summarize AdminFailures", totals);
        Assert.Contains("SeverityLevel >= 3", totals);
        Assert.Contains("isempty(OperationId)", AppInsightsQueries.Background(AppInsightsRoles.WebRoleName, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Ingestion_counts_billable_usage_since_cap_reset()
    {
        var kql = AppInsightsQueries.Totals(AppInsightsRoles.WebRoleName, TimeSpan.FromHours(24), 10);
        Assert.Contains("let capResetHour = 10h;", kql);
        Assert.Contains("Usage | where TimeGenerated >= resetAt and IsBillable == true", kql);
    }

    [Fact]
    public void Spa_paths_are_grouped_with_id_placeholder()
    {
        Assert.Contains(@"replace_regex(Name, @'/\d+', '/{id}')", AppInsightsQueries.SlowOperations(AppInsightsRoles.WebRoleName, TimeSpan.FromHours(1)));
        Assert.Contains(@"replace_regex(Name, @'/\d+', '/{id}')", AppInsightsQueries.Errors(AppInsightsRoles.WebRoleName, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Summary_is_one_query_for_site_24h()
    {
        var q = Assert.Single(AppInsightsQueries.Summary(10));
        Assert.Contains("let role = 'swimhub-web';", q.Kql);
        Assert.Contains("let span = 1440m;", q.Kql);
    }

    [Theory]
    [InlineData("swimhub-dev")]
    [InlineData("swimhub-web'; Usage | take 1; let x='")]
    [InlineData("")]
    public void Unknown_role_never_reaches_kql(string role) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AppInsightsQueries.Totals(role, TimeSpan.FromHours(1), 10));

    [Fact]
    public void Range_must_be_sane() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AppInsightsQueries.Span(TimeSpan.FromDays(400)));

    // ── Параметры адреса ────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "24h")]
    [InlineData("", "24h")]
    [InlineData("1h", "1h")]
    [InlineData("24H", "24h")]
    [InlineData("7d", "7d")]
    [InlineData("30d", null)]
    [InlineData("1h;drop", null)]
    public void Range_keys(string? key, string? normalized) => Assert.Equal(normalized, AppInsightsRanges.Normalize(key));

    [Theory]
    [InlineData(null, "web", "swimhub-web")]
    [InlineData("web", "web", "swimhub-web")]
    [InlineData("ProdAdmin", "prodadmin", "swimhub-prodadmin")]
    [InlineData("dev", null, null)]
    public void Role_keys(string? key, string? normalized, string? roleName)
    {
        Assert.Equal(normalized, AppInsightsRoles.Normalize(key));
        Assert.Equal(roleName, AppInsightsRoles.RoleName(key));
    }

    [Fact]
    public async Task Controller_rejects_unknown_range_and_role_without_calling_reader()
    {
        var reader = new Mock<IAppInsightsReader>(MockBehavior.Strict);
        var controller = new AppInsightsAdminController(reader.Object);

        Assert.IsType<BadRequestObjectResult>(await controller.Overview("30d", "web", false, default));
        Assert.IsType<BadRequestObjectResult>(await controller.Overview("1h", "dev", false, default));
    }

    [Fact]
    public async Task Controller_passes_normalized_range_and_role()
    {
        var reader = new Mock<IAppInsightsReader>();
        reader.Setup(r => r.GetOverviewAsync(TimeSpan.FromDays(7), "prodadmin", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AppInsightsOverview)null!);
        var controller = new AppInsightsAdminController(reader.Object);

        Assert.IsType<OkObjectResult>(await controller.Overview("7d", "PRODADMIN", true, default));
        reader.VerifyAll();
    }
}
