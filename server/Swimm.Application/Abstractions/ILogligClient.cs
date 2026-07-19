using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>Клиент для чтения публичной карточки игрока loglig.com (docs/loglig-id-plan.md).</summary>
public interface ILogligClient
{
    /// <summary>Тянет карточку игрока loglig; null — карточка недоступна (404/500/невалидный HTML).</summary>
    Task<LogligPlayerCard?> GetPlayerCardAsync(int logligId, CancellationToken ct = default);
}
