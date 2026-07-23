using System.Text.Json;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Пишет строку аудита в Sys_AdminAudit. Actor берётся из <see cref="ICurrentActor"/>
/// (в HTTP — текущий админ, вне HTTP — "cli").
///
/// Best-effort: любые сбои записи глотаются и логируются — аудит не должен ломать саму
/// мутацию. Вызывать ПОСЛЕ успешного SaveChanges мутации: сервис делает собственный
/// SaveChanges на общем scoped-контексте, поэтому на момент вызова трекер должен быть чист.
/// </summary>
public class AdminAuditService(
    SwimmDbContext db,
    ICurrentActor actor,
    ILogger<AdminAuditService> logger) : IAdminAuditService
{
    public async Task LogAsync(
        string action,
        string entityType,
        string? entityId = null,
        string? summary = null,
        object? details = null,
        CancellationToken ct = default)
    {
        try
        {
            db.AdminAudits.Add(new AdminAudit
            {
                ActorUserId = actor.UserId,
                ActorName = Trunc(actor.Name, 256) ?? "cli",
                Action = Trunc(action, 80)!,
                EntityType = Trunc(entityType, 60)!,
                EntityId = Trunc(entityId, 120),
                Summary = Trunc(summary, 1000) ?? string.Empty,
                DetailsJson = details is null ? null : JsonSerializer.Serialize(details),
                IpAddress = Trunc(actor.IpAddress, 64),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Аудит — вспомогательная запись: не роняем мутацию, только логируем.
            logger.LogError(ex, "Не удалось записать аудит: {Action} {EntityType} {EntityId}",
                action, entityType, entityId);
        }
    }

    private static string? Trunc(string? s, int max) =>
        s is not null && s.Length > max ? s[..max] : s;
}
