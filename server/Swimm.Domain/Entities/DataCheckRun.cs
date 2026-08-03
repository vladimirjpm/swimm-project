using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>Прогон реестра проверок (docs/data-integrity.md, фаза Д3).</summary>
public class DataCheckRun
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }

    /// <summary>manual | import | schedule — кто запустил.</summary>
    [Required, MaxLength(20)]
    public string Trigger { get; set; } = "manual";

    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }

    /// <summary>Находок, исчезнувших с прошлого прогона (данные починены).</summary>
    public int FixedCount { get; set; }
}

/// <summary>
/// Находка проверки. Живёт ДО УСТРАНЕНИЯ, а не до следующего прогона: иначе принятые решения
/// («это ошибка федерации, не чиним») пришлось бы принимать заново каждый раз. Поэтому таблица
/// не привязана к прогону — ключ находки это (CheckId, EntityType, EntityId).
/// </summary>
public class DataCheckFinding
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string CheckId { get; set; } = string.Empty;

    /// <summary>0 Info · 1 Warning · 2 Error (<c>DataCheckSeverity</c>).</summary>
    public int Severity { get; set; }

    [Required, MaxLength(50)]
    public string EntityType { get; set; } = string.Empty;

    public int? EntityId { get; set; }

    [Required, MaxLength(500)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Details { get; set; }

    /// <summary>Куда идти чинить — относительная ссылка админки.</summary>
    [MaxLength(300)]
    public string? Link { get; set; }

    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>null — открыта; fixed — исчезла сама; accepted — принята как есть.</summary>
    [MaxLength(20)]
    public string? Resolution { get; set; }

    public DateTime? ResolvedAt { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }
}
