using System;
using System.Linq;
using System.Text.RegularExpressions;
using Swimm.Parsing.Models;

namespace Swimm.Parsing.Parsers.IsrOrg;

public static class IsrOrgResultLineParser
{
    private static readonly Regex TimeRx = new(@"^\d{2}:\d{2}\.\d{1,2}$", RegexOptions.Compiled);

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
            int idxDQ = Array.FindIndex(tok, idxYear + 1, t => t == "DQ" || t == "NS");
            if (idxDQ >= 0 && idxDQ < nextResultStart)
            {
                timeFailNote = tok[idxDQ];
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
            int idxDQ = Array.FindIndex(tok, idxYear + 1, t => t == "DQ" || t == "NS");
            if (idxDQ > idxYear + 1 && idxDQ < nextResultStart)
            {
                club = string.Join(' ', tok[(idxYear + 1)..idxDQ]);
            }
            else if (idxDQ < 0)
            {
                club = string.Join(' ', tok[(idxYear + 1)..idxPoints]);
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
