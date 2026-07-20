using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>Сводка «Статус данных» для дашборда /Admin (docs/plans/admin-dashboard-status-cards-plan.md).</summary>
public interface IDashboardStatusService
{
    Task<DashboardStatusSummary> GetStatusAsync(CancellationToken ct = default);
}
