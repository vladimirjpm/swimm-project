using System.Globalization;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Режим задачи results-auto — настройка <see cref="SettingKey"/> в <c>Sys_AdminSettings</c>
/// (docs/plans/prod-auto-pull-plan.md §4.4, решение Р-2): off → report → import.
/// </summary>
public static class ResultsAutoModes
{
    public const string SettingKey = "ResultsAutoMode";

    /// <summary>Ничего не делает (умолчание).</summary>
    public const string Off = "off";

    /// <summary>Разбирает, классифицирует, пишет вердикт строке и журналу; НЕ импортирует.</summary>
    public const string Report = "report";

    /// <summary>
    /// Импорт «зелёных» — этап П6. Пока распознаётся, но работает как <see cref="Report"/> и
    /// пишет в итог прогона, что импорт ещё не реализован.
    /// </summary>
    public const string Import = "import";

    /// <summary>Значение настройки → режим. Пусто и незнакомое — <see cref="Off"/>: опечатка не включает задачу.</summary>
    public static string Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Report => Report,
        Import => Import,
        _ => Off,
    };
}

/// <summary>
/// Вердикт results-auto у строки «Входящих» (<c>Sys_DiscoveredCompetitions.AutoPullVerdict</c>, CHECK
/// <c>CK_Sys_DiscoveredCompetitions_AutoPullVerdict</c> — список должен совпадать).
/// </summary>
public static class ResultsAutoVerdicts
{
    /// <summary>Все гейты пройдены — в режиме import задача затянула бы сама.</summary>
    public const string WouldTake = "would-take";

    /// <summary>Ждёт Влада: причина в <c>AutoPullBlockedReason</c>.</summary>
    public const string Blocked = "blocked";

    /// <summary>Соревнование ещё идёт (в протоколе есть непроплытые события) — смотрим позже.</summary>
    public const string NotFinished = "not-finished";

    /// <summary>Забор/разбор упал (сеть, вёрстка) — повтор сам.</summary>
    public const string Failed = "failed";

    /// <summary>Протокола нет (PDF пуст) — повтор реже: файл могут выложить позже.</summary>
    public const string Empty = "empty";

    public static readonly IReadOnlyList<string> All = [WouldTake, Blocked, NotFinished, Failed, Empty];

    /// <summary>Короткая подпись для чипа и итога прогона.</summary>
    public static string Label(string? verdict) => verdict switch
    {
        WouldTake => "взяла бы сама",
        Blocked => "ждёт тебя",
        NotFinished => "ещё идёт",
        Failed => "сбой разбора",
        Empty => "протокола нет",
        _ => "не проверяла",
    };
}

/// <summary>Значения <c>?filter=</c> на /Admin/Competitions для вердиктов results-auto (ссылка дашборда).</summary>
public static class AutoPullFilters
{
    /// <summary>«Ждут тебя»: не затянуто, задача не взяла.</summary>
    public const string Blocked = "autopull-blocked";

    /// <summary>«Взяла бы сама».</summary>
    public const string WouldTake = "autopull-would-take";
}

/// <summary>Строка «Входящих» глазами results-auto — всё, что нужно для выбора, без сети и базы.</summary>
/// <param name="AlreadyInDb">Строка уже связана с соревнованием в базе (OrgCompId или матч имя+дата).</param>
/// <param name="ManualEmpty">Пометку «протокола нет» поставил человек — решение принято, не трогаем.</param>
public sealed record ResultsAutoCandidate(
    int Id,
    string Name,
    string Status,
    string Discipline,
    int? LogligId,
    DateTime DateStart,
    DateTime DateEnd,
    bool AlreadyInDb,
    bool ManualEmpty,
    DateTime? MissingSince,
    DateTime? CheckedAt,
    string? Verdict);

