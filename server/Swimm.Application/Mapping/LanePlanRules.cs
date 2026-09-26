using System.Globalization;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Правила плана дорожек (docs/plans/lane-plans-plan.md, L2) — одно место, без БД: дата в
/// адресе, границы, форма плана. Что уровни и пловцы принадлежат группе — проверяет сервис
/// (нужна БД); ошибки — видимый UI, по-английски.
/// </summary>
public static class LanePlanRules
{
    public const int MinLanes = 1;
    public const int MaxLanes = 12;
    public const int NoteMaxLength = 500;
    public const int WorkoutMaxLength = 4000;
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>Дата плана из адреса: строго yyyy-MM-dd, разумный диапазон.</summary>
    public static bool TryParseDate(string? raw, out DateOnly date) =>
        DateOnly.TryParseExact(raw, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
        && date.Year is >= 2000 and <= 2100;

    public static string FormatDate(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    public sealed record NormalizedLane(int LaneNo, int? LevelId, string? Workout);

    /// <summary>Пловец плана: <c>OrderNo</c> — место в своей дорожке (или среди Unassigned).</summary>
    public sealed record NormalizedSwimmer(int SwimmerId, int? LaneNo, int OrderNo);

    public sealed record NormalizedPlan(
        int LaneCount, string? Note, IReadOnlyList<NormalizedLane> Lanes, IReadOnlyList<NormalizedSwimmer> Swimmers);

    /// <summary>
    /// Проверить план и привести к полному виду: дорожки 1..LaneCount все (недостающие — пустые),
    /// порядок внутри дорожки — порядок пловцов во входе.
    /// </summary>
    public static (NormalizedPlan? Plan, string? Error) Normalize(LanePlanInputDto? input)
    {
        if (input == null) return (null, "Plan is empty.");

        var laneError = CheckLaneCount(input.LaneCount);
        if (laneError != null) return (null, laneError);

        var note = input.Note?.Trim();
        if (string.IsNullOrEmpty(note)) note = null;
        else if (note.Length > NoteMaxLength) return (null, $"Note is longer than {NoteMaxLength} characters.");

        var (lanes, lanesError) = NormalizeLanes(input.LaneCount, input.Lanes);
        if (lanes == null) return (null, lanesError);

        var swimmers = new List<NormalizedSwimmer>(input.Swimmers?.Count ?? 0);
        var seen = new HashSet<int>();
        var orderInLane = new Dictionary<int, int>();   // ключ 0 — Unassigned
        foreach (var s in input.Swimmers ?? [])
        {
            if (!seen.Add(s.SwimmerId)) return (null, "The same swimmer is listed twice.");
            if (s.LaneNo is int no && (no < 1 || no > input.LaneCount))
                return (null, $"Lane {no} is not in this plan.");

            var key = s.LaneNo ?? 0;
            var order = orderInLane.GetValueOrDefault(key);
            orderInLane[key] = order + 1;
            swimmers.Add(new NormalizedSwimmer(s.SwimmerId, s.LaneNo, order));
        }

        return (new NormalizedPlan(input.LaneCount, note, lanes, swimmers), null);
    }

    /// <summary>Дорожки «Distribute»/плана: номера в 1..LaneCount без повторов, задание ≤ лимита.</summary>
    public static (IReadOnlyList<NormalizedLane>? Lanes, string? Error) NormalizeLanes(
        int laneCount, IReadOnlyList<LanePlanLaneInputDto>? input)
    {
        var laneError = CheckLaneCount(laneCount);
        if (laneError != null) return (null, laneError);

        var byNo = new Dictionary<int, LanePlanLaneInputDto>();
        foreach (var lane in input ?? [])
        {
            if (lane.LaneNo < 1 || lane.LaneNo > laneCount) return (null, $"Lane {lane.LaneNo} is not in this plan.");
            if (!byNo.TryAdd(lane.LaneNo, lane)) return (null, $"Lane {lane.LaneNo} is listed twice.");
        }

        var result = new List<NormalizedLane>(laneCount);
        for (var no = 1; no <= laneCount; no++)
        {
            byNo.TryGetValue(no, out var lane);
            var workout = lane?.Workout?.Trim();
            if (string.IsNullOrEmpty(workout)) workout = null;
            else if (workout.Length > WorkoutMaxLength)
                return (null, $"Workout of lane {no} is longer than {WorkoutMaxLength} characters.");
            result.Add(new NormalizedLane(no, lane?.LevelId, workout));
        }
        return (result, null);
    }

    private static string? CheckLaneCount(int laneCount) =>
        laneCount is < MinLanes or > MaxLanes ? $"Lanes: from {MinLanes} to {MaxLanes}." : null;
}
