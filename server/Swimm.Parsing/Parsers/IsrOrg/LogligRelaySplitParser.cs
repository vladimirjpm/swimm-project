using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Swimm.Application.Mapping;
using Swimm.Parsing.Helpers;
using Swimm.Parsing.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Swimm.Parsing.Parsers.IsrOrg;

/// <summary>Команда эстафеты из PDF «זמני ביניים» одной дисциплины loglig.</summary>
/// <param name="Time">Итог команды как в протоколе («02:34.67»); null — DQ/NS и т. п.</param>
/// <param name="Legs">Ноги в порядке плавания; промежуточное этапа — где источник его дал
/// (у DQ и у команд без промежуточных ноги есть, времён нет).</param>
/// <param name="Band">Полоса мастерсов по СУММЕ возрастов («160-199») из строки
/// «160-199 מאסטרס שליחים» над командами; null — строки полосы нет (не мастерс).</param>
/// <param name="Status">Статус вместо времени («DQ», «NS», «DNF»); null — время есть.</param>
/// <param name="LegNames">Имена ног строкой «имя фамилия» без годов — только у команды со
/// страницы результатов (PDF промежуточных пуст): по ним состав NS-команды ищется в протоколе.</param>
public sealed record SplitRelayTeam(
    int Heat, int Lane, string? Time, string Club, IReadOnlyList<RelaySwimmer> Legs,
    string? Band = null, string? Status = null, bool LegsBorrowed = false,
    IReadOnlyList<string>? LegNames = null);

/// <summary>
/// Разбор PDF промежуточных времён ОДНОЙ эстафетной дисциплины loglig
/// (<c>ExportSwimmingDisciplineResults?leagueId=…&amp;IsSplitResults=True</c>, POST с
/// <c>DisciplineCompetitionId</c>) — docs/relays.md, «Промежуточные эстафет».
///
/// Почему отдельный разборщик, а не основной парсер: PDF всего соревнования с промежуточными
/// другой вёрстки, и основной парсер на нём теряет 67 из 337 событий (замер 19.09.2026 на
/// зимнем чемпионате мастерсов, loglig 13805). Пособытийный файл эстафеты аккуратный:
/// строка команды («1 מיקום + 0.90 03:25.30 &lt;клуб&gt; &lt;дорожка&gt; &lt;заплыв&gt;»),
/// шапка колонок и четыре строки ног «этап · нарастающим · реакция · год · имя · фамилия».
///
/// Тонкости вёрстки (все пойманы на живых файлах):
/// - иврит PdfPig отдаёт в ВИЗУАЛЬНОМ порядке: буквы слова перевёрнуты, слова идут слева
///   направо — разворачиваем буквы и читаем справа налево;
/// - имя и фамилия — разные колонки; граница берётся из шапки («פרטי» / «משפחה»), а не из
///   порядка слов: у «דגנית ברילר גולן» фамилия из двух слов, у «הייא חאג יחיא» тоже;
/// - длинная фамилия переносится на строку ВЫШЕ и НИЖЕ строки ноги («מרמור» / «סירוטה»,
///   «גוסטמלסק» / «י»): обрывок до двух букв приклеивается без пробела, длиннее — через пробел,
///   но после конечной буквы (ץ ך ם ן ף) слово кончилось — «רבינוביץ» / «בץ» через пробел;
/// - у снятой команды на высоте переноса левее стоят «DQ / SW» и «10.11» — обрывок берём из
///   слов зоны имён, а не только из строки, целиком лежащей в ней (6595 / 58158);
/// - команда может начаться внизу страницы, а ноги уйти на следующую — состояние сквозное.
/// Собралось не ровно четыре ноги — команда возвращается без ног (fail-safe, без догадок).
/// Над командами стоит строка полосы мастерсов («160-199 מאסטרס שליחים») — она уходит в
/// <see cref="SplitRelayTeam.Band"/>; у DQ и у команд без промежуточных ноги есть, времён нет.
/// </summary>
public static class LogligRelaySplitParser
{
    // Дробная часть бывает и одной цифрой: «02:17.0» (Маккабия мастерс 2026) — без этого время уезжало
    // в название клуба, а у команды не оставалось ни времени, ни статуса.
    // Часы тоже бывают: эстафета открытой воды 4X1500 плывётся полтора часа — «1:30:08.17»
    // (Эйлат-2026, 07.10.2026); без них у всех команд пропадало время, и доклейка состава
    // по ключу «заплыв + дорожка + время» не находила ни одной.
    private static readonly Regex TimeRx = new(@"^(?:\d{1,2}:)?\d{1,2}:\d{2}\.\d{1,2}$", RegexOptions.Compiled);
    private static readonly Regex YearRx = new(@"^(19|20)\d{2}$", RegexOptions.Compiled);
    private static readonly Regex IntRx = new(@"^\d{1,2}$", RegexOptions.Compiled);
    private static readonly Regex BandRx = new(@"^\d{2,3}-\d{2,3}$", RegexOptions.Compiled);
    private static readonly string[] Statuses = { "DQ", "NS", "DNS", "DNF" };

