using System.Globalization;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;

namespace Swimm.Application.Mapping;

/// <summary>
/// Сторож правдоподобия времени в диффе рекордов (docs/data-integrity.md И-20,
/// docs/plans/records-quality-plan.md §3).
///
/// Источник сам отдаёт мусор: в отчёте World Aquatics за сентябрь 2026 «мировой рекорд»
/// 100 в/с ж 50 м — 40.11 при мужском около 46 с, и строка помечена Approved. Сторож такое
/// находит, но НИЧЕГО не блокирует и не чинит: наша копия обязана совпадать с источником,
/// иначе следующий импорт молча вернёт всё назад. Находка уходит в реестр спорных рекордов
/// КАНДИДАТОМ (<see cref="RecordIssueStatuses.Candidate"/>) — статус ставит человек.
///
/// Три правила:
/// <list type="number">
/// <item>мировой рекорд улучшен за раз больше чем на <see cref="WorldMaxImprovement"/>;</item>
/// <item>не мировой рекорд (страна, возраст, мастерс) быстрее мирового той же дисциплины;
/// мировой ЮНИОРСКИЙ (<c>world/junior</c>) — тоже: WJR законно медленнее WR, но не быстрее;</item>
/// <item>строка стала МЕДЛЕННЕЕ той, что уже лежит в базе (И-21) — см. <see cref="IsContestedSlot"/>.</item>
/// </list>
/// Порога улучшения для национальных рекордов нет сознательно: у малых федераций скачки на
/// 5–10 % нормальны (редкие дистанции, дырявые наборы). Подбирать его — по данным Фазы 11.
/// Порога ЗАМЕДЛЕНИЯ нет тоже, и по другой причине: рекорд не ходит назад ни на секунду, ни
/// на сотую — любое замедление означает, что источник переписал строку, а насколько сильно,
/// решает уже человек в реестре.
///
/// Замер на живых источниках 2026-09-16: World Aquatics — 86 изменившихся строк, ОДНА
/// находка; мастерсы — 5 изменившихся, три находки (ровно те, что разобраны в И-21);
/// возрастные — ноль. Единственная неточность там же: правило 3 не отличает потерю рекорда
/// от того, как источник ЧИНИТ свою же ошибку — находка на world есть откат 40.11 → 51.68,
/// то есть World Aquatics убирает мусор из И-20. Разбирать человеку в реестре.
/// </summary>
public static class RecordPlausibility
{
    /// <summary>
    /// Больше какой доли мировой рекорд за раз не улучшают. Самые крупные скачки — костюмная
    /// эра 2008–2009, около 2 %; 3 % — с запасом. Живые улучшения 2026 года — 0.5–1.5 %
    /// (1500 в/с м, 50 в/с ж, 50 спина ж), «40.11» против 51.68 — 22 %.
    /// </summary>
    public const double WorldMaxImprovement = 0.03;

    /// <summary>Строка мировой оси для эталона. Категория и полоса нужны мастерсам.</summary>
    public readonly record struct WorldRow(
        string Category, string AgeKey, string Gender, string PoolType, string Style,
        string Distance, string Time);

