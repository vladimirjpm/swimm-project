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
    public const int MinPerLane = 1;
    public const int MaxPerLane = 20;
    /// <summary>Запасных дорожек — не больше, чем «все, кроме одной» при самом большом бассейне.</summary>
    public const int MaxReservedLanes = MaxLanes - 1;
    public const int NoteMaxLength = 500;
    public const int WorkoutMaxLength = 4000;
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>Дата плана из адреса: строго yyyy-MM-dd, разумный диапазон.</summary>
    public static bool TryParseDate(string? raw, out DateOnly date) =>
        DateOnly.TryParseExact(raw, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
        && date.Year is >= 2000 and <= 2100;

    public static string FormatDate(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Отказ в публикации (решение 10 плана trainings-coach-flow): «Tom and Niv are going but have
    /// no level and no lane…». Имён больше трёх — «Tom, Niv, Dan and 2 more».
    /// </summary>
    public static string PublishBlockedMessage(IReadOnlyList<string> names)
    {
        const int shown = 3;
        var head = names.Take(shown).ToList();
        var rest = names.Count - head.Count;
        var list = rest > 0 ? $"{string.Join(", ", head)} and {rest} more"
            : head.Count == 1 ? head[0]
            : $"{string.Join(", ", head.Take(head.Count - 1))} and {head[^1]}";
        var verb = names.Count == 1 ? "is going but has" : "are going but have";
        return $"{list} {verb} no level and no lane. Set a level or put them on a lane to publish.";
    }

    /// <summary>
    /// Push участникам об опубликованном плане (CF3): заголовок — название группы, текст — «Lanes
    /// for Sat, Nov 14, 10:00 are ready» (с CF5 план — у занятия, а занятий в день бывает два).
    /// Адрес зеркалит <c>routes.group(slug)</c> клиента + <c>?tab=</c>; тег — ключ плана: повторная
    /// публикация заменяет уведомление, а не копит.
    /// </summary>
    public static (string Title, string Body, string Url, string Tag) PublishedPush(
        int hubGroupId, string groupName, string slug, LanePlanKey key) =>
        (groupName,
         $"Lanes for {key.Date.ToString("ddd, MMM d", CultureInfo.InvariantCulture)}{(key.Start == null ? "" : $", {key.Start}")} are ready",
         $"/groups/{Uri.EscapeDataString(slug)}?tab=trainings",
         $"lanes-{hubGroupId}-{key}");

    /// <summary>Отказ в записи плана дня без занятия (CF5): такой план — архив только для чтения.</summary>
    public const string ReadOnlyDayPlanMessage =
        "This plan belongs to a day without a training and is read-only.";

    public sealed record NormalizedLane(int LaneNo, int? LevelId, string? Workout);

    /// <summary>
    /// Пловец плана: <c>OrderNo</c> — место в своей дорожке (или среди Unassigned);
    /// <c>Pinned</c> — закреплён на дорожке («Auto-arrange» не двигает).
    /// </summary>
    public sealed record NormalizedSwimmer(int SwimmerId, int? LaneNo, int OrderNo, bool Pinned = false);

    public sealed record NormalizedPlan(
        int LaneCount, int? MaxPerLane, string? Note, IReadOnlyList<NormalizedLane> Lanes, IReadOnlyList<NormalizedSwimmer> Swimmers,
        int ReservedLanes = LanePlanDefaults.ReservedLanes);

    /// <summary>
    /// Проверить план и привести к полному виду: дорожки 1..LaneCount все (недостающие — пустые),
    /// порядок внутри дорожки — порядок пловцов во входе.
    /// </summary>
    public static (NormalizedPlan? Plan, string? Error) Normalize(LanePlanInputDto? input)
    {
        if (input == null) return (null, "Plan is empty.");

        var laneError = CheckLaneCount(input.LaneCount);
        if (laneError != null) return (null, laneError);

        var perLaneError = CheckMaxPerLane(input.MaxPerLane);
        if (perLaneError != null) return (null, perLaneError);

        var reservedError = CheckReservedLanes(input.ReservedLanes);
        if (reservedError != null) return (null, reservedError);

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
            // Закрепление — только на дорожке (CF2).
            swimmers.Add(new NormalizedSwimmer(s.SwimmerId, s.LaneNo, order, s.Pinned && s.LaneNo != null));
        }

        return (new NormalizedPlan(input.LaneCount, input.MaxPerLane, note, lanes, swimmers, input.ReservedLanes), null);
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

    /// <summary>Лимит людей на дорожку (перелив по подуровням): null — нет лимита, иначе 1..20.</summary>
    public static string? CheckMaxPerLane(int? maxPerLane) =>
        maxPerLane is < MinPerLane or > MaxPerLane
            ? $"Max per lane: from {MinPerLane} to {MaxPerLane}, or no limit."
            : null;

    /// <summary>
    /// Запасных дорожек: 0..11. Больше, чем дорожек в плане, — не ошибка: раскладка возьмёт столько,
    /// сколько можно (число дорожек тренер меняет отдельно).
    /// </summary>
    public static string? CheckReservedLanes(int reservedLanes) =>
        reservedLanes is < 0 or > MaxReservedLanes ? $"Reserve lanes: from 0 to {MaxReservedLanes}." : null;

    private static string? CheckLaneCount(int laneCount) =>
        laneCount is < MinLanes or > MaxLanes ? $"Lanes: from {MinLanes} to {MaxLanes}." : null;
}
