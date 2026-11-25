using System.Globalization;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>Что показала сверка «программа loglig ↔ дни в базе» у отметки Р115 (Р123).</summary>
public enum ProgrammeVerdictKind
{
    /// <summary>(г) Вердикта нет: нет loglig-id, loglig не ответил, программа пуста, в базе нет дней.</summary>
    None,
    /// <summary>(а) Дни склеены: в программе есть даты, которых нет в базе, а их строки лежат в имеющемся дне.</summary>
    Collapsed,
    /// <summary>
    /// (б+в) В привязанной программе loglig только те дни, что уже в базе, а федерация пишет больше.
    /// Дальше решает поиск соседнего соревнования-дня на loglig: нашёлся — подсказка затянуть его
    /// (compID 488 → 9817, 6611 → 11590); нет — даты федерации шире плавания (Маккабиада).
    /// </summary>
    OnlyKnownDays,
    /// <summary>В программе есть дата, которой нет в базе, а строк этой даты в базе тоже нет (или разделить нельзя).</summary>
    DayNotPulled,
}

/// <summary>Соседнее соревнование loglig: номер, заголовок как на loglig и его даты.</summary>
public sealed record ProgrammeNeighbour(int LogligId, string Title, IReadOnlyList<DateOnly> Dates);

/// <summary>
/// Вердикт сверки. <paramref name="Lines"/> — перечень (план деления по дням);
/// <paramref name="SplitCompetitionId"/> — что делить кнопкой «Разделить по дням» (только у
/// <see cref="ProgrammeVerdictKind.Collapsed"/> без помех); <paramref name="SplitBlocked"/> — почему
/// кнопки нет, хотя дни склеены; <paramref name="AcceptPrimary"/> — «Принять как есть» основным действием;
/// <paramref name="AcceptWarning"/> — предупреждение у «Принять как есть» (день, похоже, есть на loglig);
/// <paramref name="Candidates"/> — соседи-кандидаты в недостающий день; <paramref name="Similar"/> —
/// то же название, но дата не подходит (одной строкой в подсказке); <paramref name="PlanFingerprint"/> —
/// отпечаток плана, который видел админ («дата=строк;…»): «Разделить» сверяет его с новым dry-run.
/// </summary>
public sealed record ProgrammeCheckVerdict(
    ProgrammeVerdictKind Kind,
    string Headline,
    IReadOnlyList<string> Lines,
    string? Hint,
    int? SplitCompetitionId,
    string? SplitBlocked,
    bool AcceptPrimary,
    string? AcceptWarning = null,
    IReadOnlyList<ProgrammeNeighbour>? Candidates = null,
    IReadOnlyList<ProgrammeNeighbour>? Similar = null,
    string? PlanFingerprint = null)
{
    public bool CanSplit => SplitCompetitionId != null;
}

/// <summary>
/// Что знает сервер к моменту вердикта. <paramref name="ProgrammeDays"/> — даты дисциплин программы
/// loglig (<see cref="DisciplineDates.Days"/>), null — программу получить не удалось
/// (<paramref name="SourceError"/>); <paramref name="SplitPlan"/> — dry-run
/// <see cref="ICompetitionDaySplitService"/>, если его спрашивали (<see cref="ProgrammeCheckRules.NeedsSplitPlan"/>).
/// Поиск соседей (<see cref="ProgrammeCheckRules.NeedsNeighbourSearch"/>): <paramref name="BoundTitle"/> —
/// заголовок привязанного соревнования, <paramref name="Neighbours"/> — ответившие соседи,
/// <paramref name="NeighboursFailed"/> — сколько не ответило; null у списка — поиск не делали.
/// </summary>
public sealed record ProgrammeCheckInput(
    DateOnly SiteStart,
    DateOnly SiteEnd,
    IReadOnlyList<DateOnly> DbDays,
    int? LogligId,
    IReadOnlyList<DateOnly>? ProgrammeDays,
    string? SourceError = null,
    CompetitionDaySplitReport? SplitPlan = null,
    string? BoundTitle = null,
    IReadOnlyList<LogligCompetitionSummaryDto>? Neighbours = null,
    int NeighboursFailed = 0);

