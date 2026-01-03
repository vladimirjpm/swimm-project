using System;
using System.Text.RegularExpressions;
using Swimm.API.Services.Models;

namespace Swimm.API.Services.Parsers;

public static class ResultLineParser
{
    public static PDF_Result ParseResultLine(string line)
    {
        var tok = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        object? position = tok[0] == "-" ? null : int.Parse(tok[0]);
        int heat = int.Parse(tok[1]);
        int lane = int.Parse(tok[2]);
        int idxYear = Array.FindIndex(tok, 3, t => Regex.IsMatch(t, @"^\d{4}$"));
        int birth = int.Parse(tok[idxYear]);
        string timeTok = tok[^2];

        // ????????? ?????? ??????? ??? ??????????? ???????? (DQ, NS, 00:00.00)
        string? time = null;
        if (Regex.IsMatch(timeTok, @"^\d{2}:\d{2}\.\d{2}$"))
        {
            // ???? ????? 00:00.00 - ??????? ??? ?????????? ???????
            if (timeTok != "00:00.00")
            {
                time = timeTok;
            }
        }

        int points = int.TryParse(tok[^1], out var p) ? p : 0;

        string firstEn = string.Empty;
        string lastEn = string.Empty;
        if (idxYear > 3)
        {
            firstEn = tok[idxYear - 1];
            lastEn = string.Join(' ', tok[3..(idxYear - 1)]);
        }

        string club = tok.Length > idxYear + 1
            ? string.Join(' ', tok[(idxYear + 1)..^2])
            : string.Empty;

        return new PDF_Result(
            Country: tok[0],
            Position: position,
            Heat: heat,
            Lane: lane,
            LastName: lastEn,
            FirstName: firstEn,
            BirthYear: birth,
            Club: club,
            Time: time,
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

        // ??????: "??????? ??? 2014"
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
