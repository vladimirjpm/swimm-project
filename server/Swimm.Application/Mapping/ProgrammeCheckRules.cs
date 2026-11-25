using System.Globalization;
using Swimm.Application.Abstractions;

namespace Swimm.Application.Mapping;

/// <summary>Что показала сверка «программа loglig ↔ дни в базе» у отметки Р115 (Р123).</summary>
public enum ProgrammeVerdictKind
{
    /// <summary>(г) Вердикта нет: нет loglig-id, loglig не ответил, программа пуста, в базе нет дней.</summary>
    None,
    /// <summary>(а) Дни склеены: в программе есть даты, которых нет в базе, а их строки лежат в имеющемся дне.</summary>
    Collapsed,
    /// <summary>(б) Программа loglig многодневная и вся в базе; федерация пишет даты шире (Маккабиада).</summary>
    ProgrammeNarrower,
    /// <summary>(в) Программа loglig — один день, он в базе; остальные дни федерации, вероятно, — отдельное соревнование на loglig (compID 488).</summary>
    DayElsewhere,
    /// <summary>В программе есть дата, которой нет в базе, а строк этой даты в базе тоже нет (или разделить нельзя).</summary>
    DayNotPulled,
}

/// <summary>
/// Вердикт сверки. <paramref name="Lines"/> — перечень (план деления по дням);
/// <paramref name="SplitCompetitionId"/> — что делить кнопкой «Разделить по дням» (только у
/// <see cref="ProgrammeVerdictKind.Collapsed"/> без помех); <paramref name="SplitBlocked"/> — почему
/// кнопки нет, хотя дни склеены; <paramref name="AcceptPrimary"/> — «Принять как есть» основным действием.
/// </summary>
public sealed record ProgrammeCheckVerdict(
    ProgrammeVerdictKind Kind,
    string Headline,
    IReadOnlyList<string> Lines,
    string? Hint,
    int? SplitCompetitionId,
    string? SplitBlocked,
    bool AcceptPrimary)
{
    public bool CanSplit => SplitCompetitionId != null;
}

/// <summary>
/// Что знает сервер к моменту вердикта. <paramref name="ProgrammeDays"/> — даты дисциплин программы
/// loglig (<see cref="DisciplineDates.Days"/>), null — программу получить не удалось
/// (<paramref name="SourceError"/>); <paramref name="SplitPlan"/> — dry-run
/// <see cref="ICompetitionDaySplitService"/>, если его спрашивали (<see cref="ProgrammeCheckRules.NeedsSplitPlan"/>).
/// </summary>
public sealed record ProgrammeCheckInput(
    DateOnly SiteStart,
    DateOnly SiteEnd,
    IReadOnlyList<DateOnly> DbDays,
    int? LogligId,
    IReadOnlyList<DateOnly>? ProgrammeDays,
    string? SourceError = null,
    CompetitionDaySplitReport? SplitPlan = null);

/// <summary>
/// Р123: вердикт кнопки «Проверить, чего не хватает» у недотянутой многодневки (Р115) — чистая
/// функция, тесты <c>ProgrammeCheckRulesTests</c>. Порядок:
/// <list type="number">
/// <item>нет loglig-id / ошибка / пустая программа / в базе нет дней → (г) <see cref="ProgrammeVerdictKind.None"/>;</item>
/// <item>в программе есть даты, которых нет в базе: dry-run деления раскладывает строки на несколько
///   дней → (а) <see cref="ProgrammeVerdictKind.Collapsed"/> с кнопкой; у строк нет однозначного дня →
///   (а) без кнопки с причиной; иначе → <see cref="ProgrammeVerdictKind.DayNotPulled"/>;</item>
/// <item>все даты программы в базе: программа на 2+ дня → (б) <see cref="ProgrammeVerdictKind.ProgrammeNarrower"/>
///   («Принять как есть» основным), на 1 день → (в) <see cref="ProgrammeVerdictKind.DayElsewhere"/>
///   (подсказка без кнопки).</item>
/// </list>
/// Различие (б)/(в) — по числу дней программы: одна loglig-страница на день при многодневной записи
/// федерации — признак «дни — разные соревнования loglig» (488: 9816 + 9817); многодневная программа,
/// которая вся в базе, — сам старт короче записи федерации (Маккабиада: даты всех Игр).
/// </summary>
public static class ProgrammeCheckRules
{
    /// <summary>
    /// Нужен ли dry-run деления: в программе есть даты, которых нет в базе, а в базе ровно один день
    /// (делить умеет только его — у события из нескольких дней сервис деления откажет сам).
    /// </summary>
    public static bool NeedsSplitPlan(IReadOnlyList<DateOnly> dbDays, IReadOnlyList<DateOnly>? programmeDays) =>
        dbDays.Distinct().Count() == 1 && programmeDays != null && programmeDays.Except(dbDays).Any();

