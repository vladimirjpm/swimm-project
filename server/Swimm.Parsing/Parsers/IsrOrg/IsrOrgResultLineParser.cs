using System;
using System.Linq;
using System.Text.RegularExpressions;
using Swimm.Parsing.Models;

namespace Swimm.Parsing.Parsers.IsrOrg;

public static class IsrOrgResultLineParser
{
    private static readonly Regex TimeRx = new(@"^\d{2}:\d{2}\.\d{1,2}$", RegexOptions.Compiled);

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
            if (timeTok != "00:00.00" && timeTok != "00:00.0")
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

        string club = string.Empty;
        if (idxTime > idxYear + 1)
        {
            club = string.Join(' ', tok[(idxYear + 1)..idxTime]);
        }
        else if (idxTime < 0 && idxPoints > idxYear + 1)
        {
            if (idxNoteStart > idxYear + 1)
            {
                club = string.Join(' ', tok[(idxYear + 1)..idxNoteStart]);
            }
            else if (idxNoteStart < 0)
            {
                club = string.Join(' ', tok[(idxYear + 1)..idxPoints]);
            }
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

    public static RelaySwimmer ParseRelaySwimmerLine(string line, int order)
    {
        var tok = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int idxYear = Array.FindIndex(tok, t => Regex.IsMatch(t, @"^\d{4}$"));

        int? birth = null;
        if (idxYear >= 0 && int.TryParse(tok[idxYear], out var y)) birth = y;

        string last = tok.Length > 0 ? tok[0] : "";
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
