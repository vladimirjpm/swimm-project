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

    /// <summary>
    /// Сразу несколько дорожек одного заплыва проплыли намного быстрее своих заявочных
    /// времён — след сбоя хронометража заплыва, а не прогресса пловцов (И-55: comp 1772,
    /// 200 вольным, пять дорожек на 14–29% быстрее заявок 2:26–2:27).
    /// </summary>
    public const string SeedTimeJump = "seed_time_jump";

    /// <summary>
    /// Протокол напечатал МЕСТО у строки без времени и без статуса (DQ/NS/DNF) — время не
    /// записали. Место оставляем, как напечатано, строка с чипом «Under review» (Р90,
    /// решение Влада 07.11.2026). 16891, 50 вольным мальчики 13: двое «00:00.00» на 1-м месте.
    /// </summary>
    public const string PlacedWithoutTime = "placed_without_time";

    /// <summary>Поставлено человеком. Переживает переимпорт (в отличие от автоматических).</summary>
    public const string Manual = "manual";

    /// <summary>Все коды автопроверок — то, что пересчитывается при каждом прогоне.</summary>
    public static readonly string[] Automatic =
    [
        TimeOutlier, TimeVsDistance, RelayTimeInIndividual, GenderMismatch, DuplicateSwim,
        PersonalOutlier, SeedTimeJump, PlacedWithoutTime
    ];

    /// <summary>
    /// Строка «место без времени» (Р90): личная, без времени, без статуса снятия, но с местом
    /// в протоколе. Единственное определение — его зовут импорт, проверка качества и сверка
    /// мест с loglig. DQ/NS/DNS/DNF несут статус, и их номер в протоколе местом не является (И-18).
    /// </summary>
    public static bool IsPlacedWithoutTime(bool isRelay, int? timeMs, string? failNote, int? position) =>
        !isRelay && timeMs is null or <= 0 && string.IsNullOrWhiteSpace(failNote) && position is > 0;
}
