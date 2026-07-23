namespace Swimm.Application.Dtos;

/// <summary>Одна строка аудита для админ-виджета «Журнал действий» (фаза 7.4).</summary>
public sealed record AdminAuditRowDto(
    long Id,
    int? ActorUserId,
    string ActorName,
    string Action,
    string EntityType,
    string? EntityId,
    string Summary,
    string? DetailsJson,
    string? IpAddress,
    DateTime CreatedAt);

/// <summary>Фильтр журнала аудита (все поля опциональны).</summary>
public sealed record AdminAuditFilter(
    string? Action = null,
    string? EntityType = null,
    int? ActorUserId = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 30,
    /// <summary>Нижняя граница CreatedAt (UTC) — deep-link фильтр по периоду (24h/7d/30d).</summary>
    DateTime? SinceUtc = null);
