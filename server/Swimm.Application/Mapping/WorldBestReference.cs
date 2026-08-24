using System;
using System.Collections.Generic;

namespace Swimm.Application.Mapping;

/// <summary>
/// Мировые рекорды как порог «физически невозможно» для <see cref="SuspectResultDetector"/>.
///
/// ⚠ Ось бассейна ОБЯЗАТЕЛЬНА. В 25-метровом бассейне вдвое больше поворотов, отталкивание
/// быстрее плавания — времена короткой воды на 1.5–4% лучше, и мировых рекордов на каждую
/// дистанцию два разных набора. До 2026-08-24 порог был захардкоженным списком без бассейна
/// (в основном длинная вода), и зимний чемпионат в 25 м получил две ложные пометки
/// «быстрее мирового рекорда»: 23.46 на 50 на спине против рекорда ДЛИННОЙ воды 23.55, при
/// том что рекорд короткой — 22.11. См. docs/data-integrity.md, И-13.
///
/// Источник — тот же справочник <c>Records</c>, что показывает попап Normative Info
/// (<c>RegionType='world'</c>). Копии в коде быть не должно: она разъезжается со справочником
/// молча — прежний список из 36 строк содержал 20 рекордов длинной воды, 3 короткой и
/// 13 протухших.
/// </summary>
public sealed class WorldBestReference
{
    public const string ShortCourse = "25m";
    public const string LongCourse = "50m";

    private readonly Dictionary<string, int> _byKey;

    private WorldBestReference(Dictionary<string, int> byKey) => _byKey = byKey;

    /// <summary>Пустой справочник: правила, опирающиеся на мировой рекорд, молчат.</summary>
    public static WorldBestReference Empty { get; } = new([]);

    /// <summary>
    /// Собирает справочник из строк <c>Records</c>. Время разбирается тем же
    /// <see cref="CompetitionRecordsDetector.ParseTimeToMs"/>, что и везде; строки с пустым
    /// или неразобранным временем пропускаются — в справочнике такие есть (у женских 200 и
    /// 400 к/п длинной воды <c>Time</c> пуст).
    /// </summary>
    public static WorldBestReference Build(
        IEnumerable<(string Gender, string Style, string Distance, string PoolType, string? Time)> rows)
    {
        var byKey = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (gender, style, distance, pool, time) in rows)
        {
            var ms = CompetitionRecordsDetector.ParseTimeToMs(time);
            if (ms is not > 0) continue;

            var key = Key(gender, style, distance, NormalizePool(pool));
            if (key is null) continue;

            // Дубли по ключу (в справочнике одна и та же дисциплина лежит и как
            // individual_medley, и как medley): берём БЫСТРЕЙШУЮ — порог «невозможно»
            // должен быть самым мягким из известных, иначе правило начнёт обвинять зря.
            if (!byKey.TryGetValue(key, out var known) || ms.Value < known) byKey[key] = ms.Value;
        }
        return new WorldBestReference(byKey);
    }

    /// <summary>
    /// Порог для заплыва. Сначала рекорд СВОЕГО бассейна; если его в справочнике нет —
    /// рекорд короткой воды, и тогда <paramref name="poolFallbackNote"/> объясняет подмену
    /// (решение Влада 2026-08-24: мерить по 25 м, но говорить об этом вслух).
    ///
    /// Обратной подмены НЕТ: рекорд длинной воды медленнее, для заплыва в 25 м он был бы
    /// порогом СТРОЖЕ настоящего — ровно та ошибка, из-за которой правило и переписано.
    /// В нынешнем справочнике этот случай и не встречается: дисциплин с рекордом 50 м без
    /// рекорда 25 м нет ни одной (100 к/п, наоборот, бывает только в короткой воде).
    /// </summary>
    public bool TryGet(string gender, string style, string distance, string? poolType,
        out int best, out string? poolFallbackNote)
    {
        poolFallbackNote = null;
        best = 0;

        var pool = NormalizePool(poolType);
        if (pool is not null && Key(gender, style, distance, pool) is string exact
            && _byKey.TryGetValue(exact, out best))
            return true;

        if (Key(gender, style, distance, ShortCourse) is not string shortKey
            || !_byKey.TryGetValue(shortKey, out best))
            return false;

        poolFallbackNote = pool is null
            ? "бассейн соревнования неизвестен, сверено по рекорду 25 м"
            : $"рекорда для {PoolLabel(pool)} в справочнике нет, сверено по рекорду 25 м";
        return true;
    }

    /// <summary>Человеку: «25 м» / «50 м».</summary>
    public static string PoolLabel(string? pool) => NormalizePool(pool) switch
    {
        ShortCourse => "25 м",
        LongCourse => "50 м",
        _ => "?"
    };

    /// <summary>
    /// Пол смешанных и неизвестных заплывов считаем мужским: мужской рекорд быстрее, то есть
    /// порог мягче. Лучше пропустить, чем пометить корректную строку с неопределённым полом.
    /// </summary>
    private static string? Key(string? gender, string? style, string? distance, string? pool)
    {
        if (string.IsNullOrWhiteSpace(style) || string.IsNullOrWhiteSpace(distance) || pool is null)
            return null;

        var g = gender is "male" or "female" ? gender : "male";
        var d = distance.EndsWith('m') ? distance[..^1] : distance;
        return $"{g}|{style}|{d}|{pool}";
    }

    /// <summary>«25m», «25 m», «SCM», «short» → 25m; «50m», «LCM», «long» → 50m; иначе null.</summary>
    private static string? NormalizePool(string? pool)
    {
        if (string.IsNullOrWhiteSpace(pool)) return null;
        var v = pool.Trim().ToLowerInvariant();
        if (v.Contains("25") || v.Contains("scm") || v.Contains("short")) return ShortCourse;
        if (v.Contains("50") || v.Contains("lcm") || v.Contains("long")) return LongCourse;
        return null;
    }
}
