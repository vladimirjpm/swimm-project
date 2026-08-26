using Swimm.Domain;

namespace Swimm.Application.Mapping;

/// <summary>Строка результата в объёме, нужном проверкам качества.</summary>
public sealed record SuspectCandidateRow(
    long ResultId,
    int SwimmerId,
    string StyleName,
    string Distance,
    string Gender,
    int? TimeMilliseconds,
    DateTime CompetitionDate,
    bool IsRelay,
    bool TimeFail,
    string? AgeGroup,
    /// <summary>Очки FINA этого заплыва — единственная величина, сравнимая между стилями
    /// и дистанциями. Нужны правилу «выброс относительно личных результатов».</summary>
    int? Points = null,
    /// <summary>prelim / final / null (timed final или данные без признака). Различает
    /// сессии в правиле «дубль дисциплины»: у бугрим предварительные и финал плывут в ОДИН
    /// день, и без признака каждый финалист выглядел дублем (1678 ложных пометок на
    /// чемпионате 2026).</summary>
    string? HeatType = null,
    /// <summary>Раунд зачёта источника (timed-final / final / prelim); null — источник их не
    /// различает. Тоже входит в ключ «дубля дисциплины»: у чемпионата «мокдамот и финал»
    /// утренний зачёт возрастных групп и вечерний финал — РАЗНЫЕ соревнования в один день,
    /// и оба помечены HeatType=final (И13, docs/data-integrity.md §10).</summary>
    string? Round = null,
    /// <summary>Номер заплыва в протоколе. Соседи по нему — независимая проверка
    /// правдоподобия времени: ошибка протокола изолирована, а победа в своём заплыве нет.</summary>
    int Heat = 0,
    /// <summary>Пол ПЛОВЦА из карточки (male/female); null или пусто — не заполнен.
    /// Опора правила «пол результата расходится с полом пловца»: она прямее, чем
    /// большинство по заплывам этого соревнования.</summary>
    string? SwimmerGender = null,
    /// <summary>Бассейн соревнования (25m / 50m). Обязателен правилу «быстрее мирового
    /// рекорда»: рекордов на дистанцию ДВА — короткой и длинной воды, и разница между ними
    /// больше, чем разрыв между сильным пловцом и рекордсменом (И-13).</summary>
    string? PoolType = null);

/// <summary>
/// Заплыв пловца из его личной истории (для правила «выброс относительно себя»).
/// Кладётся сервисом: истории нет в скоупе соревнования, она приходит из БД.
/// </summary>
public sealed record PersonalSwim(long ResultId, int Points, DateTime Date);

/// <summary>Вердикт по одной строке: код причины + человекочитаемое пояснение.</summary>
public sealed record SuspectVerdict(long ResultId, string Reason, string Note);

/// <summary>
/// Проверки достоверности результатов соревнования. Ищут ошибки САМОГО источника — то,
/// что нельзя починить парсером: протокол напечатан так, как напечатан. Живой пример —
/// протокол Маккабиады 2026, где у Elisa MOSHKOVITCH на 100 м баттерфляем стоит 00:32.59
/// (её же полтинник — 27.27), и организаторы посчитали из этого времени 4702 очка. Такой
/// заплыв «бьёт» национальный рекорд, хотя данные приехали к нам корректно.
///
/// Чистая функция: I/O (загрузка строк, запись пометок) — на репозитории.
/// Одна строка получает ОДНУ причину — первую сработавшую в порядке убывания надёжности
/// правила, чтобы пометка отвечала «почему» однозначно.
/// </summary>
public static class SuspectResultDetector
{
    /* ── Порог «быстрее, чем плавал кто-либо на планете» ──────────────────────────
     * Мировые рекорды приходят СНАРУЖИ, из того же справочника Records, что показывает
     * попап Normative Info (см. WorldBestReference). Своя копия в коде разъезжается со
     * справочником молча: прежний список из 36 строк содержал 20 рекордов длинной воды,
     * 3 короткой и 13 протухших — и про бассейн не знал вовсе (docs/data-integrity.md, И-13).
     *
     * Правило грубое и потому надёжное: ловит не «слишком быстро для этого пловца», а
     * «быстрее всех на планете» — возраст и уровень тут не нужны. Пол в ключе обязателен:
     * женские рекорды на 5–8% медленнее мужских, и по мужскому порогу женские ошибки
     * проходят незамеченными (00:53.42 на 100 м баттерфляем у Ophir RAKAH: быстрее
     * женского рекорда, но медленнее мужского).
     */

