using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Отладочные подробности: общий тумблер (настройка <c>DebugDetails</c>) + частные опции
/// (<c>Sys_DebugOptions</c>). Единственное место, где эти два уровня складываются вместе —
/// вторая копия правила «и то, и другое» рано или поздно разъедется.
/// </summary>
public interface IDebugOptionsService
{
    /// <summary>Общий тумблер из /Admin/Settings.</summary>
    bool MasterEnabled { get; }

    /// <summary>
    /// Действует ли опция прямо сейчас: общий тумблер включён И галочка опции стоит.
    /// Неизвестный ключ — false.
    /// </summary>
    Task<bool> IsEnabledAsync(string key, CancellationToken ct = default);

    /// <summary>Все опции для админки — вместе с состоянием общего тумблера.</summary>
    Task<DebugOptionsDto> GetAllAsync(CancellationToken ct = default);

    /// <summary>Переключить опцию. false — ключа нет.</summary>
    Task<bool> SetAsync(string key, bool enabled, string? updatedBy, CancellationToken ct = default);
}
