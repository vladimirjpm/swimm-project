using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>Прогон реестра проверок (docs/data-integrity.md, фаза Д3).</summary>
public class DataCheckRun
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }

    /// <summary>manual | import | schedule — кто запустил.</summary>
    [Required, MaxLength(20)]
    public string Trigger { get; set; } = "manual";

    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }

    /// <summary>Находок, исчезнувших с прошлого прогона (данные починены).</summary>
    public int FixedCount { get; set; }
}

/// <summary>
/// Текущее состояние ОДНОЙ проверки — итог её последнего прогона (Д3/Д5).
///
/// Зачем отдельная таблица, если есть находки: список находок капнут (проверка, нашедшая
/// 8071 группу, кладёт 50), а дашборду нужно ПОЛНОЕ число. Считать его по таблице находок
/// нельзя — оно там физически не хранится. Плюс одна строка на проверку это ровно тот
/// дешёвый запрос, который дашборду и нужен, вместо перебора находок.
/// </summary>
public class DataCheckState
{
    [Key]
    [MaxLength(100)]
    public string CheckId { get; set; } = string.Empty;

    /// <summary>0 Info · 1 Warning · 2 Error (<c>DataCheckSeverity</c>).</summary>
    public int Severity { get; set; }

    /// <summary>Сколько проверка нашла на последнем прогоне — ПОЛНОЕ число, без среза.</summary>
    public int Total { get; set; }

    /// <summary>Сколько находок реально записано (≤ Total, срез списка). Shown &lt; Total — «показано N из M».</summary>
    public int Shown { get; set; }

    /// <summary>true — проверка упала на последнем прогоне, число в Total ничего не значит.</summary>
    public bool Failed { get; set; }

    public int LastRunId { get; set; }
    public DateTime LastRunAt { get; set; }
}

/// <summary>
/// Находка проверки. Живёт ДО УСТРАНЕНИЯ, а не до следующего прогона: иначе принятые решения
/// («это ошибка федерации, не чиним») пришлось бы принимать заново каждый раз. Поэтому таблица
/// не привязана к прогону — ключ находки это (CheckId, EntityType, EntityId).
/// </summary>
public class DataCheckFinding
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string CheckId { get; set; } = string.Empty;

    /// <summary>0 Info · 1 Warning · 2 Error (<c>DataCheckSeverity</c>).</summary>
    public int Severity { get; set; }

    [Required, MaxLength(50)]
    public string EntityType { get; set; } = string.Empty;

    public int? EntityId { get; set; }

    [Required, MaxLength(500)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Details { get; set; }

    /// <summary>Куда идти чинить — относительная ссылка админки.</summary>
    [MaxLength(300)]
    public string? Link { get; set; }

    /// <summary>
    /// Имя субъекта находки отдельным полем — чтобы его можно было скопировать одним кликом
    /// (найти пловца в интернете, написать в клуб). В <c>Message</c> оно вплавлено в текст
    /// вместе с дисциплиной и временем, и выделять его мышью из строки — мучение.
    /// </summary>
    [MaxLength(200)]
    public string? SubjectName { get; set; }

    /// <summary>
    /// Точечное исправление прямо из списка находок: код действия (напр.
    /// <c>swimmer-gender</c>) и id сущности, к которой оно применяется. null — чинить надо
    /// руками на своей странице.
    ///
    /// ⚠ Реестр остаётся ЧИТАЮЩИМ: он не чинит сам, а лишь даёт кнопку человеку. Действие
    /// выполняет отдельный эндпоинт со своим аудитом.
    /// </summary>
    [MaxLength(40)]
    public string? FixKind { get; set; }

    public int? FixEntityId { get; set; }

    /// <summary>
    /// Куда идти смотреть глазами — относительный путь публичной страницы
    /// (контракт client/src/utils/routes.ts). Именно относительный: базу подставляет
    /// страница, иначе dev-адрес осел бы в данных.
    /// </summary>
    [MaxLength(300)]
    public string? PublicLink { get; set; }

    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>null — открыта; fixed — исчезла сама; accepted — принята как есть.</summary>
    [MaxLength(20)]
    public string? Resolution { get; set; }

    public DateTime? ResolvedAt { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }
}