/// <summary>
/// Р123: вердикт кнопки «Проверить, чего не хватает» у недотянутой многодневки (Р115) — чистая
/// функция, тесты <c>ProgrammeCheckRulesTests</c>. Порядок:
/// <list type="number">
/// <item>нет loglig-id / ошибка / пустая программа / в базе нет дней → (г) <see cref="ProgrammeVerdictKind.None"/>;</item>
/// <item>в программе есть даты, которых нет в базе: dry-run деления раскладывает строки на несколько
///   дней → (а) <see cref="ProgrammeVerdictKind.Collapsed"/> с кнопкой; у строк нет однозначного дня →
///   (а) без кнопки с причиной; иначе → <see cref="ProgrammeVerdictKind.DayNotPulled"/>;</item>
/// <item>все даты программы в базе → <see cref="ProgrammeVerdictKind.OnlyKnownDays"/>. По одной
///   программе Маккабиаду (даты всех Игр) не отличить от 6611/488 (день — отдельное соревнование
///   loglig), поэтому решает поиск соседей ±<see cref="NeighbourRadius"/>: кандидат — то же название
///   без дня недели и дат (<see cref="LogligTitleRules.Normalize"/>) И даты внутри недостающих дней
///   федерации. Кандидат есть — подсказка затянуть его, «Принять как есть» не основное и с
///   предупреждением; кандидатов нет и все соседи ответили — «Принять как есть» основным.</item>
/// </list>
/// </summary>
public static class ProgrammeCheckRules
{
    /// <summary>Сколько номеров loglig в каждую сторону от привязанного проверять (не больше 6 запросов).</summary>
    public const int NeighbourRadius = 3;

    /// <summary>
    /// Нужен ли dry-run деления: в программе есть даты, которых нет в базе, а в базе ровно один день
    /// (делить умеет только его — у события из нескольких дней сервис деления откажет сам).
    /// </summary>
    public static bool NeedsSplitPlan(IReadOnlyList<DateOnly> dbDays, IReadOnlyList<DateOnly>? programmeDays) =>
        dbDays.Distinct().Count() == 1 && programmeDays != null && programmeDays.Except(dbDays).Any();

    /// <summary>
    /// Нужен ли поиск соседей: программа есть, вся она в базе, а у федерации есть дни, которых в базе нет.
    /// </summary>
    public static bool NeedsNeighbourSearch(DateOnly siteStart, DateOnly siteEnd, IReadOnlyList<DateOnly> dbDays,
        IReadOnlyList<DateOnly>? programmeDays) =>
        programmeDays is { Count: > 0 } p && dbDays.Count > 0 && !p.Except(dbDays).Any()
        && SiteDays(siteStart, siteEnd).Except(dbDays).Any();

    /// <summary>Номера соседей: ±<see cref="NeighbourRadius"/> от привязанного, без него самого и без &lt;1.</summary>
    public static IReadOnlyList<int> NeighbourIds(int logligId) =>
        Enumerable.Range(logligId - NeighbourRadius, 2 * NeighbourRadius + 1)
            .Where(id => id != logligId && id > 0).ToList();

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
            return MissingFromDb(x, prog, db, notInDb, Day, Days, Span);

        var missing = SiteDays(x.SiteStart, x.SiteEnd).Except(db).ToList();
        var headline = $"В привязанной программе loglig {x.LogligId} только те дни, что уже есть в базе: {Span(prog, Day)}"
            + (missing.Count > 0 ? $". Нет ни в базе, ни в программе: {Days(missing)}." : ".");