/// <summary>Итог выбора тика: кого смотреть и сколько всего в окне.</summary>
/// <param name="InWindow">Строк в окне (прошли, не старше <see cref="ResultsAutoRules.WindowDays"/> дней).</param>
/// <param name="Due">Из них пора проверять (шаг повторной проверки прошёл).</param>
public sealed record ResultsAutoPick(ResultsAutoCandidate? Next, int InWindow, int Due);

/// <summary>Что задача знает о строке после разбора — вход гейтов §4.4.</summary>
/// <param name="Classifier">Вердикт <see cref="Validation.BulkPullClassifier"/> — тот же, что у пакетной кнопки.</param>
/// <param name="IsChampionship">Чемпионат по имени или регламенту (подтверждённому именем).</param>
/// <param name="DaysInFile">Сколько дней (соревнований) в разобранном файле.</param>
/// <param name="Progress">«Проплыто ли» по <see cref="CompetitionProgress"/>; null — разбор не читается.</param>
/// <param name="RegulationPoolType">Длина из регламента; null — не нашлась.</param>
/// <param name="Venue">Ответ справочника бассейнов по месту строки.</param>
public sealed record ResultsAutoGateInput(
    BulkPullVerdict Classifier,
    IReadOnlyList<string> ClassifierReasons,
    bool IsChampionship,
    int DaysInFile,
    CompetitionProgressSummary? Progress,
    string? RegulationPoolType,
    VenuePoolLookup Venue);

/// <summary>Решение по строке: вердикт, причины (для «ждёт тебя») и длина бассейна у «взяла бы».</summary>
public sealed record ResultsAutoDecision(
    string Verdict,
    IReadOnlyList<string> Reasons,
    string? PoolType = null,
    string? PoolTypeSource = null)
{
    /// <summary>Причины одной строкой под колонку <c>AutoPullBlockedReason</c>; null — причин нет.</summary>
    public string? ReasonText => ResultsAutoRules.JoinReasons(Reasons);
}

/// <summary>
/// Правила задачи results-auto (П5, docs/plans/prod-auto-pull-plan.md §4.4) — чистые функции:
/// кого брать за тик и прошла ли строка гейты. Ни сети, ни базы — тесты <c>ResultsAutoRulesTests</c>.
/// </summary>
public static class ResultsAutoRules
{
    /// <summary>Окно: старты, закончившиеся не раньше стольких дней назад (Р-2: 7).</summary>
    public const int WindowDays = 7;

    /// <summary>
    /// С какого часа дня окончания (по Израилю) старт уже кандидат. План говорит «DateEnd прошёл», а
    /// цель П6 — «результаты в тот же вечер»; поэтому не со следующего дня, а с полудня дня окончания:
    /// раньше финал утренней сессии не готов, а недоплытый протокол отсекает гейт «закончено»
    /// (<see cref="CompetitionProgress"/>) — строка получит «ещё идёт» и вернётся через
    /// <see cref="NotFinishedRecheck"/>.
    /// </summary>
    public const int FinishedFromLocalHour = 12;

    /// <summary>«Ещё идёт» — посмотреть снова через 2 ч (вечерняя сессия кончается за пару часов).</summary>
    public static readonly TimeSpan NotFinishedRecheck = TimeSpan.FromHours(2);

    /// <summary>Сбой забора — через 3 ч (как у старт-листов: сайт лёг — долбить незачем).</summary>
    public static readonly TimeSpan FailedRecheck = TimeSpan.FromHours(3);

    /// <summary>Протокола нет — через 6 ч: loglig выкладывает файл позже, но не каждые полчаса.</summary>
    public static readonly TimeSpan EmptyRecheck = TimeSpan.FromHours(6);

    /// <summary>
    /// «Ждёт тебя» и «взяла бы сама» — раз в сутки: вердикт меняется только если поменялись данные
    /// (протокол перевыложили, Влад дописал место в справочник), а за сутки это видно.
    /// </summary>
    public static readonly TimeSpan SettledRecheck = TimeSpan.FromHours(24);

