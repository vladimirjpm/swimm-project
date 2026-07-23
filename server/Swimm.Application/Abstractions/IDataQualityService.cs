using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Порт read-only выборок «здоровье данных» для deep-link секций (T3b,
/// docs/tasks/dashboard-deeplinks-lists-sonnet.md) — те же предикаты, что считает
/// <see cref="IDashboardStatusService"/> для счётчиков на дашборде. Все списки капнуты
/// топ-200 (<see cref="CappedListDto{T}"/>), без мутаций.
/// </summary>
public interface IDataQualityService
{
    /// <summary>filter: no-org-id | no-results. Неизвестное значение → пустой список.</summary>
    Task<CappedListDto<SwimmerQualityRowDto>> GetSwimmerQualityAsync(string filter, CancellationToken ct = default);

    /// <summary>filter: no-swimmers | no-country. Неизвестное значение → пустой список.</summary>
    Task<CappedListDto<ClubQualityRowDto>> GetClubQualityAsync(string filter, CancellationToken ct = default);

    /// <summary>FK-аномалии Results + пустые эстафеты (Admin/Results, секция «Аномалии»).</summary>
    Task<ResultAnomaliesDto> GetResultAnomaliesAsync(CancellationToken ct = default);

    /// <summary>Публикации медиа в статусе pending (Admin/Media?filter=moderation-pending), read-only обзор.</summary>
    Task<CappedListDto<ModerationPendingRowDto>> GetModerationPendingAsync(CancellationToken ct = default);

    /// <summary>Заявки на вступление в группы, ожидающие решения (Admin/HubGroups?tab=requests).</summary>
    Task<CappedListDto<HubGroupJoinRequestRowDto>> GetPendingJoinRequestsAsync(CancellationToken ct = default);
}
