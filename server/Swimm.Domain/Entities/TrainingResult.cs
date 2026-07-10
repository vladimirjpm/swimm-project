using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.Domain.Entities;

/// <summary>
/// Один тренировочный повтор (индивидуальный заплыв). ПРИВАТНЫЕ данные — таблица
/// <c>Sys_TrainingResults</c>, БЕЗ grant <c>swimm_ro</c>. В рекорды/зачёт/очки НЕ попадает
/// (для этого разделено с <see cref="ResultRecord"/>). См. hubgroups-architecture.md §7.
/// </summary>
[Index(nameof(SessionId))]
[Index(nameof(SwimmerId))]
public class TrainingResult
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int SessionId { get; set; }

    [ForeignKey(nameof(SessionId))]
    public TrainingSession? Session { get; set; }

    /// <summary>Пловец (публичная <see cref="Swimmer"/>, в т.ч. Origin='local'). Приватны времена, не сам пловец.</summary>
    public int SwimmerId { get; set; }

    [ForeignKey(nameof(SwimmerId))]
    public Swimmer? Swimmer { get; set; }

    public int StyleId { get; set; }

    [ForeignKey(nameof(StyleId))]
    public Style? Style { get; set; }

    /// <summary>Дистанция: 25, 50, 100, 200, 400 …</summary>
    [MaxLength(20)]
    public string Distance { get; set; } = string.Empty;

    /// <summary>male / female (из event_style_gender записи).</summary>
    [MaxLength(10)]
    public string Gender { get; set; } = string.Empty;

    /// <summary>Время в миллисекундах (для сравнения/сортировки).</summary>
    public int? TimeMillisecond { get; set; }

    /// <summary>Оригинальная строка времени из источника (как введено).</summary>
    [MaxLength(20)]
    public string TimeOriginal { get; set; } = string.Empty;

    // ── Тренировочные атрибуты (нет у соревнований) ────────────────────────────

    /// <summary>Номер сета внутри тренировки (training.set).</summary>
    public int SetNo { get; set; }

    /// <summary>Порядок повтора внутри сета (training.order).</summary>
    public int OrderNo { get; set; }

    /// <summary>Интервал отдыха/старта, сек (training.interval).</summary>
    public int? IntervalSec { get; set; }

    /// <summary>Интенсивность: v1..v5 (training.intensity).</summary>
    [MaxLength(10)]
    public string? Intensity { get; set; }

    /// <summary>Плыл с лопатками (training.isPaddles).</summary>
    public bool IsPaddles { get; set; }

    /// <summary>Плыл с колобашкой (training.isBuoy).</summary>
    public bool IsBuoy { get; set; }

    /// <summary>Плановое время, мс (training.expected_time).</summary>
    public int? ExpectedTimeMs { get; set; }
}