    /// <summary>
    /// Запаса нет: помечаем всё быстрее мирового рекорда. Настоящий новый мировой рекорд на
    /// израильском протоколе — событие раз в годы, и пометка с него снимается одной кнопкой;
    /// пропущенная же ошибка молча «бьёт» национальные рекорды. Запас в 5% как раз и
    /// прятал 00:53.42 выше.
    /// </summary>
    private const double WorldBestTolerance = 1.0;

    /// <summary>Во сколько раз время должно выбиваться из медианы заплыва.</summary>
    private const double OutlierFactor = 0.6;

    /// <summary>
    /// Второе условие того же правила: время должно быть оторвано и от БЛИЖАЙШЕГО соседа —
    /// второго результата ступени.
    ///
    /// Зачем (калибровка на живой базе 2026-08-23): в детских лигах разброс внутри ступени
    /// двукратный (9-10 лет, 50 вольным: 46.89 … 1:41.14), поэтому лидер там всегда ниже
    /// 0.6 медианы — просто потому, что он умеет плавать, а половина группы ещё нет. По
    /// одной медиане правило дало 11 пометок на всю базу, и настоящая ошибка среди них
    /// одна (00:32.59 на 100 баттерфляем), да и ту ловит правило мирового рекорда. Ложные
    /// же — обычные быстрые дети, вплоть до ЧЕТЫРЁХ помеченных девочек в одном заплыве.
    ///
    /// Ошибка протокола изолирована: время отрезка вместо всей дистанции даёт ~0.5 от
    /// соседнего результата, опечатка в минутах — и того меньше. Настоящий сильный ребёнок
    /// от второго места отрывается на проценты, а не в разы (0.61 … 1.08 у всех 11 находок).
    /// Порог 0.55 покрывает «половину дистанции» с запасом и оставляет их все в покое.
    /// </summary>
    private const double OutlierGapFactor = 0.55;

    /// <summary>Минимум строк в заплыве, чтобы медиана вообще что-то значила.</summary>
    private const int MinRowsForMedian = 4;

    /* ── Правило «выброс относительно личных результатов» (Б1) ─────────────────────
     * Калибровано на живой базе 2026-08-03 (26 тыс. личных заплывов, 3963 пловца).
     *
     * Метрика — очки FINA: единственное, что сравнимо между стилями и дистанциями.
     * Сравниваем с ЛУЧШИМ личным заплывом, а не с медианой: у новичка медиана крошечная,
     * и любой удачный старт даёт кратный выброс — по медиане с порогом 1.4 набиралось
     * 1528 «находок», то есть чистый крик волком.
     *
     * Порог 2.0 против личного лучшего внутри окна ±120 дней даёт 5 находок на всю базу,
     * и обе известные ошибки внутри: 00:32.59 на 100 баттерфляем (ratio 6.53) и
     * 01:53.09 на 200 вольным (2.17). Для сравнения 1.5 дало бы 20 строк, 1.4 — 33;
     * ослаблять будем по фактам, когда разберём эти пять.
     *
     * Окно нужно от подросткового прогресса: за год 13-летний легально прибавляет
     * 10–15%, и сравнение с прошлогодним результатом ловило бы рост, а не ошибку.
     */
    private const double PersonalOutlierFactor = 2.0;
    private const int PersonalWindowDays = 120;

    /// <summary>Минимум своих заплывов в окне: по одному-двум профиль не построить.</summary>
    private const int MinPersonalSwims = 3;

