using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.Domain.Entities;

/// <summary>
/// Нога эстафеты: структурная связь <see cref="Relay"/> → <see cref="Swimmer"/>.
/// Раньше состав жил только текстом в <see cref="Relay.SwimmersName"/>, а сам
/// эстафетный результат был привязан к одному пловцу (первой ноге) — поэтому
/// эстафета показывалась лишь «владельцу» строки. Эта таблица делает членство
/// явным: результат виден всем участникам (My media, карточка спортсмена).
/// </summary>
[Index(nameof(SwimmerId))]
[Index(nameof(RelayId), nameof(SwimmerId), IsUnique = true)]
public class RelayMember
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int RelayId { get; set; }

    [ForeignKey(nameof(RelayId))]
    public Relay Relay { get; set; } = null!;

    public int SwimmerId { get; set; }

    [ForeignKey(nameof(SwimmerId))]
    public Swimmer Swimmer { get; set; } = null!;

    /// <summary>Порядок ноги (1..4). 0 — если из источника не восстановлен.</summary>
    public int LegOrder { get; set; }

    /// <summary>Сплит ноги как в протоколе (напр. "00:29.14"), если есть.</summary>
    [MaxLength(50)]
    public string? SplitTime { get; set; }
}