    /// <summary>
    /// Эталоны для правила 2: из того, что лежит в базе, и того, что приехало в этом диффе,
    /// берётся ЛУЧШЕЕ. Так эталон не портится ни в одну сторону: мусорно-быстрый новый WR
    /// (40.11) ложных находок не даёт — всё медленнее него, а сам он пойман правилом 1;
    /// мусорно-медленный новый WR не превращает честные национальные рекорды в «быстрее
    /// мирового».
    ///
    /// Эталонов в словаре ДВА вида, и это правка 17.09.2026. Раньше мастерский рекорд страны
    /// мерился АБСОЛЮТНЫМ мировым, и правило для мастерсов было почти мёртвым: на 100 в/с ж
    /// 50 м запас между полосой и абсолютом — 5.3 с в полосе 25-29 и 54.5 с в полосе 90-94,
    /// то есть источник мог отдать полминуты бреда и остаться незамеченным. Ось
    /// <c>world/masters</c> загружена (1095 строк), поэтому планка опускается до рекорда ТОЙ
    /// ЖЕ полосы. Замер 17.09: полосы сходятся один в один — все 845 израильских мастерских
    /// строк имеют пару в <c>world/masters</c>.
    ///
    /// Ключи разной формы и не сталкиваются: у полосы впереди <c>masters|&lt;полоса&gt;</c>,
    /// у абсолютного — только дисциплина.
    /// </summary>
    public static Dictionary<string, (int Ms, string Time)> WorldReference(IEnumerable<WorldRow> worldRows)
    {
        var map = new Dictionary<string, (int Ms, string Time)>();
        foreach (var row in worldRows)
        {
            if (SwimTime.ParseToMs(row.Time) is not int ms) continue;

            // Юниорский мировой — не эталон: он сам меряется абсолютным (правило 2 для
            // world/junior). Пусти его в словарь — и в ключ дисциплины он встал бы как
            // «абсолютный», хотя всегда медленнее настоящего.
            if (IsJunior(row.Category)) continue;

            var key = IsMasters(row.Category)
                ? BandKey(row.AgeKey, row.Gender, row.PoolType, row.Style, row.Distance)
                : DisciplineKey(row.Gender, row.PoolType, row.Style, row.Distance);

            if (!map.TryGetValue(key, out var best) || ms < best.Ms) map[key] = (ms, row.Time.Trim());
        }
        return map;
    }

    /// <summary>
    /// Проверяет новые и изменившиеся строки диффа. Время, которое не разбирается, правилам
    /// не подлежит — это другой класс дефекта.
    /// </summary>
    public static IReadOnlyList<RecordSuspiciousEntry> Check(
        IEnumerable<RecordDiffEntry> entries,
        IReadOnlyDictionary<string, (int Ms, string Time)> worldReference)
    {
        var found = new List<RecordSuspiciousEntry>();
        foreach (var e in entries)
        {
            if (SwimTime.ParseToMs(e.NewTime) is not int newMs) continue;

            // Правило 3 идёт первым, потому что оно единственное касается и мировых строк:
            // ветка ниже уходит в continue и до правила 2 их не доводит.
            if (SwimTime.ParseToMs(e.OldTime) is int storedMs && newMs > storedMs && !IsContestedSlot(e))
                found.Add(Finding(e, RecordIssueReasons.SlowerThanStored,
                    $"Рекорд стал МЕДЛЕННЕЕ: {e.OldTime} → {e.NewTime} ({Delta(newMs - storedMs)}). " +
                    "Рекорды назад не ходят — источник либо потерял прежнего держателя, либо " +
                    "переписал строку."));

            if (e.RegionType == "world")
            {
                // Правило 2 для мирового юниорского: рекорд, который могут ставить только
                // 14–18-летние, не бывает быстрее абсолютного (21.09.2026, WJR-план J1).
                if (IsJunior(e.Category)
                    && Lookup(worldReference, DisciplineKey(e.Gender, e.PoolType, e.Style, e.Distance)) is { } wr
                    && newMs < wr.Ms)
                    found.Add(Finding(e, RecordIssueReasons.FasterThanWorldRecord,
                        $"Мировой юниорский {e.NewTime} быстрее абсолютного мирового рекорда той же " +
                        $"дисциплины ({wr.Time}). Юниорский может быть равен абсолютному, но не быстрее."));

                if (SwimTime.ParseToMs(e.OldTime) is not int oldMs || newMs >= oldMs) continue;

                var gain = (oldMs - newMs) / (double)oldMs;
                if (gain > WorldMaxImprovement)
                    found.Add(Finding(e, RecordIssueReasons.ImplausibleImprovement,
                        $"Мировой рекорд улучшен за раз на {Percent(gain)}: {e.OldTime} → {e.NewTime}. " +
                        $"Больше чем на {Percent(WorldMaxImprovement)} мировые рекорды не улучшают — " +
                        "похоже на ошибку ввода у источника."));
                continue;
            }

            // У мастерса планка — рекорд ЕГО полосы; абсолютный остаётся фоллбеком на случай,
            // когда полосы в world/masters нет (эстафеты мы не берём вовсе, у них другая ось).
            var band = IsMasters(e.Category)
                ? Lookup(worldReference, BandKey(e.AgeKey, e.Gender, e.PoolType, e.Style, e.Distance))
                : null;
            var world = band ?? Lookup(worldReference, DisciplineKey(e.Gender, e.PoolType, e.Style, e.Distance));

            if (world is { } reference && newMs < reference.Ms)
                found.Add(Finding(e, RecordIssueReasons.FasterThanWorldRecord, band != null
                    ? $"{e.NewTime} быстрее мирового рекорда мастерсов в полосе {e.AgeKey.Trim()} " +
                      $"({reference.Time}). Рекорд страны не может быть быстрее мирового своей полосы."
                    : $"{e.NewTime} быстрее мирового рекорда той же дисциплины ({reference.Time}). " +
                      "Рекорд страны, возраста или мастерса не может быть быстрее абсолютного."));
        }
        return found;
    }