    /* ── Страховка «согласовано со своим заплывом» ────────────────────────────────
     * Правило «выброс относительно себя» строит личный уровень по ЛЮБЫМ дисциплинам —
     * иначе у него не было бы данных. Но специализация выглядит как выброс: мастерс
     * 1977 г.р. на Маккабиаде плыл 50 вольным за 31.95 (256 очков) при своих же
     * 50 баттерфляем 41.06 (82), 100 вольным 1:17.46 (74) и 50 на спине 47.75 (76) —
     * формально «втрое выше собственного уровня», фактически обычный спринтер.
     *
     * Отличает их протокол: он выиграл СВОЙ заплыв у соседей 32.84 / 34.47 / 35.17,
     * то есть время согласовано с тем, что видели судьи. Настоящая ошибка изолирована
     * и в заплыве: 01:53.09 на 200 вольным у 13-летнего стоит при ближайшем соседе
     * 02:28.82 — 0.76 от него.
     *
     * Поэтому: время, отстающее от лучшего соседа по заплыву не более чем на 10%,
     * считаем подтверждённым протоколом и не метим.
     */
    private const double HeatPlausibleFactor = 0.9;

    /// <summary>Минимум соседей по заплыву, чтобы их времена что-то значили.</summary>
    private const int MinHeatPeers = 3;

