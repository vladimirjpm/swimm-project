using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.API.Models;

/// <summary>
/// Денормализованная запись результата.
/// Содержит копии ключевых полей и FK к справочникам для JOIN с сущностями (Competition, Swimmer, Club, Relay, Style, Gallery).
/// Поля CompetitionDate, Distance и Gender хранятся inline для быстрых выборок и фильтрации.
/// </summary>
[Index(nameof(CompetitionId))]
[Index(nameof(SwimmerId))]
[Index(nameof(StyleId), nameof(Distance))]
[Index(nameof(CompetitionId), nameof(StyleId), nameof(Distance), nameof(Gender))]
[Index(nameof(CompetitionId), nameof(TimeMillisecond))]
[Index(nameof(SwimmerId), nameof(TimeMillisecond))]
[Index(nameof(CompetitionDate))]
public class ResultRecord
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /* === FK-ссылки на справочники === */

    public int CompetitionId { get; set; }

    [ForeignKey(nameof(CompetitionId))]
    public Competition Competition { get; set; } = null!;

    public int SwimmerId { get; set; }

    [ForeignKey(nameof(SwimmerId))]
    public Swimmer Swimmer { get; set; } = null!;

    public int ClubId { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club Club { get; set; } = null!;

    public int StyleId { get; set; }

    [ForeignKey(nameof(StyleId))]
    public Style Style { get; set; } = null!;

    public int? RelayId { get; set; }

    [ForeignKey(nameof(RelayId))]
    public Relay? Relay { get; set; }

    public int? GalleryId { get; set; }

    [ForeignKey(nameof(GalleryId))]
    public Gallery? Gallery { get; set; }

    public int? CountryId { get; set; }

    [ForeignKey(nameof(CountryId))]
    public Country? Country { get; set; }

    /* === Inline-поля для быстрых выборок === */

    public DateTime CompetitionDate { get; set; }

    /// <summary>50, 100, 200, 400, 800, 1500, 4x100, etc.</summary>
    [MaxLength(20)]
    public string Distance { get; set; } = string.Empty;

    /// <summary>male, female, mix</summary>
    [MaxLength(10)]
    public string Gender { get; set; } = string.Empty;

    /* === Поля результата === */

    [MaxLength(50)]
    public string AgeGroup { get; set; } = string.Empty;

    [MaxLength(20)]
    public string EventStyleAge { get; set; } = string.Empty;

    public int? Position { get; set; }

    public int? PositionAgeGroup { get; set; }

    public int Heat { get; set; }

    public int Lane { get; set; }

    /// <summary>Время в миллисекундах (единый формат для сравнения и сортировки).</summary>
    public int? TimeMillisecond { get; set; }

    /// <summary>Оригинальная строка времени из протокола (как напечатано в источнике).</summary>
    [MaxLength(20)]
    public string TimeOriginal { get; set; } = string.Empty;

    [MaxLength(50)]
    public string TimeSplit { get; set; } = string.Empty;

    public bool TimeFail { get; set; }

    [MaxLength(100)]
    public string? TimeFailNote { get; set; }

    public int InternationalPoints { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }
}