    /// <summary>
    /// Слот, на который в цепочке <c>--records-refresh</c> пишут ДВА источника (И-13):
    /// <c>country/*/open</c> берут и отчёт NR World Aquatics, и PDF федерации. Там откат
    /// назад — штатная середина цепочки: шаг World Aquatics возвращает своё устаревшее
    /// значение, а следующий шаг кладёт федеральное обратно. Правило 3 такие строки
    /// пропускает — иначе кандидатом становился бы каждый израильский откат, а их в прогоне
    /// World Aquatics десятки, и все до одного возвращает следующий шаг цепочки.
    ///
    /// Остальные слоты однохозяйные: <c>world/open</c> пишет только <c>worldrecords</c>,
    /// <c>world/masters</c> — <c>wa-masters</c>, <c>world/junior</c> — <c>wa-junior</c>,
    /// <c>age</c> — только <c>isrorg-age</c>, <c>country/masters</c> — только <c>isrorg-masters</c>
    /// (<c>RecordDiffService.SourceScopes</c>), и там замедление означает дефект источника.
    /// </summary>
    private static bool IsContestedSlot(RecordDiffEntry e) =>
        e.RegionType.Equals("country", StringComparison.OrdinalIgnoreCase)
        && e.Category.Equals("open", StringComparison.OrdinalIgnoreCase);

    /// <summary>Разница во времени человеческим языком: «+44.41 с».</summary>
    private static string Delta(int ms) =>
        "+" + (ms / 1000.0).ToString("0.##", CultureInfo.InvariantCulture) + " с";

    /// <summary>Пол × бассейн × стиль × дистанция — ось, на которой живёт мировой рекорд.</summary>
    public static string DisciplineKey(string gender, string poolType, string style, string distance) =>
        string.Join('|',
            gender.Trim().ToLowerInvariant(),
            poolType.Trim().ToLowerInvariant(),
            style.Trim().ToLowerInvariant(),
            // Records хранит дистанцию с суффиксом ("100m") — как и в RecordIssueKey.
            distance.Trim().ToLowerInvariant().TrimEnd('m'));

    /// <summary>Полоса мастерса × дисциплина — ось, на которой живёт мировой рекорд мастерсов.</summary>
    public static string BandKey(
        string ageKey, string gender, string poolType, string style, string distance) =>
        "masters|" + ageKey.Trim().ToLowerInvariant() + "|"
        + DisciplineKey(gender, poolType, style, distance);

    private static bool IsJunior(string category) =>
        category.Trim().Equals("junior", StringComparison.OrdinalIgnoreCase);

    private static bool IsMasters(string category) =>
        category.Trim().Equals("masters", StringComparison.OrdinalIgnoreCase);

    private static (int Ms, string Time)? Lookup(
        IReadOnlyDictionary<string, (int Ms, string Time)> reference, string key) =>
        reference.TryGetValue(key, out var found) ? found : null;

    private static RecordSuspiciousEntry Finding(RecordDiffEntry e, string reason, string note) =>
        new(e.RegionType, e.RegionCode, e.Category, e.AgeKey, e.Gender, e.PoolType, e.Style,
            e.Distance, e.NewTime, reason, note);

    private static string Percent(double share) =>
        (share * 100).ToString("0.#", CultureInfo.InvariantCulture) + " %";
}