    /// <param name="personalHistory">
    /// Заплывы пловцов по их Id — своя история за пределами этого соревнования тоже.
    /// null/пусто — правило «выброс относительно себя» просто не работает (остальные живут).
    /// </param>
    /// <param name="worldBests">
    /// Справочник мировых рекордов из БД (<see cref="WorldBestReference"/>). null/пустой —
    /// правила 1 и 2 молчат: обвинять «быстрее рекорда», не зная рекорда, нельзя.
    /// </param>
    public static List<SuspectVerdict> Detect(
        IReadOnlyCollection<SuspectCandidateRow> rows,
        IReadOnlyDictionary<int, IReadOnlyList<PersonalSwim>>? personalHistory = null,
        WorldBestReference? worldBests = null)
    {
        var verdicts = new Dictionary<long, SuspectVerdict>();

        // Эстафеты вне скоупа: у них время команды, а состав и ноги считаются иначе.
        var timed = rows
            .Where(r => !r.IsRelay && !r.TimeFail && r.TimeMilliseconds is > 0)
            .ToList();
        if (timed.Count == 0) return [];

        void Flag(SuspectCandidateRow row, string reason, string note)
        {
            // Первое сработавшее правило побеждает — порядок вызовов ниже и есть приоритет.
            if (!verdicts.ContainsKey(row.ResultId))
                verdicts[row.ResultId] = new SuspectVerdict(row.ResultId, reason, note);
        }

        var wr = worldBests ?? WorldBestReference.Empty;

        // 1. Быстрее мирового рекорда — самое надёжное правило, идёт первым.
        //    Рекорд берётся ПО БАССЕЙНУ заплыва: короткая вода на 1.5–4% быстрее длинной,
        //    и сверка 25-метрового результата с рекордом 50 м обвиняла нормальные заплывы
        //    (И-13). Если рекорда своего бассейна в справочнике нет, порог берётся из
        //    короткой воды — он мягче — и пометка об этом ГОВОРИТ.
        foreach (var row in timed)
        {
            if (!wr.TryGet(row.Gender, row.StyleName, TrimDistance(row.Distance), row.PoolType,
                    out var best, out var poolNote))
                continue;
            if (row.TimeMilliseconds!.Value >= best * WorldBestTolerance) continue;
            var poolUsed = WorldBestReference.PoolLabel(poolNote is null ? row.PoolType : WorldBestReference.ShortCourse);
            Flag(row, SuspectReasons.TimeVsDistance,
                $"{Fmt(row.TimeMilliseconds.Value)} на {row.Distance} м {row.StyleName} — быстрее мирового рекорда "
                + $"{poolUsed} ({Fmt(best)}){FallbackSuffix(poolNote)}");
        }

        // 2. Время уровня одной ноги эстафеты: быстрее, чем возможно на этой дистанции, но
        //    правдоподобно для вдвое короткой — типичный след того, что в протокол попало
        //    время отрезка, а не заплыва.
        foreach (var row in timed)
        {
            var dist = TrimDistance(row.Distance);
            if (!int.TryParse(dist, out var meters) || meters < 100) continue;
            if (!wr.TryGet(row.Gender, row.StyleName, meters.ToString(), row.PoolType, out var best, out var note))
                continue;
            if (!wr.TryGet(row.Gender, row.StyleName, (meters / 2).ToString(), row.PoolType, out var half, out var halfNote))
                continue;
            var ms = row.TimeMilliseconds!.Value;
            if (ms >= best * WorldBestTolerance) continue;
            if (ms < half * WorldBestTolerance) continue;
            Flag(row, SuspectReasons.RelayTimeInIndividual,
                $"{Fmt(ms)} на {row.Distance} м — похоже на время отрезка {meters / 2} м, а не всей дистанции"
                + FallbackSuffix(note ?? halfNote));
        }

        // 3. Выброс относительно своей дисциплины В СВОЕЙ возрастной ступени.
        //    AgeGroup в ключе обязателен: на детской лиге в одной дисциплине плывут и
        //    восьмилетки (медиана ~59 с), и семнадцатилетние (~30 с). Без ступени медиану
        //    задают младшие, и победители старшей группы становятся «выбросами» — 2026-08-02
        //    так пометились 25.25 и 26.03 на 50 вольным при медиане 43.53 по всем 93 строкам.
        //    Ошибки протокола, ради которых правило и живёт, выбиваются и внутри ступени.
        foreach (var grp in timed.GroupBy(r => (r.StyleName, r.Distance, r.Gender, r.AgeGroup)))
        {
            if (grp.Count() < MinRowsForMedian) continue;
            var sorted = grp.Select(r => r.TimeMilliseconds!.Value).OrderBy(x => x).ToList();
            var median = sorted[sorted.Count / 2];
            // Второе время ступени — мера «оторванности». Сравнивать надо именно с ним:
            // с лучшим временем сравнивать бессмысленно (лучшее — сам кандидат).
            var second = sorted[1];
            foreach (var row in grp)
            {
                var ms = row.TimeMilliseconds!.Value;
                if (ms >= median * OutlierFactor) continue;
                // …и оторвано от ближайшего соседа (см. OutlierGapFactor): иначе правило
                // ловит просто сильных детей в слабой группе.
                var nearest = ms <= second ? second : sorted.Last(x => x < ms);
                if (ms >= nearest * OutlierGapFactor) continue;
                var scope = string.IsNullOrWhiteSpace(row.AgeGroup) ? "дисциплины" : $"ступени {row.AgeGroup}";
                Flag(row, SuspectReasons.TimeOutlier,
                    $"{Fmt(ms)} против медианы {scope} {Fmt(median)} при ближайшем результате {Fmt(nearest)}"
                    + " — быстрее, чем физически правдоподобно");
            }
        }

        // 4. Пол результата расходится с полом пловца.
        //
        // Опора — КАРТОЧКА пловца, если пол в ней заполнен, и лишь затем большинство по
        // его заплывам этого соревнования. Большинство одно не годится: у пловца бывает
        // ровно два старта, и при 1:1 «меньшинством» оказывается случайная строка. Живой
        // случай (comp 1580): у пяти пловцов по два заплыва, один из них с чужим полом, —
        // правило пометило верную строку лишь у четверых, а у טנא יהלי (male по карточке
        // и по 32 другим заплывам) обвинило как раз мужскую строку.
        foreach (var grp in timed.GroupBy(r => r.SwimmerId))
        {
            var byGender = grp
                .Where(r => r.Gender is "male" or "female")
                .GroupBy(r => r.Gender)
                .ToList();
            if (byGender.Count < 2) continue;

            var cardGender = grp
                .Select(r => Normalize(r.SwimmerGender))
                .FirstOrDefault(g => g != null);
            var expected = cardGender ?? byGender.OrderByDescending(g => g.Count()).First().Key;
            var against = cardGender != null ? "по карточке пловца" : "в остальных заплывах пловца";

            foreach (var g in byGender.Where(g => g.Key != expected))
            foreach (var row in g)
                Flag(row, SuspectReasons.GenderMismatch,
                    $"пол '{row.Gender}', тогда как {against} — '{expected}'");
        }

        // 5. Один пловец дважды в одной дисциплине одного дня с разным временем.
        //    Разные ДНИ — норма (повтор дисциплины), поэтому день в ключе. Разные СЕССИИ
        //    одного дня — тоже норма (у бугрим предварительные и финал в один день),
        //    поэтому в ключе и HeatType: prelim и final не сравниваются друг с другом,
        //    дубль внутри одной сессии по-прежнему ловится. Round — по той же причине:
        //    утренний зачёт возрастов и вечерний финал оба «final», но это разные зачёты.
        foreach (var grp in timed.GroupBy(r =>
                     (r.SwimmerId, r.StyleName, r.Distance, r.CompetitionDate.Date, r.HeatType, r.Round)))
        {
            if (grp.Count() < 2) continue;
            if (grp.Select(r => r.TimeMilliseconds).Distinct().Count() < 2) continue;
            var times = string.Join(", ", grp.Select(r => Fmt(r.TimeMilliseconds!.Value)));
            foreach (var row in grp)
                Flag(row, SuspectReasons.DuplicateSwim,
                    $"дисциплина повторяется в один день с разным временем: {times}");
        }

        // 6. Выброс относительно СОБСТВЕННЫХ результатов пловца (Б1). Идёт последним:
        //    правило самое мягкое из автоматических — оно про «так не плавают», а не про
        //    «физически невозможно», и уступает место более надёжным причинам.
        if (personalHistory is { Count: > 0 })
        {
            foreach (var row in timed)
            {
                if (row.Points is not > 0) continue;
                if (!personalHistory.TryGetValue(row.SwimmerId, out var history)) continue;

                var window = history
                    .Where(h => h.ResultId != row.ResultId && h.Points > 0)
                    .Where(h => Math.Abs((h.Date - row.CompetitionDate).TotalDays) <= PersonalWindowDays)
                    .ToList();
                if (window.Count < MinPersonalSwims) continue;

                var best = window.Max(h => h.Points);
                if (row.Points.Value < best * PersonalOutlierFactor) continue;

                // Страховка протоколом: время, согласованное с собственным заплывом,
                // ошибкой не бывает — судьи видели этих людей рядом (HeatPlausibleFactor).
                if (IsPlausibleInHeat(row, timed)) continue;

                Flag(row, SuspectReasons.PersonalOutlier,
                    $"{row.Points} очков против личного лучшего {best} за ±{PersonalWindowDays} дней "
                    + $"({window.Count} заплывов) — вдвое выше собственного уровня");
            }
        }

        return verdicts.Values.OrderBy(v => v.ResultId).ToList();
    }

