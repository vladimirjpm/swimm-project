using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.Domain.Entities;

/// <summary>
/// Тип тренировки — фиксированный список на весь сайт (решение Влада 01.10.2026; заменяет решение
/// 30.09 «отдельного типа нет»). Нужен ленте (фильтр, значок), видеоразбору пловца
/// (<see cref="Technique"/>) и разовым занятиям в Next. Название тренировки (<see cref="TrainingSession.Title"/>)
/// остаётся свободным текстом. Список меняется только кодом — и клиентским зеркалом
/// <c>training-types.ts</c>.
/// </summary>
public static class TrainingSessionType
{
    /// <summary>Обычная тренировка в воде — по умолчанию, у залитых скриптом тоже.</summary>
    public const string Swim = "swim";
    /// <summary>Техника / видеоразбор.</summary>
    public const string Technique = "technique";
    /// <summary>Суша, ОФП.</summary>
    public const string Dryland = "dryland";
    public const string OpenWater = "open-water";
    /// <summary>Контрольный сет / прикидка.</summary>
    public const string TestSet = "test-set";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All = [Swim, Technique, Dryland, OpenWater, TestSet, Other];

    public static bool IsKnown(string? type) => type != null && All.Contains(type);
}

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

    /// <summary>
    /// Название тренировки для людей («Underwater filming»). Заводит тренер кнопкой — тренировка
    /// может быть и без времён, а к ней видео (docs/plans/group-events-media-plan.md, Е1). У
    /// залитых скриптом сессий пусто — там говорит <see cref="Name"/> (план из источника).
    /// </summary>
    [MaxLength(100)]
    public string? Title { get; set; }

    /// <summary>Тип — <see cref="TrainingSessionType"/>; по умолчанию <see cref="TrainingSessionType.Swim"/>.</summary>
    [Required, MaxLength(20)]
    public string Type { get; set; } = TrainingSessionType.Swim;

    /// <summary>Начало «HH:mm» — связка с занятием расписания (ключ RSVP «дата + начало»); null — не задано.</summary>
    [MaxLength(5)]
    public string? StartTime { get; set; }

    /// <summary>Кто завёл кнопкой; null — залито скриптом (или аккаунт удалён — SetNull).</summary>
    public int? CreatedByUserId { get; set; }

    [ForeignKey(nameof(CreatedByUserId))]
    public AppUser? CreatedBy { get; set; }

    /// <summary>Когда заведена кнопкой; null — у залитых скриптом.</summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// Ключевой сет — ★ тренера (docs/plans/trainings-tab-redesign-plan.md, Р4): по нему строка
    /// «You» в ленте (быстрейший повтор «моих» пловцов в этом сете). null — не помечен, строки нет.
    /// В источнике Дельфина такого поля нет — метка только на сайте.
    /// </summary>
    public int? KeySetNo { get; set; }

    public ICollection<TrainingResult> Results { get; set; } = new List<TrainingResult>();
}