    private const string PlaceWordVisual = "םוקימ"; // «מיקום» в визуальном порядке PdfPig
    private const string FirstNameHeaderVisual = "יטרפ"; // «פרטי»
    private const string LastNameHeaderVisual = "החפשמ"; // «משפחה»

    /// <summary>Слова шапки колонок ног в визуальном порядке: «שם», «שנת», «לידה», «פרטי», «משפחה».</summary>
    private static readonly HashSet<string> HeaderWordsVisual = ["םש", "תנש", "הדיל", FirstNameHeaderVisual, LastNameHeaderVisual];

    /// <summary>
    /// Дальность обрывка имени от строки ноги. Живые файлы: ±4 pt («גוסטמלסק»/«י») и до
    /// ±10 pt («BEN» / «SHOHA» / «M» — при 9.5 терялся то верхний, то нижний кусок, и одна
    /// пловчиха заводилась дважды: «BEN SHOHA» и «SHOHAM»). Ноги идут с шагом от 12 pt, а
    /// обрывок достаётся только БЛИЖАЙШЕЙ ноге — соседнюю он не захватит.
    /// </summary>
    private const double WrapReach = 12;

    public static List<SplitRelayTeam> Parse(Stream pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        var teams = new List<TeamDraft>();

        TeamDraft? current = null;
        string? band = null;
        double nameBoundaryX = 519; // уточняется по шапке колонок на каждой странице
        double nameZoneX = 490;

        foreach (var page in doc.GetPages())
        {
            var rows = page.GetWords()
                .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 2.0))
                .OrderByDescending(g => g.Key)
                .Select(g => new Row(g.Key * 2, g.OrderBy(w => w.BoundingBox.Left).ToList()))
                .ToList();

            var firstHeader = rows.SelectMany(r => r.Words).FirstOrDefault(w => w.Text == FirstNameHeaderVisual);
            var lastHeader = rows.SelectMany(r => r.Words).FirstOrDefault(w => w.Text == LastNameHeaderVisual);
            if (firstHeader != null && lastHeader != null)
            {
                nameBoundaryX = (firstHeader.BoundingBox.Left + lastHeader.BoundingBox.Left) / 2;
                nameZoneX = firstHeader.BoundingBox.Left - 12;
            }

            // Строки ног и строки-обрывки имён собираем по странице целиком: обрывок может
            // лежать и выше, и ниже своей ноги.
            var legRows = new List<(Row Row, TeamDraft Team)>();
            var fragments = new List<Row>();

            foreach (var row in rows)
            {
                // Строка полосы: «160-199 מאסטרס שליחים» — действует до следующей такой строки.
                var bandWord = row.Words.FirstOrDefault(w => BandRx.IsMatch(w.Text));
                if (bandWord != null && row.Words.Count <= 4 && !row.Words.Any(w => TimeRx.IsMatch(w.Text)))
                {
                    band = bandWord.Text;
                    continue;
                }

                if (row.Words.Any(w => w.Text == PlaceWordVisual || w.Text == "מיקום"))
                {
                    current = TeamDraft.FromRow(row);
                    current.Band = band;
                    teams.Add(current);
                    continue;
                }

                var times = row.Words.Where(w => TimeRx.IsMatch(w.Text)).ToList();
                var hasYear = row.Words.Any(w => YearRx.IsMatch(w.Text));
                // Строка ноги — по году рождения: у DQ и у команд без промежуточных времён в ней
                // нет, а нога есть (имя нужно, когда эстафеты строятся целиком — этап 2).
                if (current != null && hasYear)
                {
                    legRows.Add((row, current));
                    continue;
                }

                // Обрывок имени: слова в зоне имён, без чисел. Берём их и из строки, где левее
                // напечатано что-то ещё: у снятой команды на высоте переноса фамилии стоят
                // причина «DQ / SW» и время «10.11» — строка целиком не годилась, и фамилия из
                // двух слов теряла хвост вместе с головой (6595 / 58158: «איריס סברנסקי נקש»,
                // «תמר ערמון אריאלי» → нога без фамилии, импорт завёл карточки «איריס», «תמר»).
                // Шапку колонок («שם · שנת לידה · פרטי · משפחה») обрывком не считаем.
                var nameWords = row.Words.Where(w => w.BoundingBox.Left >= nameZoneX).ToList();
                if (nameWords.Count > 0
                    && !nameWords.Any(w => w.Text.Any(char.IsDigit))
                    && !row.Words.Any(w => HeaderWordsVisual.Contains(w.Text)))
                    fragments.Add(row with { Words = nameWords });
            }

