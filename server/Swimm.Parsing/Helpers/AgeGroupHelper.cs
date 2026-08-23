using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Swimm.Domain;

namespace Swimm.Parsing.Helpers;

public static class AgeGroupHelper
{
    public static string GetAgeGroup(int age)
    {
        if (age <= 0) return "0";
        if (age <= 8) return "0-8";
        if (age <= 10) return "9-10";
        if (age <= 12) return "11-12";
        if (age <= 14) return "13-14";
        if (age <= 16) return "15-16";
        if (age <= 18) return "17-18";
        if (age <= 24) return "19-24";
        if (age <= 29) return "25-29";
        if (age <= 34) return "30-34";
        if (age <= 39) return "35-39";
        if (age <= 44) return "40-44";
        if (age <= 49) return "45-49";
        if (age <= 54) return "50-54";
        if (age <= 59) return "55-59";
        if (age <= 64) return "60-64";
        if (age <= 69) return "65-69";
        if (age <= 74) return "70-74";
        if (age <= 79) return "75-79";
        if (age <= 84) return "80-84";
        if (age <= 89) return "85-89";
        return "90+";
    }

    /// <summary>
    /// Год, по которому считается возраст пловца, — год ОКОНЧАНИЯ СЕЗОНА, а не календарный
    /// год заплыва (правило Влада, docs/season-boundary-rule.md).
    ///
    /// ⚠ Ради чего: сезон федерации идёт октябрь–август, и полосы протокола нарезаны по нему.
    /// На אליפות הרצליה (31/10/2025) заплыв назывался «בנות 11-12» и в нём плыли 2014-2015 г.р.
    /// Календарный счёт давал 2015 → 10 лет → полоса «9-10», то есть один и тот же заплыв
    /// разъезжался у нас по двум группам, а места в группе считались не по протоколу.
    ///
    /// Если дату разобрать не удалось — откат на календарный год: лучше прежнее поведение,
    /// чем ноль.
    /// </summary>
    public static int SeasonEndYearFromDateString(string dateStr)
    {
        var d = ParseDate(dateStr);
        return d is DateTime dt
            ? SeasonMath.FederationYearOf(dt)     // = StartYearOf + 1, год окончания сезона
            : ExtractYearFromDateString(dateStr);
    }

    /// <summary>Дата протокола: «31/10/2025», «2025-10-31». null — формат не распознан.</summary>
    private static DateTime? ParseDate(string dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return null;

        string[] formats = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy"];
        return DateTime.TryParseExact(dateStr.Trim(), formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var dt)
            ? dt
            : null;
    }

    public static int ExtractYearFromDateString(string dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return DateTime.Now.Year;
        if (DateTime.TryParse(dateStr, out var dt)) return dt.Year;
        
        var m = Regex.Match(dateStr, @"\d{4}");
        if (m.Success && int.TryParse(m.Value, out var y)) return y;
        
        var toks = dateStr.Split([' ', '-', '/', '.'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = toks.Length - 1; i >= 0; i--)
        {
            if (Regex.IsMatch(toks[i], @"^\d{4}$") && int.TryParse(toks[i], out var yy)) return yy;
        }
        return DateTime.Now.Year;
    }
}
