using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.Application.Validation;

/// <summary>
/// Сверка подозрительного заплыва с карточкой пловца на loglig — чистая функция, без сети
/// и БД (загрузку делает <c>IPreviewRecordCheckService</c>).
///
/// Держится на одном факте: протокол мы качаем С LOGLIG, значит его время обязано быть и в
/// карточке. В карточке время ХУЖЕ — почти наверняка разбор поехал (эталон: 00:32.59 на
/// 100 баттерфляем у Маккабиады).
///
/// ⚠ Обратное НЕ верно: совпадение с карточкой доказывает только, что МЫ разобрали протокол
/// верно. Ошибку самой федерации loglig повторяет — это тот же источник (И-40: 00:20.49 на
/// 50 вольным у 14-летней — быстрее мирового рекорда, а вердикт «на loglig то же время»
/// советовал не помечать). Поэтому до сверки с карточкой идут две проверки, от источника
/// не зависящие: мировой рекорд и собственный уровень пловца.
/// </summary>
public static class RecordCheckComparer
{
    /// <summary>
    /// Допуск сравнения. Федерация и loglig округляют по-разному (сотые против тысячных),
    /// поэтому «то же время» — не побайтовое равенство.
    /// </summary>
    public const int ToleranceMs = 30;

    /// <summary>
    /// Во сколько раз очки заплыва должны превышать лучший ДРУГОЙ заплыв пловца из карточки.
    /// Тот же порог, что у правила «выброс относительно себя» в <see cref="SuspectResultDetector"/>
    /// (калибровка 2026-08-03): за сезон так не прибавляют.
    /// </summary>
    public const double PersonalOutlierFactor = 2.0;

    /// <summary>Минимум других дисциплин в карточке, чтобы говорить об «уровне» пловца.</summary>
    public const int MinOtherEvents = 2;

    /// <summary>
    /// Проверка, которой карточка не нужна: время быстрее мирового рекорда — ошибка источника,
    /// с чем бы оно ни совпадало. null — рекорд не побит или его нет в справочнике.
    /// </summary>
    public static (RecordCheckVerdict Verdict, string Message)? CheckWorldBest(
        string styleName, string distance, string poolType, string? gender, string timeText,
        WorldBestReference? worldBests)
    {
        var fileMs = CompetitionRecordsDetector.ParseTimeToMs(timeText);
        if (fileMs is null || worldBests is null
            || !worldBests.TryGet(gender ?? "", styleName, distance, poolType, out var best, out _)
            || fileMs.Value >= best)
            return null;

        return (RecordCheckVerdict.Contradicts,
            $"{timeText} быстрее мирового рекорда ({Fmt(best)}) — ошибка в самом протоколе. "
            + "Совпадение с loglig тут ничего не доказывает: там тот же протокол. "
            + "Рекомендую пометить заплыв сомнительным.");
    }

