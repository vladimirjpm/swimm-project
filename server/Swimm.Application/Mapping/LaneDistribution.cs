namespace Swimm.Application.Mapping;

/// <summary>
/// Общие записи раскладки по дорожкам (docs/plans/lane-plans-plan.md): дорожка, пловец, место.
/// Раскладывает <see cref="LaneAllocation"/> («Auto-arrange»), переливает <see cref="LaneOverflow"/>,
/// ставит по ответу <see cref="LanePlanPlacement"/>, вид «по дорожкам» — <see cref="TrainingLaneView"/>.
/// Прежняя раскладка «по дорожкам, заданным тренером» (Distribute) убрана в CF8 плана
/// trainings-coach-flow — её заменил Auto-arrange.
/// </summary>
public static class LaneDistribution
{
    public sealed record Lane(int LaneNo, int? LevelId);

    /// <summary>
    /// Пловец в порядке состава (<paramref name="SortKey"/> — место в этом порядке);
    /// <paramref name="SubLevel"/> — подуровень внутри уровня (1 — верхний), null — не задан.
    /// </summary>
    public sealed record Swimmer(int SwimmerId, int? LevelId, int SortKey, int? SubLevel = null);

    public sealed record Placement(int SwimmerId, int? LaneNo);
}