            foreach (var (row, team) in legRows)
            {
                var near = fragments
                    .Where(f => Math.Abs(f.Y - row.Y) <= WrapReach
                                && legRows.OrderBy(l => Math.Abs(l.Row.Y - f.Y)).First().Row == row)
                    .ToList();
                team.Legs.Add(BuildLeg(row, near, nameBoundaryX, nameZoneX));
            }
        }

        return teams.Select(t => t.Build()).ToList();
    }

    private static RelaySwimmerDraft BuildLeg(Row row, List<Row> fragments, double boundaryX, double zoneX)
    {
        var times = row.Words.Where(w => TimeRx.IsMatch(w.Text)).ToList();
        var year = row.Words.First(w => YearRx.IsMatch(w.Text));

        // Сверху вниз: обрывок над строкой ноги, сама строка, обрывок под ней.
        var lines = fragments.Append(row).OrderByDescending(r => r.Y).ToList();
        var first = JoinName(lines.Select(l => l.Words.Where(w => w.BoundingBox.Left >= zoneX && w.BoundingBox.Left < boundaryX)));
        var last = JoinName(lines.Select(l => l.Words.Where(w => w.BoundingBox.Left >= boundaryX)));

        return new RelaySwimmerDraft(
            last, first,
            int.Parse(year.Text, CultureInfo.InvariantCulture),
            times.Select(t => t.Text).ToList());
    }

    /// <summary>
    /// Время этапа из времён строки ноги. Строка печатает пары «отрезок · нарастающее у команды»
    /// на каждые 50 м, слева — последняя: у 4×50 пара одна, и левое время и есть этап; у 4×100
    /// их две, у 4×200 — четыре, и левое — только ПОСЛЕДНИЙ отрезок (так и хранилось до
    /// 10.10.2026: 00:32.77 у этапа за 01:03.15, docs/relays.md).
    ///
    /// Этап — разность нарастающих на концах этапов (второе слева время; у первого этапа — само
    /// нарастающее). Разности одной верить нельзя: loglig печатает и сломанные строки
    /// (отрицательный отрезок «-1.-29:00», нарастающие переставлены — loglig 13627); такая
    /// ячейка не время, колонки съезжают, а разность по построению всё равно складывается в
    /// итог команды. Поэтому три сторожа:
    /// - нарастающее на конце — САМОЕ большое время строки (в сломанной это не так);
    /// - этап не короче своего последнего отрезка (левое время);
    /// - если отрезки разобрались все (чётное число времён), их сумма равна разности.
    /// Чётность бывает нарушена и в целой строке: узкая колонка «нарастающее 150» переносит
    /// цифру на строку ниже («01:33.6» + «3»), и PdfPig уносит обрывок в другую строку —
    /// тогда хватает первых двух сторожей.
    /// </summary>
    private static string? LegTime(IReadOnlyList<string> times, IReadOnlyList<string>? previous, bool isFirst)
    {
        if (times.Count < 2) return null;
        if (times.Count == 2) return times[0];

        var ms = times.Select(SwimTime.ParseToMs).ToList();
        if (ms.Any(t => t is null)) return null;
        var end = ms[1]!.Value;
        if (ms.Max() != end) return null;

        int? start = isFirst ? 0 : previous is { Count: >= 2 } ? SwimTime.ParseToMs(previous[1]) : null;
        if (start is not int s) return null;
        var leg = end - s;
        if (leg <= 0 || leg < ms[0]!.Value) return null;

        if (times.Count % 2 == 0 && ms.Where((_, i) => i % 2 == 0).Sum(t => t!.Value) != leg) return null;
        return FormatLegTime(leg);
    }

    /// <summary>Как в источнике: «01:03.15» (минуты двумя цифрами, сотые).</summary>
    private static string FormatLegTime(int ms) =>
        string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}.{2:D2}", ms / 60000, ms / 1000 % 60, ms % 1000 / 10);

    /// <summary>Собирает имя из строк (сверху вниз), каждое слово — в логическом порядке.</summary>
    private static string JoinName(IEnumerable<IEnumerable<Word>> lines)
    {
        var result = "";
        foreach (var lineWords in lines)
        {
            var words = lineWords.ToList();
            if (words.Count == 0) continue;
            var hebrew = words.Any(w => w.Text.Any(IsHebrew));
            var ordered = hebrew
                ? words.OrderByDescending(w => w.BoundingBox.Left).Select(w => Logical(w.Text))
                : words.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text);
            var part = string.Join(' ', ordered);
            if (result.Length == 0) result = part;
            else if (HebrewTextHelper.IsMidWordWrap(result, part)) result += part; // «גוסטמלסק» + «י»
            else result += " " + part;
        }
        return result.Trim();
    }

    private static string Logical(string visual) =>
        visual.Any(IsHebrew) ? new string(visual.Reverse().ToArray()) : visual;

    private static bool IsHebrew(char c) => c is >= (char)0x0590 and <= (char)0x05FF;

    private sealed record Row(double Y, List<Word> Words);

    /// <param name="Times">Времена строки ноги слева направо (см. <see cref="LegTime"/>).</param>
    private sealed record RelaySwimmerDraft(string LastName, string FirstName, int BirthYear, IReadOnlyList<string> Times);

    private sealed class TeamDraft
    {
        public int Heat;
        public int Lane;
        public string? Time;
        public string? Status;
        public string? Band;
        public string Club = "";
        public readonly List<RelaySwimmerDraft> Legs = new();

        public static TeamDraft FromRow(Row row)
        {
            var w = row.Words;
            var timeWord = w.FirstOrDefault(x => TimeRx.IsMatch(x.Text));
            var ints = w.Where(x => IntRx.IsMatch(x.Text)).ToList();
            var draft = new TeamDraft
            {
                Time = timeWord?.Text,
                Status = timeWord == null ? w.FirstOrDefault(x => Statuses.Contains(x.Text))?.Text : null,
            };
            // Дорожка и заплыв — два последних числа строки (самые правые колонки).
            if (ints.Count >= 3)
            {
                draft.Lane = int.Parse(ints[^2].Text, CultureInfo.InvariantCulture);
                draft.Heat = int.Parse(ints[^1].Text, CultureInfo.InvariantCulture);
                var clubFrom = (timeWord ?? w.FirstOrDefault(x => x.Text is "DQ" or "NS" or "DNF"))?.BoundingBox.Right ?? 0;
                var clubTo = ints[^2].BoundingBox.Left;
                draft.Club = string.Join(' ', w
                    .Where(x => x.BoundingBox.Left > clubFrom && x.BoundingBox.Right < clubTo)
                    .OrderByDescending(x => x.BoundingBox.Left)
                    .Select(x => Logical(x.Text)));
            }
            return draft;
        }

        public SplitRelayTeam Build()
        {
            if (Legs.Count != 4)
                return new SplitRelayTeam(Heat, Lane, Time, Club, Array.Empty<RelaySwimmer>(), Band, Status);

            var times = Legs.Select((l, i) => LegTime(l.Times, i > 0 ? Legs[i - 1].Times : null, isFirst: i == 0)).ToList();
            if (!LongLegsAddUp(times)) times = times.Select(_ => (string?)null).ToList();

            var legs = Legs.Select((l, i) => new RelaySwimmer(i + 1, l.LastName, l.FirstName, l.BirthYear, null, times[i])).ToList();
            return new SplitRelayTeam(Heat, Lane, Time, Club, legs, Band, Status);
        }

        /// <summary>
        /// Сторож этапов длиннее 50 м: они обязаны сложиться в итог команды. Источник бывает
        /// сломан сам — у зимнего чемпионата 2025 (loglig 13627, 4×100) loglig напечатал
        /// отрицательные отрезки («-1.-29:00») и перепутал нарастающие местами; такая ячейка не
        /// время, колонки строки съезжают, и разность берёт не те числа. Угадывать порядок не
        /// беремся — у команды тогда промежуточных нет. 4×50 (пара времён) не проверяем: там
        /// этап напечатан прямо, вычислять нечего. Неполный набор у длинных этапов тоже не
        /// годится: разность берётся от соседней строки, и проверить её нечем.
        /// </summary>
        private bool LongLegsAddUp(IReadOnlyList<string?> times)
        {
            if (Legs.All(l => l.Times.Count <= 2)) return true;
            if (times.Any(t => t is null) || SwimTime.ParseToMs(Time) is not int team) return false;
            return times.Sum(t => SwimTime.ParseToMs(t)!.Value) == team;
        }
    }
}
