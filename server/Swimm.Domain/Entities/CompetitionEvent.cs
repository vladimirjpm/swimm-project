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

    /// <summary>
    /// compID соревнования на isr.org.il, к которому относится всё событие (Д2,
    /// docs/data-integrity.md). Нужен, чтобы переимпорт попадал в СВОИ дни, а не искал их
    /// по названию: названия расходятся («…חלק ב'») и однажды породили полный дубликат
    /// события на 1837 строк (инцидент И-3).
    ///
    /// Почему здесь, а не на каждом дне: <c>Competition.OrgCompId</c> — альтернативный
    /// ключ с UNIQUE-индексом, на него ссылается FK из <c>CompetitionResultUrls</c>;
    /// проставить один и тот же compID трём дням там нельзя. Уникальности НЕТ и здесь:
    /// на сайте одному файлу могут соответствовать две записи (6621 и 6622 → тот же
    /// протокол), и обе законно указывают на одно событие.
    /// </summary>
    public int? OrgCompId { get; set; }

    /// <summary>Дни этого события.</summary>
    public ICollection<Competition> Days { get; set; } = new List<Competition>();
}
