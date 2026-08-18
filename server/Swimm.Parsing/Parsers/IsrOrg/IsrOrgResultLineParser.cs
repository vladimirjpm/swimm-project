using System;
using System.Linq;
using System.Text.RegularExpressions;
using Swimm.Parsing.Models;

namespace Swimm.Parsing.Parsers.IsrOrg;

public static class IsrOrgResultLineParser
{
    // Время заплыва: «мм:сс.дд» и «чч:мм:сс.дд». Часовая форма — не экзотика: длинные
    // дистанции (3 км «в бассейне», открытая вода) печатаются как 00:40:29.16, и без неё
    // токен времени не распознавался, уезжал в НАЗВАНИЕ КЛУБА («בני הרצליה 00:42:41.03»),
    // а результат сохранялся вообще без времени (соревнование 16776, 2026-02-13).
    private static readonly Regex TimeRx = new(@"^(?:\d{1,2}:)?\d{2}:\d{2}\.\d{1,2}$", RegexOptions.Compiled);

    /// <summary>Нулевое время («не плыл») в обеих формах — в результат не идёт.</summary>
    public static bool IsZeroTime(string t) =>
        t is "00:00.00" or "00:00.0" or "00:00:00.00" or "00:00:00.0";

    // Маркер срыва результата (дисквалификация / неявка / не финишировал). Раньше
    // "DNS"/"DNF" не распознавались и прилипали к названию клуба ("… DNS", "… 10.2 SW / DNF").
    private static readonly Regex FailMarkerRx = new(@"^(DQ|DNS|DNF|NS)$", RegexOptions.Compiled);

    // Фрагменты заметки правила вокруг маркера: "DQ / SW 7.1" в нормальном
    // порядке токенов, "7.1 SW / DQ" — после RTL-реверса ивритской строки.
    private static readonly Regex NoteFragmentRx = new(@"^(SW|/|\d+\.\d+)$", RegexOptions.Compiled);

    public static IsrOrgResult ParseResultLine(string line)
    {
        var tok = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        object? position = tok[0] == "-" ? null : int.Parse(tok[0]);
        int heat = int.Parse(tok[1]);
        int lane = int.Parse(tok[2]);

        int idxYear = Array.FindIndex(tok, 3, t => Regex.IsMatch(t, @"^\d{4}$"));
        int birth = int.Parse(tok[idxYear]);

        int idxTime = -1;
        for (int i = idxYear + 1; i < tok.Length; i++)
        {
            if (TimeRx.IsMatch(tok[i]))
            {
                idxTime = i;
                break;
            }
        }

        int nextResultStart = tok.Length;
        for (int i = idxYear + 1; i < tok.Length - 2; i++)
        {
            bool isPositionOrDash = tok[i] == "-" || (int.TryParse(tok[i], out var posCandidate) && posCandidate <= 100);
            bool isHeat = int.TryParse(tok[i + 1], out var heatCandidate) && heatCandidate >= 1 && heatCandidate <= 50;
            bool isLane = int.TryParse(tok[i + 2], out var laneCandidate) && laneCandidate >= 0 && laneCandidate <= 10;

            if (isPositionOrDash && isHeat && isLane)
            {
                if (!TimeRx.IsMatch(tok[i]) && !tok[i].Contains(':'))
                {
                    if (i > idxTime)
                    {
                        nextResultStart = i;
                        break;
                    }
                }
            }
        }

        int idxPoints = -1;
        for (int i = nextResultStart - 1; i > idxYear; i--)
        {
            if (int.TryParse(tok[i], out var pointsCandidate) && pointsCandidate >= 0)
            {
                idxPoints = i;
                break;
            }
        }

        int points = 0;
        if (idxPoints > 0)
        {
            int.TryParse(tok[idxPoints], out points);
        }

        string? time = null;
        string? timeFailNote = null;
        int idxNoteStart = -1;
        int idxNoteEnd = -1;

        if (idxTime >= 0)
        {
            var timeTok = tok[idxTime];
            if (!IsZeroTime(timeTok))
            {
                time = timeTok;
            }

            int idxSlash = Array.FindIndex(tok, idxTime + 1, t => t == "/");

            if (idxSlash >= 0 && idxSlash + 1 < idxPoints)
            {
                var noteParts = tok[(idxSlash + 1)..idxPoints];
                if (noteParts.Length > 0)
                {
                    timeFailNote = string.Join(' ', noteParts);
                }
            }
        }
        else
        {
            int idxMarker = Array.FindIndex(tok, idxYear + 1, t => FailMarkerRx.IsMatch(t));
            if (idxMarker >= 0 && idxMarker < nextResultStart)
            {
                // Собираем фрагменты заметки по обе стороны от маркера и приводим
                // к каноничному порядку (маркер первым) независимо от ориентации.
                idxNoteStart = idxMarker;
                idxNoteEnd = idxMarker;
                while (idxNoteStart - 1 > idxYear && NoteFragmentRx.IsMatch(tok[idxNoteStart - 1])) idxNoteStart--;
                while (idxNoteEnd + 1 < nextResultStart && NoteFragmentRx.IsMatch(tok[idxNoteEnd + 1])) idxNoteEnd++;

                var noteTok = tok[idxNoteStart..(idxNoteEnd + 1)];
                if (idxMarker == idxNoteEnd && idxNoteStart < idxNoteEnd)
                {
                    Array.Reverse(noteTok);
                }
                timeFailNote = string.Join(' ', noteTok);
            }
        }

        string firstEn = string.Empty;
        string lastEn = string.Empty;
        if (idxYear > 3)
        {
            firstEn = tok[idxYear - 1];
            lastEn = string.Join(' ', tok[3..(idxYear - 1)]);
        }

        // Название клуба собираем ТОЛЬКО из «словесных» токенов: всё, что состоит из цифр
        // и пунктуации без единой буквы, — это остаток числовых колонок, а не часть имени.
        // Живой случай (comp 6592, «ליגה 3 הפועל ירושלים», 50 баттерфляй): протокол напечатал
        // ячейку времени искажённой — «2/.00:28». Токен не подошёл ни под время, ни под
        // DQ/NS, уехал в клуб — и импорт завёл клуб «הפועל בית שמש 2/.00:28», а с ним и
        // ВТОРОГО пловца-двойника (ключ пловца включает клуб). Мусор не выбрасываем молча:
        // если времени нет, он становится заметкой — иначе строка выглядит обычной неявкой.
        // Клубы с цифрами В СЛОВЕ («M25», «הפועל H2O כפר שמריהו») правило не задевает:
        // в них есть буквы.
        string club = string.Empty;
        var junk = Array.Empty<string>();
        if (idxTime > idxYear + 1)
        {
            (club, junk) = SplitClubTokens(tok[(idxYear + 1)..idxTime]);
        }
        else if (idxTime < 0 && idxPoints > idxYear + 1)
        {
            if (idxNoteStart > idxYear + 1)
            {
                (club, junk) = SplitClubTokens(tok[(idxYear + 1)..idxNoteStart]);
            }
            else if (idxNoteStart < 0)
            {
                (club, junk) = SplitClubTokens(tok[(idxYear + 1)..idxPoints]);
            }
        }

        // Время не распозналось, а в его колонке что-то стояло — сохраняем это «что-то»
        // заметкой. Правило проекта: ошибку протокола не чиним и не додумываем, помечаем.
        if (time == null && timeFailNote == null && junk.Length > 0)
        {
            timeFailNote = string.Join(' ', junk);
        }

        // Клуб, перенесённый на соседнюю строку: после склейки строк его токены
        // оказываются в хвосте, за очками. Подбираем буквенные токены хвоста.
        if (club.Length == 0 && idxTime < 0 && idxPoints > 0 && idxPoints + 1 < nextResultStart)
        {
            var tail = tok[(idxPoints + 1)..nextResultStart]
                .Where(t => t.Any(char.IsLetter) && !FailMarkerRx.IsMatch(t) && !NoteFragmentRx.IsMatch(t))
                .ToArray();
            if (tail.Length > 0)
            {
                club = string.Join(' ', tail);
            }
        }

        return new IsrOrgResult(
            Country: "",
            Position: position,
            Heat: heat,
            Lane: lane,
            LastName: lastEn,
            FirstName: firstEn,
            BirthYear: birth,
            Club: club,
            Time: time,
            TimeFailNote: timeFailNote,
            InternationalPoints: points,
            IsRelay: false,
            RelayTeamName: null,
            RelaySwimmersName: null,
            RelaySwimmers: null
        );
    }

