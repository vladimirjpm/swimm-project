using System.Globalization;
using System.Text.RegularExpressions;

namespace Swimm.Application.Mapping;

/// <summary>День старта в базе: дата и сколько строк результатов.</summary>
public sealed record PulledDay(DateOnly Date, int Rows);

/// <summary>
/// Объяснение «чего не хватает» у отметки «неполный» для блока «Что не так» карточки
/// /Admin/Competitions (просьба Влада 24.11.2026: «ждём протокол · неполный — а чего не хватает?»).
/// <paramref name="Headline"/> — что именно не так, <paramref name="Items"/> — перечень (дни или
/// дисциплины), <paramref name="Hints"/> — что это обычно значит и что делать.
/// </summary>
public sealed record IncompleteExplanation(string Headline, IReadOnlyList<string> Items, IReadOnlyList<string> Hints);

/// <summary>
/// Тексты «неполного» — чистые функции, тесты <c>IncompleteExplanationRulesTests</c>.
/// Р115 — затянуты не все дни по сайту федерации (<see cref="MissingDays"/>); Р114 — в протоколе дня
/// есть дисциплины без времён (<see cref="PartialProtocol"/>, разбирает текст отметки
/// <see cref="CompetitionProgressSummary.PendingLines"/>).
/// </summary>
public static partial class IncompleteExplanationRules
{
    /// <summary>
    /// Все объяснения «неполного» у строки списка: Р115 (если правило состояний поставило
    /// <c>PulledDays/SiteDays</c> и у строки есть даты сайта), затем Р114 по каждому дню с видимой
    /// отметкой (у события — с подписью «День N · дд.мм: »). Пусто — объяснять нечего.
    /// </summary>
    public static IReadOnlyList<IncompleteExplanation> For(Dtos.UnifiedCompetitionRowDto u)
    {
        var list = new List<IncompleteExplanation>();
        if (u.Db == null) return list;
        var days = u.Db.Single != null ? [u.Db.Single] : u.Db.Days;
        if (u.Status is { PulledDays: not null, SiteDays: not null } && u.Site is { } site
            && MissingDays(DateOnly.FromDateTime(site.DateStart), DateOnly.FromDateTime(site.DateEnd), PulledOf(days)) is { } r115)
            list.Add(r115);
        foreach (var d in days.Where(d => d.PartialResults != null))
        {
            var e = PartialProtocol(d.PartialResults!);
            var label = u.Db.Single != null ? "" : $"День {d.DayNumber?.ToString(CultureInfo.InvariantCulture) ?? "?"}"
                + (ParseDbDate(d.Date) is { } dd ? $" · {dd.ToString("dd.MM", CultureInfo.InvariantCulture)}" : "") + ": ";
            list.Add(e with { Headline = label + e.Headline });
        }
        return list;
    }

