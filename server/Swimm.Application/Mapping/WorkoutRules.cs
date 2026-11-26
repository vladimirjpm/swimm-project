using System.Text.Json;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Правила workout занятия (docs/plans/trainings-coach-flow-plan.md, §5.5, CF6) — без БД: границы
/// документа, приведение к чистому виду, хранение строкой JSON и «версия уровня» для участника.
/// Что id уровней принадлежат группе — проверяет сервис. Ошибки — видимый UI, по-английски.
/// Итоговые метры здесь не считаются (решение: считает клиент, сервер их не хранит).
/// </summary>
public static class WorkoutRules
{
    public const int MaxSets = 30;
    public const int MaxLinesPerSet = 40;
    public const int MaxRepeat = 20;
    public const int MaxReps = 99;
    public const int MaxDistance = 10000;
    public const int NoteMaxLength = 1000;
    public const int TitleMaxLength = 200;
    public const int TextMaxLength = 500;
    public const int IntensityMaxLength = 12;
    public const int SendOffMaxLength = 24;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>
    /// Проверить и привести к чистому виду: обрезать пробелы, пустое — null, пустые ячейки уровней
    /// выбросить. Пустой документ (ни заметки, ни сетов) — null: workout стёрт.
    /// </summary>
    public static (WorkoutDto? Workout, string? Error) Normalize(WorkoutDto? input)
    {
        if (input == null) return (null, null);

        var note = Clean(input.Note);
        if (note?.Length > NoteMaxLength) return (null, $"Workout note is longer than {NoteMaxLength} characters.");

        var sets = input.Sets ?? [];
        if (sets.Count > MaxSets) return (null, $"A workout has at most {MaxSets} sets.");

        var result = new WorkoutDto { Note = note };
        for (var i = 0; i < sets.Count; i++)
        {
            var set = sets[i];
            var label = $"Set {i + 1}";
            var title = Clean(set.Title);
            if (title?.Length > TitleMaxLength) return (null, $"{label}: the title is longer than {TitleMaxLength} characters.");
            if (set.Repeat is < 1 or > MaxRepeat) return (null, $"{label}: repeat from 1 to {MaxRepeat}.");

            var lines = set.Lines ?? [];
            if (lines.Count > MaxLinesPerSet) return (null, $"{label}: at most {MaxLinesPerSet} lines.");

            var clean = new WorkoutSetDto
            {
                Title = title,
                Repeat = set.Repeat,
                // Пустой список галочек — «никому»: так не бывает осмысленно, но хранится как есть.
                LevelIds = set.LevelIds?.Distinct().ToList(),
                Main = set.Main,
            };
            foreach (var line in lines)
            {
                var (cleanLine, error) = NormalizeLine(line, label);
                if (cleanLine == null) return (null, error);
                clean.Lines.Add(cleanLine);
            }
            result.Sets.Add(clean);
        }

        return result.Note == null && result.Sets.Count == 0 ? (null, null) : (result, null);
    }

    private static (WorkoutLineDto? Line, string? Error) NormalizeLine(WorkoutLineDto line, string label)
    {
        if (line.Reps is < 1 or > MaxReps) return (null, $"{label}: reps from 1 to {MaxReps}.");
        if (line.Distance is < 0 or > MaxDistance) return (null, $"{label}: distance from 0 to {MaxDistance} m.");

        var text = Clean(line.Text);
        if (text?.Length > TextMaxLength) return (null, $"{label}: a line is longer than {TextMaxLength} characters.");
        var intensity = Clean(line.Intensity);
        if (intensity?.Length > IntensityMaxLength) return (null, $"{label}: intensity is longer than {IntensityMaxLength} characters.");

        Dictionary<int, WorkoutCellDto>? perLevel = null;
        foreach (var (levelId, cell) in line.PerLevel ?? [])
        {
            if (cell == null) continue;
            var sendOff = Clean(cell.SendOff);
            if (sendOff?.Length > SendOffMaxLength) return (null, $"{label}: a send-off is longer than {SendOffMaxLength} characters.");
            if (cell.Reps is < 1 or > MaxReps) return (null, $"{label}: reps from 1 to {MaxReps}.");
            if (sendOff == null && cell.Reps == null) continue;
            perLevel ??= [];
            perLevel[levelId] = new WorkoutCellDto { SendOff = sendOff, Reps = cell.Reps };
        }

        return (new WorkoutLineDto
        {
            Reps = line.Reps,
            Distance = line.Distance == 0 ? null : line.Distance,
            Text = text,
            Intensity = intensity,
            PerLevel = perLevel,
        }, null);
    }

    /// <summary>Все id уровней, которые документ называет (галочки сетов и ключи ячеек).</summary>
    public static IReadOnlySet<int> LevelIdsUsed(WorkoutDto workout) =>
        workout.Sets.SelectMany(s => (s.LevelIds ?? []).Concat(s.Lines.SelectMany(l => l.PerLevel?.Keys ?? Enumerable.Empty<int>())))
            .ToHashSet();

    public static string Serialize(WorkoutDto workout) => JsonSerializer.Serialize(workout, Json);

    /// <summary>Хранимая строка → документ; битая или пустая — null (план без workout).</summary>
    public static WorkoutDto? Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        try
        {
            return JsonSerializer.Deserialize<WorkoutDto>(stored, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Версия уровня — то, что получает участник (§5.5: «сервер отдаёт только версию уровня его
    /// дорожки»): сеты, отмеченные для этого уровня (или для всех), в ячейках — только его время.
    /// <paramref name="levelId"/> null (дорожка без уровня) — только сеты для всех, без времён.
    /// Ничего не осталось — null.
    /// </summary>
    public static WorkoutDto? ForLevel(WorkoutDto? workout, int? levelId)
    {
        if (workout == null) return null;

        // Сет без названия получает «Set N» по номеру у ТРЕНЕРА: уровень, который пропускает сет 2,
        // видит дальше «Set 3», а не «Set 2» — номер совпадает с листом и с тем, что говорит тренер.
        var sets = workout.Sets
            .Select((s, i) => (Set: s, No: i + 1))
            .Where(x => x.Set.LevelIds == null || (levelId is int id && x.Set.LevelIds.Contains(id)))
            .Select(x => new WorkoutSetDto
            {
                Title = x.Set.Title ?? $"Set {x.No}",
                Repeat = x.Set.Repeat,
                LevelIds = null,
                Main = x.Set.Main,
                Lines = x.Set.Lines.Select(l => new WorkoutLineDto
                {
                    Reps = l.Reps,
                    Distance = l.Distance,
                    Text = l.Text,
                    Intensity = l.Intensity,
                    PerLevel = levelId is int lid && l.PerLevel != null && l.PerLevel.TryGetValue(lid, out var cell)
                        ? new Dictionary<int, WorkoutCellDto> { [lid] = new() { SendOff = cell.SendOff, Reps = cell.Reps } }
                        : null,
                }).ToList(),
            })
            .ToList();

        return sets.Count == 0 && workout.Note == null ? null : new WorkoutDto { Note = workout.Note, Sets = sets };
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