    /// <summary>Пол в БД живёт как male/female и как M/F — сводим к одному написанию.</summary>
    private static string? Normalize(string? gender) => gender?.Trim().ToLowerInvariant() switch
    {
        "male" or "m" => "male",
        "female" or "f" => "female",
        _ => null,
    };

    /// <summary>
    /// Время подтверждено собственным заплывом: соседи по нему плыли примерно так же.
    /// Заплыв — это то, что судьи видели глазами, поэтому он и служит опорой.
    ///
    /// Ключ заплыва — день + дисциплина + номер заплыва: номер уникален внутри дня одной
    /// дисциплины, а через дни и дисциплины повторяется.
    /// </summary>
    private static bool IsPlausibleInHeat(
        SuspectCandidateRow row, IReadOnlyCollection<SuspectCandidateRow> all)
    {
        // Heat = 0 — источник номера заплыва не дал; опоры нет, правило работает как прежде.
        if (row.Heat <= 0) return false;

        var peers = all
            .Where(r => r.ResultId != row.ResultId
                        && r.Heat == row.Heat
                        && r.StyleName == row.StyleName
                        && r.Distance == row.Distance
                        && r.CompetitionDate.Date == row.CompetitionDate.Date)
            .Select(r => r.TimeMilliseconds!.Value)
            .ToList();
        if (peers.Count < MinHeatPeers) return false;

        return row.TimeMilliseconds!.Value >= peers.Min() * HeatPlausibleFactor;
    }

    /// <summary>Пояснение к пометке, если порог взят не из бассейна заплыва.</summary>
    private static string FallbackSuffix(string? note) => note is null ? string.Empty : $"; {note}";

    /// <summary>Results.Distance приходит и как "100", и как "100m".</summary>
    private static string TrimDistance(string distance)
        => distance.EndsWith('m') ? distance[..^1] : distance;

    private static string Fmt(int ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalMinutes >= 1
            ? $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}"
            : $"{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}";
    }
}