    public static ProgrammeCheckVerdict Verdict(ProgrammeCheckInput x)
    {
        if (x.LogligId == null)
            return None("У записи федерации нет loglig-id — программу проверить не по чему. Номер соревнования на loglig можно найти на странице федерации (ссылка на результаты).");
        if (x.SourceError != null)
            return None($"loglig не ответил (программа {x.LogligId}): {x.SourceError}. Попробуйте позже.");
        if (x.ProgrammeDays is not { Count: > 0 } programme)
            return None($"Программа на loglig {x.LogligId} пуста: у дисциплин нет дат — сравнивать не с чем.");
        if (x.DbDays.Count == 0)
            return None("В базе у старта нет дней — сравнивать программу не с чем.");

        var prog = programme.Distinct().Order().ToList();
        var db = x.DbDays.Distinct().Order().ToList();
        var withYear = prog.Concat(db).Append(x.SiteStart).Append(x.SiteEnd).Select(d => d.Year).Distinct().Count() > 1;
        string Day(DateOnly d) => d.ToString(withYear ? "dd.MM.yyyy" : "dd.MM", CultureInfo.InvariantCulture);
        string Days(IEnumerable<DateOnly> ds) => string.Join(", ", ds.Select(Day));

        var notInDb = prog.Except(db).ToList();
        if (notInDb.Count > 0)
        {
            var plan = x.SplitPlan;
            if (plan is { Refusal: null, Days.Count: > 1 })
            {
                var lines = plan.Days.Select(d =>
                    $"день {d.DayNumber} {Day(d.Date)}: {d.Rows} {Plural(d.Rows, "строка", "строки", "строк")}"
                    + (d.Media > 0 ? $" (медиа {d.Media})" : "")
                    + (d.Existing ? $" — остаётся #{plan.CompetitionId}" : " — новое соревнование")).ToList();
                return new ProgrammeCheckVerdict(ProgrammeVerdictKind.Collapsed,
                    $"Дни склеены: по программе loglig {x.LogligId} старт идёт {Days(prog)}, а в базе всё лежит днём {Days(db)}. "
                    + $"Строки {Days(notInDb)} — в нём, их можно перенести в свой день.",
                    lines,
                    "«Разделить по дням» перенесёт строки (их Id те же — медиа, пометки и промежуточные едут с ними), "
                    + "заведёт событие и пересчитает оба дня. Одна транзакция: всё или ничего.",
                    plan.CompetitionId, null, AcceptPrimary: false);
            }
            if (plan is { Unresolved.Count: > 0 })
            {
                var lines = plan.Unresolved.OrderByDescending(kv => kv.Value).Take(8)
                    .Select(kv => $"без однозначного дня: {kv.Key} ×{kv.Value}").ToList();
                if (plan.Unresolved.Count > 8) lines.Add($"и ещё {plan.Unresolved.Count - 8}");
                return new ProgrammeCheckVerdict(ProgrammeVerdictKind.Collapsed,
                    $"Дни, похоже, склеены: по программе loglig {x.LogligId} старт идёт {Days(prog)}, в базе — {Days(db)}. "
                    + "Но разделить кнопкой нельзя.",
                    lines, null, null,
                    $"Разделить нельзя: {plan.Refusal ?? "у части строк нет однозначного дня"}. "
                    + "Дисциплина на двух датах (отборы и финал в разные дни) или её нет в программе — делите руками (CLI --split-days, docs/plans/multiday-split-plan.md).",
                    AcceptPrimary: false);
            }
            return new ProgrammeCheckVerdict(ProgrammeVerdictKind.DayNotPulled,
                $"В программе loglig {x.LogligId} есть {Days(notInDb)}, а в базе этого дня нет"
                + (plan?.Refusal is { } why ? $"; разделить имеющийся день нельзя: {why}." : "."),
                [],
                "Строк этого дня в базе нет — протокол дня не затянут. «Перезатянуть», когда федерация выложит протокол; "
                + "дня не будет — «Принять как есть».",
                null, null, AcceptPrimary: false);
        }

        var siteDays = Enumerable.Range(0, Math.Max(0, x.SiteEnd.DayNumber - x.SiteStart.DayNumber) + 1)
            .Select(x.SiteStart.AddDays).ToList();
        var missing = siteDays.Except(db).ToList();
        if (prog.Count >= 2)
            return new ProgrammeCheckVerdict(ProgrammeVerdictKind.ProgrammeNarrower,
                $"Программа на loglig — только {Span(prog, Day)}, все эти дни в базе. Даты федерации шире (например, даты всех Игр)"
                + (missing.Count > 0 ? $": {Days(missing)} в программе нет." : "."),
                [],
                "Дотягивать нечего — «Принять как есть» закроет долг.",
                null, null, AcceptPrimary: true);

        return new ProgrammeCheckVerdict(ProgrammeVerdictKind.DayElsewhere,
            $"Программа на loglig {x.LogligId} — только {Day(prog[0])}, и этот день в базе"
            + (missing.Count > 0 ? $"; {Days(missing)} нет ни в базе, ни в программе." : "."),
            [],
            "Возможно, день лежит на loglig отдельным соревнованием, не привязанным к странице федерации "
            + "(живой случай: compID 488 — суббота в loglig 9817 при привязанном 9816). "
            + "Найдите его номер и затяните вручную — docs/pull-to-prod.md.",
            null, null, AcceptPrimary: false);
    }

    private static ProgrammeCheckVerdict None(string text) =>
        new(ProgrammeVerdictKind.None, text, [], null, null, null, AcceptPrimary: false);

    /// <summary>Сплошной ряд дат — диапазоном («05–07.07»), с разрывом — перечнем.</summary>
    private static string Span(IReadOnlyList<DateOnly> days, Func<DateOnly, string> day)
    {
        var contiguous = days[^1].DayNumber - days[0].DayNumber == days.Count - 1;
        if (!contiguous) return string.Join(", ", days.Select(day));
        if (days[0].Year != days[^1].Year) return IncompleteExplanationRules.Range(days[0], days[^1]);
        var full = IncompleteExplanationRules.Range(days[0], days[^1]);
        // «05–07.07.2026» → «05–07.07»: год виден в шапке карточки.
        return full.Length > 5 && full[^5] == '.' ? full[..^5] : full;
    }

    private static string Plural(int n, string one, string few, string many)
    {
        var m100 = Math.Abs(n) % 100;
        var m10 = m100 % 10;
        if (m100 is >= 11 and <= 14) return many;
        return m10 switch { 1 => one, >= 2 and <= 4 => few, _ => many };
    }
}