    /// <summary>Недостающие дни коротко — для подписи шага 5: «нет 23.12» (несколько — через запятую).</summary>
    public static string? MissingDatesShort(Dtos.UnifiedCompetitionRowDto u)
    {
        if (u.Db == null || u.Site is not { } site || u.Status is not { PulledDays: not null }) return null;
        var from = DateOnly.FromDateTime(site.DateStart);
        var to = DateOnly.FromDateTime(site.DateEnd) is var t && t >= from ? t : from;
        var have = PulledOf(u.Db.Single != null ? [u.Db.Single] : u.Db.Days).Select(p => p.Date).ToHashSet();
        var missing = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays).Where(d => !have.Contains(d)).ToList();
        var fmt = from.Year != to.Year ? "dd.MM.yyyy" : "dd.MM";
        return missing.Count == 0 ? null : "нет " + string.Join(", ", missing.Select(d => d.ToString(fmt, CultureInfo.InvariantCulture)));
    }

    private static List<PulledDay> PulledOf(IEnumerable<Dtos.CompetitionListItemDto> days) =>
        days.Select(d => ParseDbDate(d.Date) is { } dt ? new PulledDay(dt, d.ResultCount) : null).OfType<PulledDay>().ToList();

    private static DateOnly? ParseDbDate(string? s) =>
        DateOnly.TryParseExact(s, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>
    /// Р115: «По сайту федерации старт идёт 22–23.12.2023 (2 дня). В базе есть: 22.12 (1337 строк).
    /// Нет: 23.12.» + подсказки. Дни в базе вне диапазона сайта тоже перечислены в «есть» — так видно,
    /// что даты разъехались. Не хватает ни одного дня — null (объяснять нечего).
    /// </summary>
    public static IncompleteExplanation? MissingDays(DateOnly siteStart, DateOnly siteEnd, IReadOnlyList<PulledDay> pulled)
    {
        if (siteEnd < siteStart) siteEnd = siteStart;
        var siteDays = Enumerable.Range(0, siteEnd.DayNumber - siteStart.DayNumber + 1)
            .Select(siteStart.AddDays).ToList();
        var have = pulled.GroupBy(d => d.Date).Select(g => new PulledDay(g.Key, g.Sum(x => x.Rows)))
            .OrderBy(d => d.Date).ToList();
        var missing = siteDays.Where(d => have.All(h => h.Date != d)).ToList();
        if (missing.Count == 0) return null;

        var allDates = siteDays.Concat(have.Select(h => h.Date)).ToList();
        var withYear = allDates.Select(d => d.Year).Distinct().Count() > 1;
        string Day(DateOnly d) => d.ToString(withYear ? "dd.MM.yyyy" : "dd.MM", CultureInfo.InvariantCulture);

        var headline = $"По сайту федерации старт идёт {Range(siteStart, siteEnd)} ({siteDays.Count} {Plural(siteDays.Count, "день", "дня", "дней")}). "
            + (have.Count == 0 ? "В базе дней нет. " : $"В базе есть: {string.Join(", ", have.Select(h => $"{Day(h.Date)} ({h.Rows} {Plural(h.Rows, "строка", "строки", "строк")})"))}. ")
            + $"Нет: {string.Join(", ", missing.Select(Day))}.";
        return new IncompleteExplanation(headline, [],
        [
            "Почему так бывает: протокол дня ещё не выложен; федерация держит дни разными соревнованиями "
            + "на loglig, а на странице федерации привязан только один; протокол всех дней свёрнут в один день (Р119).",
            "«Перезатянуть» поможет, только если на странице федерации появился протокол недостающего дня. "
            + "Дня не будет — «Принять как есть».",
        ]);
    }

    /// <summary>
    /// Р114: текст отметки «ждут 2 из 16 событий; A: ещё не плыли (0 из 9 со временем); …; и ещё K»
    /// → «В протоколе 2 из 16 дисциплин без результатов:» + строки дисциплин + «и ещё K» + подсказки.
    /// Текст не того вида (старые отметки, ручной текст) — заголовком как есть, без перечня.
    /// </summary>
    public static IncompleteExplanation PartialProtocol(string note)
    {
        var parts = note.Split("; ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var head = parts.Count > 0 ? PendingHeader().Match(parts[0]) : Match.Empty;
        var hints = new List<string>
        {
            "Обычно это протокол, снятый до конца старта, или дистанции, которые не проплывали. "
            + "«Перезатянуть», когда федерация выложит полный протокол; дистанций не будет — «Принять как есть».",
        };
        if (!head.Success) return new IncompleteExplanation(note, [], hints);

        var pending = int.Parse(head.Groups[1].Value, CultureInfo.InvariantCulture);
        var total = int.Parse(head.Groups[2].Value, CultureInfo.InvariantCulture);
        var items = new List<string>();
        var more = 0;
        foreach (var p in parts.Skip(1))
        {
            if (MoreTail().Match(p) is { Success: true } m)
            {
                more = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                continue;
            }
            // «Событие: причина» — двоеточие причины последнее (в названиях дисциплин его нет).
            var i = p.LastIndexOf(": ", StringComparison.Ordinal);
            items.Add(i > 0 ? $"{p[..i]} — {p[(i + 2)..]}" : p);
        }
        if (more > 0) items.Add($"и ещё {more}");
        var noun = Plural(total, "дисциплины", "дисциплин", "дисциплин");
        return new IncompleteExplanation(
            $"В протоколе {pending} из {total} {noun} без результатов (в последнем заплыве нет времён):", items, hints);
    }

    /// <summary>«22–23.12.2023» / «30.11–01.12.2023» / «31.12.2023–01.01.2024» / «05.03.2024».</summary>
    public static string Range(DateOnly from, DateOnly to)
    {
        static string F(DateOnly d, string f) => d.ToString(f, CultureInfo.InvariantCulture);
        if (to <= from) return F(from, "dd.MM.yyyy");
        if (from.Year != to.Year) return $"{F(from, "dd.MM.yyyy")}–{F(to, "dd.MM.yyyy")}";
        if (from.Month != to.Month) return $"{F(from, "dd.MM")}–{F(to, "dd.MM.yyyy")}";
        return $"{F(from, "dd")}–{F(to, "dd.MM.yyyy")}";
    }

    private static string Plural(int n, string one, string few, string many)
    {
        var m100 = Math.Abs(n) % 100;
        var m10 = m100 % 10;
        if (m100 is >= 11 and <= 14) return many;
        return m10 switch { 1 => one, >= 2 and <= 4 => few, _ => many };
    }

    [GeneratedRegex(@"^ждут (\d+) из (\d+) событ")]
    private static partial Regex PendingHeader();

    [GeneratedRegex(@"^и ещё (\d+)$")]
    private static partial Regex MoreTail();
}
