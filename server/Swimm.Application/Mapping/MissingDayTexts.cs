using System.Globalization;

namespace Swimm.Application.Mapping;

/// <summary>
/// Тексты блока «чего не хватает» в карточке /Admin/Competitions и кнопки «Затянуть этот день» (Р123,
/// Р124) — ПРОСТЫМИ словами, все в одном месте. Просьба Влада 25.11.2026: текст отвечает на вопрос «что
/// нажать, чтобы всё было правильно», без жаргона («долг», «кандидат», «вердикт», «Р115», «loglig-id»).
/// Номер loglig — только мелкой второй строкой (её рисует страница). Тесты — <c>MissingDayTextsTests</c>.
/// </summary>
public static class MissingDayTexts
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Дни недели по падежам: именительный, винительный, родительный, предложный; род — для «нашлась/нашёлся».
    private static readonly (string Nom, string Acc, string Gen, string Prep, char Gender)[] Weekdays =
    [
        ("воскресенье", "воскресенье", "воскресенья", "воскресенье", 'n'),
        ("понедельник", "понедельник", "понедельника", "понедельнике", 'm'),
        ("вторник", "вторник", "вторника", "вторнике", 'm'),
        ("среда", "среду", "среды", "среде", 'f'),
        ("четверг", "четверг", "четверга", "четверге", 'm'),
        ("пятница", "пятницу", "пятницы", "пятнице", 'f'),
        ("суббота", "субботу", "субботы", "субботе", 'f'),
    ];

    private static readonly string[] OrdinalNom = ["первый", "второй", "третий", "четвёртый", "пятый", "шестой", "седьмой"];
    private static readonly string[] OrdinalIns = ["первым", "вторым", "третьим", "четвёртым", "пятым", "шестым", "седьмым"];
    private static readonly string[] CountIns = ["", "одним", "двумя", "тремя", "четырьмя", "пятью", "шестью", "семью"];
    private static readonly string[] CountNom = ["", "один", "два", "три", "четыре", "пять", "шесть", "семь"];
    private static readonly string[] DaysAdj = ["", "однодневным", "двухдневным", "трёхдневным", "четырёхдневным", "пятидневным", "шестидневным", "семидневным"];

    public static string Nom(DateOnly d) => Weekdays[(int)d.DayOfWeek].Nom;
    public static string Acc(DateOnly d) => Weekdays[(int)d.DayOfWeek].Acc;
    public static string Gen(DateOnly d) => Weekdays[(int)d.DayOfWeek].Gen;
    public static string Prep(DateOnly d) => Weekdays[(int)d.DayOfWeek].Prep;

    public static string Day(DateOnly d) => d.ToString("dd.MM", Inv);
    public static string FullDay(DateOnly d) => d.ToString("dd.MM.yyyy", Inv);

    /// <summary>«06.12 и 07.12», «05.12, 06.12 и 07.12».</summary>
    public static string List(IEnumerable<string> items)
    {
        var l = items.ToList();
        return l.Count <= 1 ? string.Concat(l) : string.Join(", ", l.Take(l.Count - 1)) + " и " + l[^1];
    }

    public static string Plural(int n, string one, string few, string many)
    {
        var m100 = Math.Abs(n) % 100;
        var m10 = m100 % 10;
        if (m100 is >= 11 and <= 14) return many;
        return m10 switch { 1 => one, >= 2 and <= 4 => few, _ => many };
    }

    public static string Rows(int n) => $"{n} {Plural(n, "строка", "строки", "строк")}";

    private static string Pick(string[] words, int i, string fallback) => i >= 0 && i < words.Length && words[i].Length > 0 ? words[i] : fallback;
    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0], CultureInfo.GetCultureInfo("ru-RU")) + s[1..];

    /// <summary>Два дня в сумме — говорим днями недели («субботу 07.12»); больше — просто датой.</summary>
    private static bool ByWeekday(int totalDays) => totalDays == 2;

    // ── Найден недостающий день (кнопка «Затянуть…») ─────────────────────────

    /// <summary>Сколько дней будет после затягивания и каким по счёту станет новый день (0 — первый).</summary>
    private static (int Total, int Index) Position(IReadOnlyList<DateOnly> dbDays, DateOnly day)
    {
        var all = dbDays.Append(day).Distinct().Order().ToList();
        return (all.Count, all.IndexOf(day));
    }

    /// <summary>«Второй день есть у федерации, но у нас его нет».</summary>
    public static string FoundHeadline(IReadOnlyList<DateOnly> dbDays, DateOnly day)
    {
        var (_, i) = Position(dbDays, day);
        return $"{Cap(Pick(OrdinalNom, i, "Ещё один"))} день есть у федерации, но у нас его нет";
    }

    /// <summary>
    /// «Федерация выложила пятницу и субботу как два отдельных соревнования. У нас в базе только пятница
    /// 06.12. Суббота 07.12 нашлась отдельно.»
    /// </summary>
    public static string FoundText(IReadOnlyList<DateOnly> dbDays, DateOnly day)
    {
        var db = dbDays.Distinct().Order().ToList();
        var all = db.Append(day).Distinct().Order().ToList();
        if (ByWeekday(all.Count))
        {
            var w = Weekdays[(int)day.DayOfWeek];
            var found = w.Gender switch { 'f' => "нашлась", 'n' => "нашлось", _ => "нашёлся" };
            return $"Федерация выложила {Acc(all[0])} и {Acc(all[1])} как два отдельных соревнования. "
                + $"У нас в базе только {Nom(db[0])} {Day(db[0])}. {Cap(Nom(day))} {Day(day)} {found} отдельно.";
        }
        return "Федерация выложила дни этого старта как отдельные соревнования. "
            + $"У нас в базе только {List(db.Select(Day))}. День {Day(day)} нашёлся отдельно.";
    }

    /// <summary>«Затянуть субботу 07.12» (два дня) / «Затянуть 07.12».</summary>
    public static string PullButton(IReadOnlyList<DateOnly> dbDays, DateOnly day) =>
        ByWeekday(Position(dbDays, day).Total) ? $"Затянуть {Acc(day)} {Day(day)}" : $"Затянуть {Day(day)}";

    /// <summary>
    /// Под кнопкой: «Мы скачаем результаты субботы и добавим их вторым днём. Получится одно соревнование
    /// с двумя днями: 06.12 и 07.12. Перед записью вы увидите, сколько строк и какой бассейн.»
    /// </summary>
    public static string PullNote(IReadOnlyList<DateOnly> dbDays, DateOnly day)
    {
        var (total, i) = Position(dbDays, day);
        var all = dbDays.Append(day).Distinct().Order().Select(Day);
        var whose = ByWeekday(total) ? Gen(day) : $"за {Day(day)}";
        return $"Мы скачаем результаты {whose} и добавим их {Pick(OrdinalIns, i, "ещё одним")} днём. "
            + $"Получится одно соревнование с {Pick(CountIns, total, total.ToString(Inv))} днями: {List(all)}. "
            + "Перед записью вы увидите, сколько строк и какой бассейн.";
    }

    /// <summary>Рядом с «Принять как есть», пока день не затянут.</summary>
    public static string AcceptWarning(IReadOnlyList<DateOnly> dbDays, DateOnly day) =>
        ByWeekday(Position(dbDays, day).Total)
            ? $"Не нажимайте, пока не затянули {Acc(day)}: отметка исчезнет, а результатов {Gen(day)} на сайте так и не будет."
            : $"Не нажимайте, пока не затянули {Day(day)}: отметка исчезнет, а результатов за {Day(day)} на сайте так и не будет.";

    // ── Превью и итог ────────────────────────────────────────────────────────

    /// <summary>Метки, которые страница заменяет на живой выбор в превью.</summary>
    public const string PoolToken = "{pool}", NameToken = "{name}";

    /// <summary>
    /// Над «Импортировать»: «Будет добавлен второй день: суббота 07.12.2024, 1784 строки, бассейн {pool}.
    /// Соревнование станет двухдневным и будет называться «{name}» (название можно поправить).»
    /// <paramref name="nameEditable"/> = false — день ложится в уже существующее событие, имя его.
    /// </summary>
    public static string ImportSummaryTemplate(IReadOnlyList<DateOnly> dbDays, DateOnly day, int rows, bool nameEditable)
    {
        var (total, i) = Position(dbDays, day);
        return $"Будет добавлен {Pick(OrdinalNom, i, "ещё один")} день: {Nom(day)} {FullDay(day)}, {Rows(rows)}, бассейн {PoolToken}. "
            + $"Соревнование станет {Pick(DaysAdj, total, $"{total}-дневным")} и будет называться «{NameToken}»"
            + (nameEditable ? " (название можно поправить)." : ".");
    }

    /// <summary>Над превью: откуда взяты галочки.</summary>
    public static string DefaultsNote(DateOnly existingDay) =>
        $"Бассейн, флаги и категории взяты как у уже затянутого дня {Day(existingDay)} — поправьте, если нужно.";

    /// <summary>«пятницы» / «дня 06.12» — кто это, в родительном падеже.</summary>
    private static string Whose(DateOnly d, bool byWeekday) => byWeekday ? Gen(d) : $"дня {Day(d)}";

    /// <summary>
    /// Бассейн у затянутого дня и в регламенте нового разный: «У пятницы в базе 25 м, а в регламенте
    /// субботы 50 м. Мы добавим субботу только с тем же бассейном, что у пятницы. Если у пятницы бассейн
    /// записан неверно — сначала исправьте его через «Изменить».» Сервер примет только длину затянутого дня.
    /// </summary>
    public static string PoolConflict(DateOnly existingDay, string? existingPool, DateOnly newDay, string? regulationPool,
        IReadOnlyList<DateOnly> dbDays)
    {
        var w = ByWeekday(Position(dbDays, newDay).Total);
        var newAcc = w ? Acc(newDay) : $"день {Day(newDay)}";
        return $"⚠ У {Whose(existingDay, w)} в базе {Pool(existingPool)}, а в регламенте {Whose(newDay, w)} {Pool(regulationPool)}. "
            + $"Мы добавим {newAcc} только с тем же бассейном, что у {Whose(existingDay, w)}. "
            + $"Если у {Whose(existingDay, w)} бассейн записан неверно — сначала исправьте его через «Изменить».";
    }

    /// <summary>Отказ записи: выбран бассейн не как у затянутого дня.</summary>
    public static string PoolMismatch(DateOnly existingDay, string existingPool, DateOnly newDay, string chosenPool,
        IReadOnlyList<DateOnly> dbDays)
    {
        var w = ByWeekday(Position(dbDays, newDay).Total);
        var newAcc = w ? Acc(newDay) : $"день {Day(newDay)}";
        return $"у {Whose(existingDay, w)} в базе {Pool(existingPool)}, а выбрано {Pool(chosenPool)}. Мы добавляем {newAcc} только с тем же "
            + $"бассейном, что у {Whose(existingDay, w)}. Если у {Whose(existingDay, w)} бассейн записан неверно — сначала исправьте его через «Изменить»";
    }

    /// <summary>В протоколе нет ни заплывов с дорожками, ни времени — сверку «тот же протокол?» сделать нечем.</summary>
    public const string OverlapNotChecked =
        "⚠ Не смогли проверить, что это другой день, а не тот же протокол: в файле нет заплывов и дорожек. "
        + "Посмотрите дату и строки сами.";

    /// <summary>«25 м» / «не выбран».</summary>
    public static string Pool(string? pool) => SiblingDayRules.NormalizePool(pool) is { } p ? p.TrimEnd('m') + " м" : "не выбран";

    /// <summary>После успеха: «Готово. Теперь у соревнования два дня: 06.12 (1555 строк) и 07.12 (1784 строки).»</summary>
    public static string Done(IReadOnlyList<(DateOnly Date, int Rows)> days)
    {
        var d = days.OrderBy(x => x.Date).ToList();
        var count = Pick(CountNom, d.Count, d.Count.ToString(Inv));
        return $"Готово. Теперь у соревнования {count} {Plural(d.Count, "день", "дня", "дней")}: "
            + List(d.Select(x => $"{Day(x.Date)} ({Rows(x.Rows)})")) + ".";
    }

    /// <summary>День записан, но к остальным не присоединён.</summary>
    public static string NotJoined(DateOnly day, IReadOnlyList<DateOnly> dbDays, string why, string joinCommand) =>
        $"⚠ День {Day(day)} записан, но отдельным соревнованием — присоединить его к "
        + $"{List(dbDays.Distinct().Order().Select(Day))} не вышло ({why}). Попросите агента собрать дни: {joinCommand}.";

    /// <summary>Импорт дня ничего не записал (отказ, откат, задача упала).</summary>
    public static string NotImported(DateOnly day, string why) =>
        $"⚠ Не получилось: результаты {Day(day)} не записаны ({why}). Соревнование осталось как было — можно нажать кнопку ещё раз.";

    /// <summary>Импорт прошёл, но новый день потом не нашёлся.</summary>
    public static string NotFoundAfterImport(DateOnly day) =>
        $"⚠ Результаты {Day(day)} записаны, но найти новый день после записи не вышло — дни не соединены. Попросите агента проверить.";

    /// <summary>Дни соединены, но пересчёт мест и очков после этого сорвался.</summary>
    public static string JoinedButRecalcFailed(DateOnly day, string recalcCommand) =>
        $"⚠ День {Day(day)} добавлен в соревнование, но пересчёт мест и очков не завершился. Попросите агента пересчитать дни: {recalcCommand}.";

    /// <summary>Отказ — одним предложением: «Не получилось: …».</summary>
    public static string Refused(string why) => $"Не получилось: {why}";

    // ── Два других исхода той же кнопки (Р123) ───────────────────────────────

    /// <summary>Дни склеены: «Оба дня у нас есть, но записаны одной датой.»</summary>
    public static string CollapsedHeadline(int days) =>
        days == 2 ? "Оба дня у нас есть, но записаны одной датой." : "Все дни у нас есть, но записаны одной датой.";

    /// <summary>«Нажмите „Разделить по дням“ — заплывы 28.03 и 29.03 разойдутся по своим дням, ничего не скачивается заново.»</summary>
    public static string CollapsedHint(IEnumerable<DateOnly> days) =>
        $"Нажмите «Разделить по дням» — заплывы {List(days.Distinct().Order().Select(Day))} разойдутся по своим дням, ничего не скачивается заново.";

    /// <summary>Соседних дней нет: даты федерации шире плавания.</summary>
    public const string NothingToPull =
        "У федерации в программе плавания только те дни, что у нас уже есть. Лишние даты — это не плавание "
        + "(например, даты всех Игр). Нажмите «Принять как есть» — отметка исчезнет.";
}
