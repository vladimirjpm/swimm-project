using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Логическое соревнование-«событие», объединяющее несколько дней (<see cref="Competition"/>).
/// Однодневные соревнования события не имеют (Competition.EventId == null).
/// </summary>
public class CompetitionEvent
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Общее имя соревнования (видит пользователь). Может задаваться вручную при импорте.</summary>
    [MaxLength(300)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Дата первого дня. Авто-пересчёт = min(дней) при добавлении дня.</summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>Дата последнего дня. Авто-пересчёт = max(дней) при добавлении дня.</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>Дни этого события.</summary>
    public ICollection<Competition> Days { get; set; } = new List<Competition>();
}
