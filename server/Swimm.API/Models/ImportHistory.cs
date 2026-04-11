using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.API.Models;

/// <summary>
/// История импортов данных. Хранит информацию о каждом импорте JSON-файлов.
/// </summary>
[Table("Sys_ImportHistory")]
public class ImportHistory
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Ссылка на соревнование</summary>
    public int CompetitionId { get; set; }

    [ForeignKey(nameof(CompetitionId))]
    public Competition? Competition { get; set; }

    /// <summary>Имя импортированного файла</summary>
    [Required, MaxLength(500)]
    public string ImportFileName { get; set; } = string.Empty;

    /// <summary>Дата и время импорта</summary>
    public DateTime ImportDate { get; set; } = DateTime.UtcNow;

    /// <summary>Подтверждён ли импорт после ручной проверки</summary>
    public bool Approved { get; set; } = false;
}
