using Swimm.Domain;

namespace Swimm.Tests;

/// <summary>
/// Расписания тестовых групп. С CF5 плана trainings-coach-flow план дорожек заводится только у
/// существующего занятия — тестам плана нужна группа, у которой занятие есть в любой день.
/// </summary>
internal static class TestSchedules
{
    /// <summary>JSON колонки <c>HubGroups.TrainingSchedule</c>: занятие каждый день в <paramref name="starts"/>.</summary>
    public static string EveryDay(params string[] starts)
    {
        if (starts.Length == 0) starts = ["20:00"];
        return new GroupTrainingSchedule
        {
            Slots = Enumerable.Range(1, 7)
                .SelectMany(day => starts.Select(start => new GroupTrainingSlot { Day = day, Start = start }))
                .ToList(),
        }.ToJson();
    }
}
