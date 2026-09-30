using System.Globalization;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Тренировка, заведённая тренером кнопкой (docs/plans/group-events-media-plan.md, Е1): дата,
/// название, по желанию начало, заметка, бассейн. Времён у неё может не быть вовсе — «сегодня нам
/// делали подводную съёмку», к ней видео. Отдельного типа «событие» нет (решение Влада 30.09.2026):
/// что это съёмка, видно из названия, а «есть ли времена» — из наличия результатов.
/// </summary>
public static class TrainingSessionRules
{
    public const int TitleMax = 100;
    public const int NoteMax = 500;
    public const int PoolTypeMax = 5;

    /// <summary>Вперёд не дальше года: запланировать съёмку можно, «гадание» — нет.</summary>
    public const int MaxDaysAhead = 366;

    /// <summary>Потолок тренировок группы на одну дату — от случайных повторных кликов и мусора.</summary>
    public const int MaxPerDay = 5;

    /// <summary>Потолок ссылок у одной тренировки (Е2) — от завала ленты.</summary>
    public const int MaxMediaPerSession = 50;

    /// <summary>Потолок ссылок одного участника у одной тренировки; управляющего не касается.</summary>
    public const int MaxMediaPerMember = 10;

    /// <summary>Раньше этого — явная опечатка в годе.</summary>
    public static readonly DateOnly Earliest = new(2000, 1, 1);

    /// <summary>Префикс натурального ключа у заведённых кнопкой (у залитых скриптом — «20251028»).</summary>
    public const string ExternalIdPrefix = "ui-";

    public sealed record Normalized(DateOnly Date, string? StartTime, string Title, string? Note, string? PoolType);

    /// <summary>Разобрать и проверить ввод; <c>Error</c> не null — отказ (400) с текстом для человека.</summary>
    public static (Normalized? Value, string? Error) Normalize(TrainingSessionInputDto input, DateOnly todayLocal)
    {
        if (!DateOnly.TryParseExact(input.Date?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            return (null, "Date must be yyyy-MM-dd.");
        if (date < Earliest) return (null, "Date is too far in the past.");
        if (date > todayLocal.AddDays(MaxDaysAhead)) return (null, "Date is more than a year ahead.");

        string? start = null;
        if (!string.IsNullOrWhiteSpace(input.Start))
        {
            if (!TimeOnly.TryParseExact(input.Start.Trim(), "HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var t))
                return (null, "Start must be HH:mm.");
            start = t.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        var title = input.Title?.Trim() ?? "";
        if (title.Length == 0) return (null, "Give the training a name (e.g. “Underwater filming”).");
        if (title.Length > TitleMax) return (null, $"Name is longer than {TitleMax} characters.");

        var note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        if (note?.Length > NoteMax) return (null, $"Note is longer than {NoteMax} characters.");

        var pool = string.IsNullOrWhiteSpace(input.PoolType) ? null : input.PoolType.Trim();
        if (pool?.Length > PoolTypeMax) return (null, "Pool type is too long (25m / 50m).");

        return (new Normalized(date, start, title, note, pool), null);
    }

    /// <summary>Натуральный ключ новой тренировки: уникален в группе, не число (клиент не спутает с датой источника).</summary>
    public static string NewExternalId() => ExternalIdPrefix + Guid.NewGuid().ToString("N")[..16];
}
