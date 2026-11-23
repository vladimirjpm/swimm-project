using Swimm.Domain.Entities;

namespace Swimm.Application.Mapping;

/// <summary>
/// Состояние отметки «результаты неполные» у дня соревнования — четыре поля
/// <see cref="Competition"/> одним значением, чтобы правило проверялось без базы.
/// </summary>
public sealed record PartialResultsState(string? Note, string? NoteEn, DateTime? MarkedAt, DateTime? AcceptedAt)
{
    public static readonly PartialResultsState Complete = new(null, null, null, null);

    public static PartialResultsState Of(Competition c) =>
        new(c.PartialResults, c.PartialResultsEn, c.PartialResultsAt, c.PartialResultsAcceptedAt);

    /// <summary>Отметка видна (сайт, список админки, «Требует внимания»): стоит и не принята как есть.</summary>
    public bool IsShown => IsShownFor(Note, AcceptedAt);

    /// <summary>Тот же предикат для проекций запроса (без загрузки сущности).</summary>
    public static bool IsShownFor(string? note, DateTime? acceptedAt) => note is not null && acceptedAt is null;

    public void WriteTo(Competition c)
    {
        c.PartialResults = Note;
        c.PartialResultsEn = NoteEn;
        c.PartialResultsAt = MarkedAt;
        c.PartialResultsAcceptedAt = AcceptedAt;
    }
}

/// <summary>
/// «Результаты неполные» (решение Р114, docs/data-integrity.md). Импорт любым путём считает
/// прогресс по строкам ЭТОГО дня из разобранного файла тем же <see cref="CompetitionProgress.Evaluate"/>,
/// что и results-auto, и решает здесь:
/// <list type="bullet">
/// <item>всё проплыто → отметка снята целиком (и «принято как есть» тоже — принимать нечего);</item>
/// <item>неполный, текст тот же, что был принят → «принято» остаётся (тот же протокол заехал ещё раз);</item>
/// <item>неполный иначе → отметка с новым текстом, «принято» сброшено — протокол другой, смотреть заново.</item>
/// </list>
/// Строки результатов правило не трогает: непроплытые «NS» лежат как напечатаны (Р76).
/// </summary>
public static class PartialResultsRule
{
    /// <summary>Потолок текста — длина колонок <c>Competitions.PartialResults*</c>.</summary>
    public const int MaxLength = 1000;

    public static PartialResultsState Apply(PartialResultsState current, CompetitionProgressSummary progress, DateTime now)
    {
        // Полный протокол дня — отметка снята. «Принято как есть» БЕЗ отметки — это принятие
        // «затянуто меньше дней, чем по сайту» (решение Влада 23.11.2026): оно про событие, а не про
        // протокол дня, и переимпорт полного дня его не отменяет.
        if (progress.IsFinished)
            return current.Note is null && current.AcceptedAt is not null
                ? PartialResultsState.Complete with { AcceptedAt = current.AcceptedAt }
                : PartialResultsState.Complete;

        var note = Clip(ResultsAutoRules.JoinReasons(progress.PendingLines()) ?? "результаты неполные");
        var noteEn = Clip(progress.PendingTextEn() ?? "Some events have no results yet");

        if (current.AcceptedAt is not null && current.Note == note)
            return current with { NoteEn = noteEn };
        return new PartialResultsState(note, noteEn, current.Note == note ? current.MarkedAt ?? now : now, null);
    }

    /// <summary>Админ: «это всё, остального не будет». Без отметки принимать нечего — состояние то же.</summary>
    public static PartialResultsState Accept(PartialResultsState current, DateTime now) =>
        current.Note is null ? current : current with { AcceptedAt = now };

    /// <summary>Админ передумал — отметка снова видна.</summary>
    public static PartialResultsState Unaccept(PartialResultsState current) => current with { AcceptedAt = null };

    /// <summary>
    /// Подсказка витрины для многодневки: видимые отметки дней одной строкой. Один день — его
    /// текст как есть; несколько — «Day 2: …» через « · ». null — все дни полные (или приняты).
    /// </summary>
    public static string? CombineDays(IEnumerable<(int? DayNumber, string? NoteEn)> shownDays)
    {
        var days = shownDays.Where(d => !string.IsNullOrWhiteSpace(d.NoteEn)).ToList();
        if (days.Count == 0) return null;
        if (days.Count == 1) return days[0].NoteEn;
        return string.Join(" · ", days.OrderBy(d => d.DayNumber ?? int.MaxValue)
            .Select(d => d.DayNumber is int n ? $"Day {n}: {d.NoteEn}" : d.NoteEn));
    }

    private static string Clip(string s) => s.Length <= MaxLength ? s : s[..(MaxLength - 1)] + "…";
}