    /// <summary>Делит токены колонки клуба на само название и числовой мусор
    /// (токен с цифрой и без единой буквы — остаток соседней колонки).</summary>
    private static (string Club, string[] Junk) SplitClubTokens(string[] tokens)
    {
        var words = tokens.Where(t => !IsNumericJunk(t)).ToArray();
        var junk = tokens.Where(IsNumericJunk).ToArray();
        return (string.Join(' ', words), junk);
    }

    private static bool IsNumericJunk(string t) => t.Any(char.IsDigit) && !t.Any(char.IsLetter);

    public static RelaySwimmer ParseRelaySwimmerLine(string line, int order)
    {
        var tok = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int idxYear = Array.FindIndex(tok, t => Regex.IsMatch(t, @"^\d{4}$"));

        int? birth = null;
        if (idxYear >= 0 && int.TryParse(tok[idxYear], out var y)) birth = y;

        // Фамилия — ВСЁ до имени, а не первый токен: ивритские фамилии сплошь и рядом
        // из двух слов («בן יוסף», «אבו ריא», «די קסטרו»). Брали только tok[0] — и нога
        // эстафеты «בן יוסף ניתאי 2012» давала «ניתאי בן», то есть НОВОГО пловца рядом с
        // настоящим: ключ пловца при импорте — фамилия|имя|год. Так в базе завелись
        // сотни «пловцов-теней» без единого личного результата, только с ногами эстафет.
        // Порядок токенов один и тот же в обеих ветках: «Фамилия… Имя Год»
        // (ивритская строка к этому моменту уже нормализована).
        string last = (idxYear >= 2) ? string.Join(' ', tok[..(idxYear - 1)]) : (tok.Length > 0 ? tok[0] : "");
        string first = (idxYear >= 2) ? tok[idxYear - 1] : (tok.Length > 1 ? tok[1] : "");

        return new RelaySwimmer(
            Order: order,
            LastName: last,
            FirstName: first,
            BirthYear: birth,
            Club: null,
            SplitTime: null
        );
    }
}
