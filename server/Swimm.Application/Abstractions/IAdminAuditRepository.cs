using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>Чтение журнала аудита для админ-страницы /Admin/Audit (фаза 7.4).</summary>
public interface IAdminAuditRepository
{
    /// <summary>Страница журнала (новые сверху) под фильтром.</summary>
    Task<PagedResult<AdminAuditRowDto>> QueryAsync(AdminAuditFilter filter, CancellationToken ct = default);

    /// <summary>Различные коды действий (для выпадашки фильтра), по алфавиту.</summary>
    Task<IReadOnlyList<string>> GetDistinctActionsAsync(CancellationToken ct = default);
}
