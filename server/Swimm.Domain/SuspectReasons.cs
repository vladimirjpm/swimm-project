namespace Swimm.Domain;

/// <summary>
/// Коды причин, по которым результат помечен как недостоверный
/// (<see cref="Entities.ResultRecord.SuspectReason"/>).
///
/// Проверки ищут ошибки САМОГО источника — то, что нельзя ни починить парсером, ни
/// выбросить: протокол напечатан так, как напечатан. Помеченная строка остаётся в
/// результатах, но не участвует в детекции рекордов.
/// </summary>
public static class SuspectReasons
{
    /// <summary>Время резко выбивается из своей дисциплины (медиана заплыва).</summary>
    public const string TimeOutlier = "time_outlier";

    /// <summary>Время невозможно для дистанции — быстрее мирового рекорда.</summary>
    public const string TimeVsDistance = "time_vs_distance";

    /// <summary>В личном заплыве время уровня одной ноги эстафеты.</summary>
    public const string RelayTimeInIndividual = "relay_time_in_individual";

    /// <summary>Пол результата расходится с полом пловца по остальным его заплывам.</summary>
    public const string GenderMismatch = "gender_mismatch";

    /// <summary>Пловец дважды в одной дисциплине одного дня с разным временем.</summary>
    public const string DuplicateSwim = "duplicate_swim";

    /// <summary>
    /// Заплыв не сходится с СОБСТВЕННЫМИ результатами пловца за тот же период.
    /// Ловит ошибки внутри порогов остальных правил: 200 вольным за 1:53.09 у пловца,
    /// чей стольник 1:05.05, рекорда не бьёт и от медианы заплыва недалеко — но так не плавают.
    /// </summary>
    public const string PersonalOutlier = "personal_outlier";

    /// <summary>Поставлено человеком. Переживает переимпорт (в отличие от автоматических).</summary>
    public const string Manual = "manual";

    /// <summary>Все коды автопроверок — то, что пересчитывается при каждом прогоне.</summary>
    public static readonly string[] Automatic =
    [
        TimeOutlier, TimeVsDistance, RelayTimeInIndividual, GenderMismatch, DuplicateSwim,
        PersonalOutlier
    ];
}
