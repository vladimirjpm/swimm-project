using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Аудит ручных мутаций админки (фаза 7.4): «кто / что / когда» по каждой рискованной
/// операции (склейки, удаления, смена ролей/настроек и т.п.). Sys_-таблица, БЕЗ grant swimm_ro.
///
/// Actor денормализован намеренно: <see cref="ActorUserId"/> без FK на Sys_AppUsers, а
/// <see cref="ActorName"/> — снимок email/имени на момент действия. Так запись переживает
/// удаление пользователя и остаётся читаемой без JOIN.
/// </summary>
public class AdminAudit
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>Id админа (claim NameIdentifier); null — вне HTTP (CLI-команды, фон).</summary>
    public int? ActorUserId { get; set; }

    /// <summary>Снимок email/имени актора на момент действия ("cli" — вне HTTP-контекста).</summary>
    [Required, MaxLength(256)]
    public string ActorName { get; set; } = string.Empty;

    /// <summary>Машиночитаемый код действия, напр. "swimmer.merge", "competition.delete".</summary>
    [Required, MaxLength(80)]
    public string Action { get; set; } = string.Empty;

    /// <summary>Тип затронутой сущности, напр. "Swimmer", "Competition", "Setting".</summary>
    [Required, MaxLength(60)]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Идентификатор цели (строкой — допускает составные ключи); null для массовых операций.</summary>
    [MaxLength(120)]
    public string? EntityId { get; set; }

    /// <summary>Человекочитаемая сводка действия.</summary>
    [MaxLength(1000)]
    public string Summary { get; set; } = string.Empty;

    /// <summary>Опциональная полезная нагрузка (JSON): детали до/после, список id и т.п.</summary>
    public string? DetailsJson { get; set; }

    /// <summary>IP актора (если доступен).</summary>
    [MaxLength(64)]
    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
