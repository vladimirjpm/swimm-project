namespace Swimm.Domain.Entities;

/// <summary>
/// Журнал запусков фоновой задачи по расписанию (решение В12, docs/plans/competitions-home-plan.md,
/// часть В): одна строка на задачу. Раньше «последний запуск» жил в памяти процесса, и каждый
/// перезапуск API тут же шёл на isr.org.il и loglig заново.
///
/// Sys_, приватная, БЕЗ гранта swimm_ro и НЕ в seed-tables: у каждой базы свой журнал — прод
/// пишет ProdAdmin, локальную — локальный процесс. Правило «пора ли» — <c>JobSchedule.IsDue</c>.
/// </summary>
public class JobRun
{
    /// <summary>Имя задачи — константа <c>JobNames</c> (<c>discovery</c>, <c>start-list</c>…). PK.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Начало последней попытки (успешной или нет).</summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>Начало последней УСПЕШНОЙ попытки; от неё отсчитывается интервал.</summary>
    public DateTime? LastSuccessAt { get; set; }

    /// <summary>Текст ошибки последней попытки; null — последняя попытка удалась.</summary>
    public string? LastError { get; set; }
}
