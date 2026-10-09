using System;
using System.Text;

namespace Swimm.Parsing.Helpers;

/// <summary>
/// Грубое сравнение ивритского и латинского написания одного имени — по скелету согласных
/// («SHADMAN» ↔ «שדמן» → «SDMN»). Не транслитерация, а ответ на вопрос «это то же слово или
/// другое?»: гласные, «ה», «ו», «י», «א», «ע» и H выкидываются, звуки с разным написанием
/// сводятся к одной букве (PH/F → P, V/W → B, C/CH/KH/Q → K, SH → S, TZ/TS → Z, J → G).
///
/// Откалибровано на сверке 08.10.2026 (И-47): на 4642 пловцах, у которых EN-имя совпало с
/// карточкой loglig, выбор «какое латинское слово к какому ивритскому» по этому скелету не
/// ошибся ни разу, а в ~4,5% случаев честно не смог выбрать.
/// </summary>
public static class NameTransliteration
{
    /// <summary>Похожесть ивритского и латинского написания, 0…1. Пробелы и дефисы не значат.</summary>
    public static double Similarity(string? hebrew, string? latin)
    {
        var a = HebrewSkeleton(hebrew ?? "");
        var b = LatinSkeleton(latin ?? "");
        if (a.Length == 0 && b.Length == 0) return 1.0;
        if (a.Length == 0 || b.Length == 0) return 0.3;
        return 2.0 * Lcs(a, b) / (a.Length + b.Length);
    }

    internal static string HebrewSkeleton(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            var m = c switch
            {
                'ב' => 'B', 'ג' => 'G', 'ד' => 'D', 'ז' => 'Z', 'ט' => 'T', 'ח' => 'K',
                'כ' or 'ך' or 'ק' => 'K', 'ל' => 'L', 'מ' or 'ם' => 'M', 'נ' or 'ן' => 'N',
                'ס' or 'ש' => 'S', 'פ' or 'ף' => 'P', 'צ' or 'ץ' => 'Z', 'ר' => 'R', 'ת' => 'T',
                _ => '\0',
            };
            if (m != '\0') sb.Append(m);
        }
        return Squeeze(sb.ToString());
    }

    internal static string LatinSkeleton(string s)
    {
        var t = s.ToUpperInvariant()
            .Replace("PH", "P").Replace("TZ", "Z").Replace("TS", "Z").Replace("SH", "S")
            .Replace("CH", "K").Replace("KH", "K").Replace("ZH", "Z").Replace("DJ", "G")
            .Replace("X", "KS");
        var sb = new StringBuilder();
        foreach (var c in t)
        {
            var m = c switch
            {
                'F' => 'P', 'V' or 'W' => 'B', 'C' or 'Q' => 'K', 'J' => 'G',
                'A' or 'E' or 'I' or 'O' or 'U' or 'Y' or 'H' => '\0',
                >= 'A' and <= 'Z' => c,
                _ => '\0',
            };
            if (m != '\0') sb.Append(m);
        }
        return Squeeze(sb.ToString());
    }

    /// <summary>Двойные согласные — одной («SASSON» = «SASON»).</summary>
    private static string Squeeze(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (sb.Length == 0 || sb[^1] != c) sb.Append(c);
        return sb.ToString();
    }

    private static int Lcs(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
                cur[j] = a[i - 1] == b[j - 1] ? prev[j - 1] + 1 : Math.Max(prev[j], cur[j - 1]);
            (prev, cur) = (cur, prev);
            Array.Clear(cur);
        }
        return prev[b.Length];
    }
}
