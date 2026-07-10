using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.Domain.Entities;

/// <summary>
/// Тренировочная сессия группы (SwimHub). ПРИВАТНЫЕ данные — таблица <c>Sys_TrainingSessions</c>,
/// БЕЗ grant <c>swimm_ro</c>: видят только владелец/админ группы и (в перспективе) привязанный пловец.
/// Соревнования тут НЕ хранятся — только тренировки. См. hubgroups-architecture.md §7.
/// </summary>
[Index(nameof(HubGroupId), nameof(ExternalTrainingId), IsUnique = true)]
public class TrainingSession
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Группа, которой принадлежит тренировка (обязательна — определяет, чей тренер вправе видеть).</summary>
    public int HubGroupId { get; set; }

    [ForeignKey(nameof(HubGroupId))]
    public HubGroup? HubGroup { get; set; }

    /// <summary>Внешний ID тренировки из исходника (напр. «20251028»); натуральный ключ сессии в группе.</summary>
    [MaxLength(50)]
    public string ExternalTrainingId { get; set; } = string.Empty;

    /// <summary>План тренировки как в источнике (напр. «200-&gt;2x100-&gt;4x50»). Может быть пустым.</summary>
    [MaxLength(300)]
    public string? Name { get; set; }

    /// <summary>Дата тренировки.</summary>
    public DateTime Date { get; set; }

    /// <summary>Тип бассейна: 25m / 50m.</summary>
    [MaxLength(5)]
    public string PoolType { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Note { get; set; }

    public ICollection<TrainingResult> Results { get; set; } = new List<TrainingResult>();
}