        var (candidates, similar) = Classify(x.BoundTitle, x.Neighbours ?? [], missing);
        var similarLine = similar.Count == 0 ? null
            : "Похожее, но дата не подходит: " + string.Join("; ", similar.Select(s =>
            {
                var when = s.Dates.Count > 0 ? Days(s.Dates) : "без даты";
                return $"№ {s.LogligId} «{s.Title}» ({when})";
            })) + ".";
        var lines = similarLine == null ? new List<string>() : [similarLine];

        if (candidates.Count > 0)
        {
            var hint = string.Join(" ", candidates.Select(c =>
                $"Похоже, {Plural(c.Dates.Count, "день", "дни", "дни")} {Days(c.Dates)} {(c.Dates.Count == 1 ? "лежит" : "лежат")} на loglig отдельным соревнованием № {c.LogligId} «{c.Title}»."))
                + " Затяните его вторым днём — docs/pull-to-prod.md.";
            return new ProgrammeCheckVerdict(ProgrammeVerdictKind.OnlyKnownDays, headline, lines, hint, null, null,
                AcceptPrimary: false,
                AcceptWarning: "день, похоже, есть на loglig — «Принять как есть» закроет долг, и этот день не будет затянут",
                Candidates: candidates, Similar: similar);
        }

        // Кандидатов нет. «Принять» основным — только если соседей действительно проверили.
        string? unchecked_ = x.Neighbours == null || x.BoundTitle == null
            ? "Соседние номера loglig проверить не удалось"
                + (x.BoundTitle == null && x.Neighbours != null ? " (не прочитан заголовок привязанного соревнования)" : "")
                + " — убедитесь, что недостающего дня нет на loglig отдельным соревнованием."
            : x.NeighboursFailed > 0
                ? $"{x.NeighboursFailed} из {NeighbourIds(x.LogligId!.Value).Count} соседних номеров loglig не ответили — проверьте их вручную."
                : null;
        if (unchecked_ != null)
            return new ProgrammeCheckVerdict(ProgrammeVerdictKind.OnlyKnownDays, headline, lines,
                unchecked_ + " Если дня нет — «Принять как есть».", null, null, AcceptPrimary: false, Similar: similar);

