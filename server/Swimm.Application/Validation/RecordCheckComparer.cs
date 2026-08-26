using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.Application.Validation;

/// <summary>
/// Сверка подозрительного заплыва с карточкой пловца на loglig — чистая функция, без сети
/// и БД (загрузку делает <c>IPreviewRecordCheckService</c>).
///
/// Держится на одном факте: протокол мы качаем С LOGLIG, значит его время обязано быть и в
/// карточке. Совпало — рекорд настоящий; в карточке время ХУЖЕ — почти наверняка разбор
/// поехал (эталон: 00:32.59 на 100 баттерфляем у Маккабиады).
/// </summary>
public static class RecordCheckComparer
{
    /// <summary>
    /// Допуск сравнения. Федерация и loglig округляют по-разному (сотые против тысячных),
    /// поэтому «то же время» — не побайтовое равенство.
    /// </summary>
    public const int ToleranceMs = 30;

    /// <param name="cardRows">Строки карточки loglig (личные рекорды за сезон).</param>
    /// <param name="styleName">Стиль заплыва из протокола (freestyle/backstroke/…).</param>
    /// <param name="distance">Дистанция из протокола («100»).</param>
    /// <param name="poolType">Бассейн из протокола («25m»/«50m»).</param>
    /// <param name="timeText">Время из протокола («00:25.62»).</param>
    public static (RecordCheckVerdict Verdict, string Message) Compare(
        IReadOnlyList<LogligResultRow> cardRows,
        string styleName,
        string distance,
        string poolType,
        string timeText)
    {
        var fileMs = CompetitionRecordsDetector.ParseTimeToMs(timeText);
        if (fileMs is null)
            return (RecordCheckVerdict.NotChecked, "Время из протокола не разобралось — только глазами.");

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
                $"На loglig то же время ({best.TimeRaw}) — рекорд похож на настоящий, помечать не нужно.");

        if (diff > 0)
            return (RecordCheckVerdict.Contradicts,
                $"На loglig лучшее время {best.TimeRaw}, в файле {timeText} — расходится на "
                + $"{diff / 1000.0:0.00} с. Похоже на ошибку разбора: рекомендую пометить заплыв сомнительным.");

        // В карточке время ЛУЧШЕ нашего: такой уровень пловцу по силам, разбор правдоподобен
        // (а «рекорд» справочника, вероятно, просто устарел).
        return (RecordCheckVerdict.Confirms,
            $"На loglig есть время быстрее ({best.TimeRaw}) — результат из файла правдоподобен.");
    }
}