    /// <param name="cardRows">Строки карточки loglig (личные рекорды за сезон).</param>
    /// <param name="styleName">Стиль заплыва из протокола (freestyle/backstroke/…).</param>
    /// <param name="distance">Дистанция из протокола («100»).</param>
    /// <param name="poolType">Бассейн из протокола («25m»/«50m»).</param>
    /// <param name="timeText">Время из протокола («00:25.62»).</param>
    /// <param name="gender">Пол заплыва (male/female) — ключ мирового рекорда.</param>
    /// <param name="worldBests">Справочник мировых рекордов; null — проверки уровня молчат.</param>
    public static (RecordCheckVerdict Verdict, string Message) Compare(
        IReadOnlyList<LogligResultRow> cardRows,
        string styleName,
        string distance,
        string poolType,
        string timeText,
        string? gender = null,
        WorldBestReference? worldBests = null)
    {
        var fileMs = CompetitionRecordsDetector.ParseTimeToMs(timeText);
        if (fileMs is null)
            return (RecordCheckVerdict.NotChecked, "Время из протокола не разобралось — только глазами.");

        if (CheckWorldBest(styleName, distance, poolType, gender, timeText, worldBests) is { } impossible)
            return impossible;

        if (CheckPersonalLevel(cardRows, styleName, distance, poolType, gender, fileMs.Value, worldBests)
            is { } outlier)
            return outlier;

        var poolLength = poolType.StartsWith("50", StringComparison.Ordinal) ? 50 : 25;

        var best = cardRows
            .Where(r => !r.IsRelay
                        && string.Equals(r.StyleName, styleName, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.Distance, distance, StringComparison.OrdinalIgnoreCase)
                        && r.PoolLength == poolLength
                        && r.TimeMillisecond != null)
            .OrderBy(r => r.TimeMillisecond)
            .FirstOrDefault();

        if (best is null)
            return (RecordCheckVerdict.NoData,
                $"На loglig нет {distance} {styleName} ({poolLength}м) за этот сезон — сверить не с чем.");

        var diff = best.TimeMillisecond!.Value - fileMs.Value;

        if (Math.Abs(diff) <= ToleranceMs)
            return (RecordCheckVerdict.Confirms,
                $"На loglig то же время ({best.TimeRaw}) — разобрано верно; мирового рекорда не бьёт, "
                + "из уровня пловца не выбивается. Помечать не нужно.");

        if (diff > 0)
            return (RecordCheckVerdict.Contradicts,
                $"На loglig лучшее время {best.TimeRaw}, в файле {timeText} — расходится на "
                + $"{diff / 1000.0:0.00} с. Похоже на ошибку разбора: рекомендую пометить заплыв сомнительным.");

        // В карточке время ЛУЧШЕ нашего: такой уровень пловцу по силам, разбор правдоподобен
        // (а «рекорд» справочника, вероятно, просто устарел).
        return (RecordCheckVerdict.Confirms,
            $"На loglig есть время быстрее ({best.TimeRaw}) — результат из файла правдоподобен.");
    }

    /// <summary>
    /// Заплыв против собственного уровня пловца — по ДРУГИМ дисциплинам карточки. Сравнимы они
    /// только в очках; очки считаем от мирового рекорда (1000·(WR/t)³). База не та, что у FINA,
    /// но для отношения двух заплывов одного пловца это неважно. Свою дисциплину не берём:
    /// в карточке лежит этот же заплыв, ошибочный вместе с протоколом.
    /// </summary>
    private static (RecordCheckVerdict, string)? CheckPersonalLevel(
        IReadOnlyList<LogligResultRow> cardRows, string styleName, string distance, string poolType,
        string? gender, int fileMs, WorldBestReference? worldBests)
    {
        if (worldBests is null
            || Points(worldBests, gender, styleName, distance, poolType, fileMs) is not double filePoints)
            return null;

        var others = cardRows
            .Where(r => !r.IsRelay && r.StyleName != null && r.Distance != null && r.TimeMillisecond is > 0
                        && !(string.Equals(r.StyleName, styleName, StringComparison.OrdinalIgnoreCase)
                             && string.Equals(r.Distance, distance, StringComparison.OrdinalIgnoreCase)))
            .Select(r => Points(worldBests, gender, r.StyleName!, r.Distance!,
                r.PoolLength == 50 ? WorldBestReference.LongCourse : WorldBestReference.ShortCourse,
                r.TimeMillisecond!.Value))
            .OfType<double>()
            .ToList();

        if (others.Count < MinOtherEvents) return null;

        var bestOther = others.Max();
        if (filePoints < bestOther * PersonalOutlierFactor) return null;

        return (RecordCheckVerdict.Contradicts,
            $"Уровень заплыва ≈{filePoints:0} очков, а лучший другой заплыв пловца на loglig за сезон — "
            + $"≈{bestOther:0}, в {filePoints / bestOther:0.0} раза ниже. Совпадение с loglig значит лишь, "
            + "что ошибка в самом протоколе. Рекомендую пометить заплыв сомнительным.");
    }

    /// <summary>Очки от мирового рекорда СВОЕГО бассейна; подмену бассейна не берём — исказит отношение.</summary>
    private static double? Points(WorldBestReference wr, string? gender, string style, string distance,
        string poolType, int ms)
        => wr.TryGet(gender ?? "", style, distance, poolType, out var best, out var fallback) && fallback is null
            ? 1000.0 * Math.Pow(best / (double)ms, 3)
            : null;

    private static string Fmt(int ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalMinutes >= 1
            ? $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}"
            : $"{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}";
    }
}
