namespace Swimm.Domain.Entities;

/// <summary>
/// История прогонов фоновой задачи (<c>Sys_JobRunLog</c>, страница /Admin/Jobs): когда шла,
/// чем кончилась и ЧТО сделала. <see cref="JobRun"/> держит одну строку на задачу — по ней
/// решается «пора ли»; здесь — строка на каждый прогон, для человека.
///
/// Растёт ограниченно: после записи у задачи остаются последние <c>JobRunLog.KeepPerJob</c>
/// строк (<c>JobRunStore</c>). Sys_, приватная, БЕЗ гранта swimm_ro и НЕ в seed-tables — у
/// каждой базы свой журнал, как у <see cref="JobRun"/>.
/// </summary>
public class JobRunLogEntry
{
    public long Id { get; set; }

    /// <summary>Имя задачи — константа <c>JobNames</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Кто запустил — <c>JobTriggers</c>: schedule / manual / cli (CHECK в базе).</summary>
    public string Trigger { get; set; } = "schedule";

    /// <summary>Начало прогона (UTC).</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>Конец прогона (UTC).</summary>
    public DateTime FinishedAt { get; set; }

    public bool Success { get; set; }

    /// <summary>Итог одной строкой: «на сайте 41, новых 2, перенесено 1…». null — задача итога не дала.</summary>
    public string? Summary { get; set; }

    /// <summary>Текст ошибки у неудачного прогона.</summary>
    public string? Error { get; set; }
}
