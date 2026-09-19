using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Swimm.Parsing.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Swimm.Parsing.Parsers.IsrOrg;

/// <summary>Команда эстафеты из PDF «זמני ביניים» одной дисциплины loglig.</summary>
/// <param name="Time">Итог команды как в протоколе («02:34.67»); null — DQ/NS и т. п.</param>
/// <param name="Legs">Ноги в порядке плавания, с промежуточным временем этапа.</param>
public sealed record SplitRelayTeam(int Heat, int Lane, string? Time, string Club, IReadOnlyList<RelaySwimmer> Legs);

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
/// - команда может начаться внизу страницы, а ноги уйти на следующую — состояние сквозное.
/// Собралось не ровно четыре ноги — команда возвращается без ног (fail-safe, без догадок).
/// </summary>
public static class LogligRelaySplitParser
{
    private static readonly Regex TimeRx = new(@"^\d{1,2}:\d{2}\.\d{2}$", RegexOptions.Compiled);
    private static readonly Regex YearRx = new(@"^(19|20)\d{2}$", RegexOptions.Compiled);
    private static readonly Regex IntRx = new(@"^\d{1,2}$", RegexOptions.Compiled);

    private const string PlaceWordVisual = "םוקימ"; // «מיקום» в визуальном порядке PdfPig
    private const string FirstNameHeaderVisual = "יטרפ"; // «פרטי»
    private const string LastNameHeaderVisual = "החפשמ"; // «משפחה»

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
                if (row.Words.Any(w => w.Text == PlaceWordVisual || w.Text == "מיקום"))
                {
                    current = TeamDraft.FromRow(row);
                    teams.Add(current);
                    continue;
                }

                var times = row.Words.Where(w => TimeRx.IsMatch(w.Text)).ToList();
                var hasYear = row.Words.Any(w => YearRx.IsMatch(w.Text));
                if (current != null && times.Count >= 2 && hasYear)
                {
                    legRows.Add((row, current));
                    continue;
                }

                // Обрывок имени: только слова в зоне имён, без чисел.
                if (row.Words.All(w => w.BoundingBox.Left >= nameZoneX && !w.Text.Any(char.IsDigit)))
                    fragments.Add(row);
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
            times[0].Text);
    }

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
            else if (IsMidWordWrap(result, part)) result += part; // «גוסטמלסק» + «י»
            else result += " " + part;
        }
        return result.Trim();
    }

    /// <summary>
    /// Перенос посреди слова или между словами. Обрывок короче трёх букв — почти всегда хвост
    /// слова («גוסטמלסק»+«י», «SHOHA»+«M»), но не когда строка кончается ивритской КОНЕЧНОЙ
    /// буквой (ץ ך ם ן ף): ими слово заканчивается, значит дальше новое слово —
    /// «רבינוביץ»+«בץ» это «רבינוביץ בץ», а не «רבינוביץבץ» (пойман 19.09.2026, мастерс 13805).
    /// </summary>
    private static bool IsMidWordWrap(string before, string part) =>
        part.Length <= 2 && !"ךםןףץ".Contains(before[^1]);

    private static string Logical(string visual) =>
        visual.Any(IsHebrew) ? new string(visual.Reverse().ToArray()) : visual;

    private static bool IsHebrew(char c) => c is >= (char)0x0590 and <= (char)0x05FF;

    private sealed record Row(double Y, List<Word> Words);

    private sealed record RelaySwimmerDraft(string LastName, string FirstName, int BirthYear, string Split);

    private sealed class TeamDraft
    {
        public int Heat;
        public int Lane;
        public string? Time;
        public string Club = "";
        public readonly List<RelaySwimmerDraft> Legs = new();

        public static TeamDraft FromRow(Row row)
        {
            var w = row.Words;
            var timeWord = w.FirstOrDefault(x => TimeRx.IsMatch(x.Text));
            var ints = w.Where(x => IntRx.IsMatch(x.Text)).ToList();
            var draft = new TeamDraft { Time = timeWord?.Text };
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
            IReadOnlyList<RelaySwimmer> legs = Time != null && Legs.Count == 4
                ? Legs.Select((l, i) => new RelaySwimmer(i + 1, l.LastName, l.FirstName, l.BirthYear, null, l.Split)).ToList()
                : Array.Empty<RelaySwimmer>();
            return new SplitRelayTeam(Heat, Lane, Time, Club, legs);
        }
    }
}
