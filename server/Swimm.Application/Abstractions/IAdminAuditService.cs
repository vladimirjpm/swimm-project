namespace Swimm.Application.Abstractions;

/// <summary>
/// Аудит ручных мутаций админки (фаза 7.4): пишет «кто / что / когда» в Sys_AdminAudit.
/// Actor берётся из текущего HTTP-контекста (claim NameIdentifier + email); вне HTTP
/// (CLI-команды) actor = null / "cli". Логирование best-effort: сбой записи аудита не
/// должен ломать саму мутацию — реализация глотает и логирует исключения.
/// </summary>
public interface IAdminAuditService
{
    /// <summary>
    /// Записать одну строку аудита. Вызывать ПОСЛЕ успешного применения мутации.
    /// </summary>
    /// <param name="action">Машиночитаемый код, напр. "swimmer.merge".</param>
    /// <param name="entityType">Тип сущности, напр. "Swimmer".</param>
    /// <param name="entityId">Id цели (или null для массовых операций).</param>
    /// <param name="summary">Человекочитаемая сводка.</param>
    /// <param name="details">Опциональный объект — сериализуется в JSON в DetailsJson.</param>
    Task LogAsync(
        string action,
        string entityType,
        string? entityId = null,
        string? summary = null,
        object? details = null,
        CancellationToken ct = default);
}
