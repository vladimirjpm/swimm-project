using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.API.Models;

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
}
