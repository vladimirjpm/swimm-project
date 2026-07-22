using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Эстафета, связанная с ResultRecord.
/// </summary>
public class Relay
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [MaxLength(200)]
    public string? TeamName { get; set; }

    [MaxLength(500)]
    public string? SwimmersName { get; set; }

    /// <summary>Структурный состав ног (эстафета → пловцы). Текст <see cref="SwimmersName"/>
    /// остаётся для отображения; членство для запросов «заплывы пловца» — тут.</summary>
    public List<RelayMember> Members { get; set; } = new();
}