        return new ProgrammeCheckVerdict(ProgrammeVerdictKind.OnlyKnownDays, headline, lines,
            $"Соседние номера loglig (±{NeighbourRadius}) недостающего дня не содержат. Скорее всего, даты федерации шире "
            + "плавания (как у Маккабиады — даты всех Игр): дотягивать нечего, «Принять как есть» закроет долг.",
            null, null, AcceptPrimary: true, Candidates: [], Similar: similar);
    }

    /// <summary>
    /// Кандидат — то же нормализованное название и даты (дисциплин, иначе из хвоста заголовка) все
    /// внутри недостающих дней федерации. То же название с другой датой — «похожее».
    /// </summary>
    private static (List<ProgrammeNeighbour> Candidates, List<ProgrammeNeighbour> Similar) Classify(
        string? boundTitle, IReadOnlyList<LogligCompetitionSummaryDto> neighbours, IReadOnlyList<DateOnly> missing)
    {
        var key = LogligTitleRules.Normalize(boundTitle);
        var candidates = new List<ProgrammeNeighbour>();
        var similar = new List<ProgrammeNeighbour>();
        if (key.Length == 0) return (candidates, similar);
        foreach (var n in neighbours.OrderBy(n => n.LogligId))
        {
            if (LogligTitleRules.Normalize(n.Title) != key) continue;
            IReadOnlyList<DateOnly> dates = n.Dates.Count > 0 ? n.Dates.Distinct().Order().ToList()
                : LogligTitleRules.TitleDate(n.Title) is { } td ? [td] : [];
            var item = new ProgrammeNeighbour(n.LogligId, n.Title, dates);
            if (dates.Count > 0 && dates.All(missing.Contains)) candidates.Add(item);
            else similar.Add(item);
        }
        return (candidates, similar);
    }

    /// <summary>Отпечаток плана деления: дата и число строк каждого дня по порядку.</summary>
    public static string Fingerprint(IEnumerable<CompetitionDaySplitDay> days) =>
        string.Join(";", days.OrderBy(d => d.Date).Select(d =>
            $"{d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}={d.Rows.ToString(CultureInfo.InvariantCulture)}"));

    private static ProgrammeCheckVerdict MissingFromDb(ProgrammeCheckInput x, List<DateOnly> prog, List<DateOnly> db,
        List<DateOnly> notInDb, Func<DateOnly, string> Day, Func<IEnumerable<DateOnly>, string> Days,
        Func<IReadOnlyList<DateOnly>, Func<DateOnly, string>, string> Span)
    {
        var plan = x.SplitPlan;
        if (plan is { Refusal: null, Days.Count: > 1 })
        {
            // Кнопка — только если каждый день плана внутри дат федерации и у федерации есть дни,
            // которых нет в базе (Р115). Иначе опечатка в дате дисциплины на loglig завела бы лишний
            // день, а настоящий недостающий остался бы недостающим (ревью PR #283).
            var siteDays = SiteDays(x.SiteStart, x.SiteEnd);
            var outside = plan.Days.Select(d => d.Date).Where(d => !siteDays.Contains(d)).Distinct().Order().ToList();
            var siteGap = siteDays.Except(db).Any();
            string? blocked = outside.Count > 0
                ? $"Разделить нельзя: дата программы {Days(outside)} вне дат федерации ({Span(siteDays, Day)}) — похоже на ошибку "
                  + "в программе loglig. Проверьте глазами; если дата верна — делите руками (CLI --split-days)."
                : !siteGap
                    ? "Разделить нельзя: у федерации нет дней, которых не было бы в базе, — долга Р115 нет."
                    : null;

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
                blocked == null ? plan.CompetitionId : null, blocked, AcceptPrimary: false,
                PlanFingerprint: blocked == null ? Fingerprint(plan.Days) : null);
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
        // Сюда попадают: отказ деления («остальные дни лежат не здесь», дата соревнования не совпала
        // с днём программы) или в базе уже несколько дней. «Перезатянуть» тут вреден: строки дня,
        // скорее всего, уже есть — отдельным соревнованием или внутри другого дня, и перезалив даст дубль.
        return new ProgrammeCheckVerdict(ProgrammeVerdictKind.DayNotPulled,
            $"В программе loglig {x.LogligId} есть {Days(notInDb)}, а в базе у старта этого дня нет"
            + (plan?.Refusal is { } why ? $"; разделить имеющийся день нельзя: {why}." : "."),
            [],
            "Возможно, день уже есть отдельным соревнованием, не собранным в событие, или склеен внутри события — "
            + "проверьте «дни есть, события нет» (CLI --collapsed-days, сбор — --join-days). "
            + "«Перезатянуть» не поможет: получится дубль.",
            null, null, AcceptPrimary: false);
    }

    private static List<DateOnly> SiteDays(DateOnly start, DateOnly end) =>
        Enumerable.Range(0, Math.Max(0, end.DayNumber - start.DayNumber) + 1).Select(start.AddDays).ToList();

    private static ProgrammeCheckVerdict None(string text) =>
        new(ProgrammeVerdictKind.None, text, [], null, null, null, AcceptPrimary: false);

    /// <summary>Сплошной ряд дат — диапазоном («05–07.07»), с разрывом — перечнем.</summary>
    private static string Span(IReadOnlyList<DateOnly> days, Func<DateOnly, string> day)
    {
        if (days.Count == 1) return day(days[0]);
        var contiguous = days[^1].DayNumber - days[0].DayNumber == days.Count - 1;
        if (!contiguous) return string.Join(", ", days.Select(day));
        var full = IncompleteExplanationRules.Range(days[0], days[^1]);
        if (days[0].Year != days[^1].Year) return full;
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