    /// <summary>Длина колонки <c>AutoPullBlockedReason</c>.</summary>
    public const int MaxReasonLength = 1000;

    /// <summary>Через сколько перепроверять строку с таким вердиктом.</summary>
    public static TimeSpan RecheckAfter(string? verdict) => verdict switch
    {
        ResultsAutoVerdicts.NotFinished => NotFinishedRecheck,
        ResultsAutoVerdicts.Failed => FailedRecheck,
        ResultsAutoVerdicts.Empty => EmptyRecheck,
        _ => SettledRecheck,
    };

    /// <summary>
    /// Строка в окне задачи: новая, плавание, есть loglig-id, в базе её нет, «нет протокола» не
    /// ставил человек, не пропала с сайта; день окончания по Израилю наступил (с
    /// <see cref="FinishedFromLocalHour"/>) и был не больше <see cref="WindowDays"/> дней назад.
    /// </summary>
    public static bool InWindow(ResultsAutoCandidate c, DateTime nowUtc)
    {
        if (!string.Equals(c.Status, "new", StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(c.Discipline, Disciplines.Swimming, StringComparison.OrdinalIgnoreCase)) return false;
        if (c.LogligId is null || c.AlreadyInDb || c.ManualEmpty || c.MissingSince is not null) return false;

        var localNow = IsraelTime.ToLocal(nowUtc);
        var endDay = c.DateEnd.Date;
        if (localNow < endDay.AddHours(FinishedFromLocalHour)) return false;
        return (localNow.Date - endDay).TotalDays <= WindowDays;
    }

    /// <summary>Шаг повторной проверки прошёл (или строку ещё не смотрели).</summary>
    public static bool IsDue(ResultsAutoCandidate c, DateTime nowUtc) =>
        c.CheckedAt is not { } at || nowUtc - at >= RecheckAfter(c.Verdict);

    /// <summary>
    /// Кого смотреть в этот тик — ровно одну строку (§6: строго последовательно, одна за тик).
    /// Сначала ни разу не смотренные (из них — самый свежий старт: ради него и задача), потом —
    /// дольше всех не перепроверявшиеся.
    /// </summary>
    public static ResultsAutoPick Pick(IEnumerable<ResultsAutoCandidate> candidates, DateTime nowUtc)
    {
        var inWindow = candidates.Where(c => InWindow(c, nowUtc)).ToList();
        var due = inWindow.Where(c => IsDue(c, nowUtc)).ToList();
        var next = due
            .OrderBy(c => c.CheckedAt is null ? 0 : 1)
            .ThenBy(c => c.CheckedAt ?? DateTime.MinValue)
            .ThenByDescending(c => c.DateEnd)
            .ThenBy(c => c.Id)
            .FirstOrDefault();
        return new ResultsAutoPick(next, inWindow.Count, due.Count);
    }

    /// <summary>
    /// Гейты, которые видны без сети, — по имени и датам isr.org.il. Сработал — строку не качаем:
    /// чемпионат и многодневку всё равно разбирает человек, а разбор PDF на проде — нагрузка (§6).
    /// null — качать.
    /// </summary>
    public static ResultsAutoDecision? PreGate(ResultsAutoCandidate c)
    {
        var reasons = new List<string>();
        if (CompetitionNameRules.IsChampionship(c.Name))
            reasons.Add("чемпионат Израиля (по названию) — разбирает человек");
        if (c.DateStart.Date != c.DateEnd.Date)
            reasons.Add(string.Create(CultureInfo.InvariantCulture,
                $"многодневка по isr.org.il ({c.DateStart:dd.MM}–{c.DateEnd:dd.MM}) — путь Б, по дням / --split-days"));
        return reasons.Count == 0 ? null : new ResultsAutoDecision(ResultsAutoVerdicts.Blocked, reasons);
    }

    /// <summary>
    /// Гейты §4.4 после разбора. Порядок: сбой/пусто → «закончено ли» (идущее соревнование не
    /// трогаем, какие бы ни были остальные причины — протокол неполный) → остальные гейты разом,
    /// чтобы «ждёт тебя» называл ВСЕ причины, а не первую.
    /// </summary>
    public static ResultsAutoDecision Decide(ResultsAutoGateInput input)
    {
        if (input.Classifier == BulkPullVerdict.Failed)
            return new(ResultsAutoVerdicts.Failed, NonEmpty(input.ClassifierReasons, "забор или разбор не удался"));
        if (input.Classifier == BulkPullVerdict.Empty)
            return new(ResultsAutoVerdicts.Empty, NonEmpty(input.ClassifierReasons, "протокола нет"));

        if (input.Progress is null)
            return new(ResultsAutoVerdicts.Failed, ["разбор не читается — «закончено ли» не проверить"]);
        if (!input.Progress.IsFinished)
        {
            // Строки — общие с отметкой «результаты неполные» у соревнования (PartialResultsRule).
            var reasons = input.Progress.PendingLines().ToList();
            if (input.Progress.Events.Count > 0) reasons[0] = "соревнование не закончено: " + reasons[0];
            return new(ResultsAutoVerdicts.NotFinished, reasons);
        }

        var blocked = new List<string>();

        // 🟢 по классификатору: только Clean. NoRegulation (🟠) пачка импортирует, а задача без
        // человека — нет: медали и зачёт проставить нечем (план §4.4 — «только 🟢»).
        if (input.Classifier == BulkPullVerdict.NeedsReview)
            blocked.AddRange(input.ClassifierReasons);
        else if (input.Classifier == BulkPullVerdict.NoRegulation)
            blocked.Add("без регламента (🟠): " + string.Join("; ", input.ClassifierReasons
                .Where(r => r != "чемпионат Израиля")
                .DefaultIfEmpty("регламент (תקנון) не найден")));

        if (input.IsChampionship)
            blocked.Add("чемпионат — разбирает человек");

        if (input.DaysInFile != 1 && !blocked.Any(r => r.StartsWith("дней в файле", StringComparison.Ordinal)))
            blocked.Add($"дней в файле: {input.DaysInFile} — нужно решение по событию");

        // Бассейн: регламент → справочник (тот же выбор, что в превью «Затянуть», PoolTypeChooser).
        var pool = PoolTypeChooser.Choose(input.RegulationPoolType, null, input.Venue);
        if (pool.PoolType is null)
            blocked.Add("бассейн не задан: " + input.Venue.Describe());

        var distinct = blocked.Distinct().ToList();
        return distinct.Count > 0
            ? new(ResultsAutoVerdicts.Blocked, distinct)
            : new(ResultsAutoVerdicts.WouldTake, [], pool.PoolType, pool.Source);
    }

    /// <summary>Причины одной строкой («; »), обрезка под колонку; пусто — null.</summary>
    public static string? JoinReasons(IReadOnlyList<string> reasons)
    {
        if (reasons.Count == 0) return null;
        var text = string.Join("; ", reasons.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()));
        if (text.Length == 0) return null;
        return text.Length > MaxReasonLength ? text[..(MaxReasonLength - 1)] + "…" : text;
    }

    /// <summary>
    /// Сообщение сбоя — только первая строка: парсер дописывает к исключению свой отладочный лог
    /// («--- DEBUG LOG ---» и десяток строк), а в чип и колонку нужна причина, а не трасса.
    /// </summary>
    private static IReadOnlyList<string> NonEmpty(IReadOnlyList<string> reasons, string fallback)
    {
        var firstLines = reasons
            .Select(r => r.Split('\n', 2)[0].Trim())
            .Where(r => r.Length > 0)
            .ToList();
        return firstLines.Count > 0 ? firstLines : [fallback];
    }
}
