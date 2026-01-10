using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Swimm.API.Services.Helpers;

public static class HebrewTextHelper
{
    // Hebrew style names using Unicode escapes - original order
    public static readonly Dictionary<string, string> StyleMapHE = new()
    {
        ["\u05D7\u05D5\u05E4\u05E9\u05D9"] = "freestyle",           // ????? (freestyle)
        ["\u05E4\u05E8\u05E4\u05E8"] = "butterfly",                 // ???? (butterfly)
        ["\u05D2\u05D1"] = "backstroke",                            // ?? (backstroke)
        ["\u05D7\u05D6\u05D4"] = "breaststroke",                    // ??? (breaststroke)
        ["\u05DE\u05E2\u05D5\u05E8\u05D1 \u05D0\u05D9\u05E9\u05D9"] = "individual_medley",  // ????? ???? (individual medley)
        ["\u05DE\u05E2\u05D5\u05E8\u05D1"] = "medley",              // ????? (medley - for relays)
        // Reversed versions (after NormalizeHebrewLine)
        ["\u05D9\u05E9\u05E4\u05D5\u05D7"] = "freestyle",           // ????? reversed
        ["\u05E8\u05E4\u05E8\u05E4"] = "butterfly",                 // ???? reversed
        ["\u05D1\u05D2"] = "backstroke",                            // ?? reversed
        ["\u05D4\u05D6\u05D7"] = "breaststroke",                    // ??? reversed
        ["\u05D9\u05E9\u05D9\u05D0 \u05D1\u05E8\u05D5\u05E2\u05DE"] = "individual_medley",  // ????? ???? reversed
        ["\u05D1\u05E8\u05D5\u05E2\u05DE"] = "medley"               // ????? reversed
    };

    // Gender constants - original order
    private const string HebrewNun = "\u05E0";                      // ? (???. ??????)
    private const string HebrewZayin = "\u05D6";                    // ? (???. ??????)
    private const string HebrewGimel = "\u05D2";                    // ? (???. ??????, ???????????? ? masters)
    private const string HebrewBanot = "\u05D1\u05E0\u05D5\u05EA";  // ???????
    private const string HebrewBanim = "\u05D1\u05E0\u05D9\u05DD";  // ????????
    private const string HebrewNashim = "\u05E0\u05E9\u05D9\u05DD"; // ???????
    private const string HebrewGvarim = "\u05D2\u05D1\u05E8\u05D9\u05DD"; // ???????

    // Gender constants - reversed (after NormalizeHebrewLine)
    private const string HebrewBanotReversed = "\u05EA\u05D5\u05E0\u05D1";  // ??????? (reversed)
    private const string HebrewBanimReversed = "\u05DD\u05D9\u05E0\u05D1";  // ???????? (reversed)
    private const string HebrewNashimReversed = "\u05DD\u05D9\u05E9\u05E0"; // ??????? (reversed)
    private const string HebrewGvarimReversed = "\u05DD\u05D9\u05E8\u05D1\u05D2"; // ??????? (reversed)

    // ???? = "mix" = mixed gender (used in relays)
    // Original: \u05DE\u05D9\u05E7\u05E1
    // Reversed: \u05E1\u05E7\u05D9\u05DE
    private const string HebrewMix = "\u05DE\u05D9\u05E7\u05E1";
    private const string HebrewMixReversed = "\u05E1\u05E7\u05D9\u05DE";

    public static string NormalizeHebrewLine(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Array.Reverse(tokens);
        for (int i = 0; i < tokens.Length; i++)
        {
            if (Regex.IsMatch(tokens[i], @"[\u0590-\u05FF]"))
            {
                tokens[i] = new string(tokens[i].Reverse().ToArray());
            }
        }
        return string.Join(' ', tokens);
    }

    public static string NormalizeGenderHE(string gender)
    {
        if (string.IsNullOrWhiteSpace(gender)) return "none";

        gender = gender.Trim();

        // Female: ?, ???????, ??????? (original) + ???????, ??????? (reversed)
        if (gender == HebrewNun ||
            gender == HebrewBanot || gender == HebrewBanotReversed ||
            gender == HebrewNashim || gender == HebrewNashimReversed)
        {
            return "female";
        }

        // Male: ?, ?, ????????, ??????? (original) + ????????, ??????? (reversed)
        if (gender == HebrewZayin || gender == HebrewGimel ||
            gender == HebrewBanim || gender == HebrewBanimReversed ||
            gender == HebrewGvarim || gender == HebrewGvarimReversed)
        {
            return "male";
        }

        // Mix: ???? (mixed gender relay) - treated as "none" (no specific gender)
        if (gender == HebrewMix || gender == HebrewMixReversed)
        {
            return "none";
        }

        // If gender is not present/recognized in the PDF
        return "none";
    }

    public static string NormalizeGenderEN(string gender)
    {
        if (string.IsNullOrWhiteSpace(gender)) return "none";

        return gender.Trim().ToLowerInvariant() switch
        {
            "girls" or "female" or "women" => "female",
            "boys" or "male" or "men" => "male",
            "mix" or "mixed" => "none",
            _ => "none"
        };
    }

    public static string NormalizeStyleName(string? styleName)
    {
        if (string.IsNullOrWhiteSpace(styleName)) return string.Empty;

        return styleName.Trim().ToLowerInvariant() switch
        {
            "medley" => "individual_medley",
            _ => styleName.Trim()
        };
    }
}
