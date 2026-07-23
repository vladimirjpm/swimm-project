using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.Domain.Entities;

/// <summary>
/// Справочник стилей плавания (обычно 5–7 записей).
/// </summary>
[Index(nameof(Name), IsUnique = true)]
public class Style
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>freestyle, backstroke, breaststroke, butterfly, individual_medley, etc.</summary>
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Посевные стили (id 1–7): их имена зашиты в парсер, импорт (NormalizeStyleName),
    /// рекорды и подсчёт очков. Переименование сломает эту логику незаметно, а удаление
    /// оборвёт FK результатов — поэтому у зарезервированных стилей блокируем rename/delete
    /// (по аналогии с <see cref="Category.ReservedKeys"/>).
    /// </summary>
    public static readonly IReadOnlySet<string> ReservedNames = new HashSet<string>
    {
        "freestyle", "backstroke", "breaststroke", "butterfly",
        "individual_medley", "medley_relay", "free_relay"
    };
}
