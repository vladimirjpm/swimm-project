using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

public class UserMedia
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public AppUser User { get; set; } = null!;

    /// <summary>Обязателен — рычаг «стереть всё медиа по пловцу».</summary>
    public int SwimmerId { get; set; }

    [ForeignKey(nameof(SwimmerId))]
    public Swimmer Swimmer { get; set; } = null!;

    public long? ResultId { get; set; }

    [ForeignKey(nameof(ResultId))]
    public ResultRecord? ResultRecord { get; set; }

    public int? CompetitionId { get; set; }

    [ForeignKey(nameof(CompetitionId))]
    public Competition? Competition { get; set; }

    /// <summary>swimmer / competition / result</summary>
    [Required, MaxLength(20)]
    public string Level { get; set; } = string.Empty;

    /// <summary>image / video</summary>
    [Required, MaxLength(20)]
    public string MediaType { get; set; } = string.Empty;

    /// <summary>youtube / vimeo / other</summary>
    [Required, MaxLength(20)]
    public string SourceType { get; set; } = string.Empty;

    [Required, MaxLength(1000)]
    public string Url { get; set; } = string.Empty;

    /// <summary>private / public (дефолт private)</summary>
    [Required, MaxLength(20)]
    public string Visibility { get; set; } = "private";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
