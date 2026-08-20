using System;
using System.Collections.Generic;
using System.Linq;

namespace Swimm.Application.Mapping;

/// <summary>Эстафетная строка, как она уже лежит у нас (из PDF-импорта).</summary>
/// <param name="TimeMs">null — команда без времени (DQ/NS/DNF).</param>
public sealed record RelayRowInDb(
    long ResultId,
    string StyleName,
    string Distance,
    string Club,
    int? TimeMs,
    int? Position,
    string Gender,
    string EventStyleAge,
    string AgeGroup,
    int? OfficialClubPoints);

/// <summary>
/// Эстафетная строка пособытийного источника. Пол и полоса взяты из ШАПКИ события
/// («4X50 מעורב שליחים - בנות 14-15»), а не из подзаголовка секции: секция у всех четырёх
/// эстафетных событий 1581 подписана «גמר ישיר - נשים 19-99», хотя плывут дети 14-15.
/// </summary>
public sealed record RelayRowFromSource(
    string StyleName,
    string Distance,
    string Club,
    int? TimeMs,
    int? Position,
    string Gender,
    string Band,
    int OfficialClubPoints);

/// <summary>Что меняется в одной строке. Поля «до» нужны отчёту dry-run.</summary>
public sealed record RelayBandChange(
    long ResultId,
    string Club,
    string StyleName,
    string Distance,
    string GenderBefore, string GenderAfter,
    string BandBefore, string BandAfter,
    int? PositionBefore, int? PositionAfter,
    int? OfficialBefore, int OfficialAfter)
{
    /// <summary>Строка, у которой хоть одно поле отличается, — только такие и пишутся в БД.</summary>
    public bool HasChanges =>
        GenderBefore != GenderAfter
        || BandBefore != BandAfter
        || PositionBefore != PositionAfter
        || OfficialBefore != OfficialAfter;
}

/// <summary>План ремонта. Применять можно только план без <see cref="Problems"/>.</summary>
public sealed record RelayBandPlan(
    IReadOnlyList<RelayBandChange> Changes,
    IReadOnlyList<string> Problems)
{
    public bool CanApply => Problems.Count == 0;
}

/// <summary>
/// Сопоставление эстафетных строк пособытийного источника loglig с нашими строками того же
/// соревнования (docs/data-integrity.md §10, ремонт полос 1581).
///
/// Зачем. PDF-экспорт печатает эстафеты одной сквозной дисциплиной: пол «none», места
/// 1…48 через все возрастные полосы. Организатор же считает зачёт по полосам — у 1581 это
/// четыре события «4X50 מעורב/חופשי שליחים - בנות 14-15 / בנים 15-16», и очки достаются
/// топ-20 КАЖДОЙ полосы. Из-за сквозных мест мы платили 884 очка вместо 1766.
///
/// Почему не переимпорт эстафет с сайта, а точечная правка полей. Состав команды у нас уже
/// есть (<c>RelayMembers</c> из PDF, привязка ног к пловцам); импорт заново пересоздал бы
/// строки и ноги, а вместе с ними — ссылки, медиа и ключи upsert (рецидив И-4). Полосу же
/// источник печатает явно, и это единственное, чего протоколу не хватает.
///
/// Ключ сопоставления — <b>стиль + дистанция + клуб + время</b>. Проверено на 1581: среди
/// 95 строк такие пары уникальны с обеих сторон, включая четыре команды без времени
/// (DQ/NS у них в разных клубах). Уникальность проверяется КАЖДЫЙ раз: неоднозначность
/// делает план неприменимым целиком, а не «сматчим что получится» — частично разложенные
/// полосы дали бы кривые места, как у <c>RelayBandReconstructor</c> (всё или ничего).
///
/// Чистая функция: на входе две плоские выборки, на выходе план. Ни сети, ни БД.
/// </summary>
public static class LogligRelayBandMatcher
{
    public static RelayBandPlan Build(
        IReadOnlyList<RelayRowFromSource> source,
        IReadOnlyList<RelayRowInDb> db)
    {
        var problems = new List<string>();

        var sourceByKey = Index(source, r => Key(r.StyleName, r.Distance, r.Club, r.TimeMs),
            key => problems.Add($"источник: две строки с одним ключом {key}"));
        var dbByKey = Index(db, r => Key(r.StyleName, r.Distance, r.Club, r.TimeMs),
            key => problems.Add($"база: две строки с одним ключом {key}"));

        foreach (var key in sourceByKey.Keys.Where(k => !dbByKey.ContainsKey(k)))
            problems.Add($"строка источника без пары в базе: {key}");
        foreach (var key in dbByKey.Keys.Where(k => !sourceByKey.ContainsKey(k)))
            problems.Add($"строка базы без пары у источника: {key}");

        var changes = new List<RelayBandChange>();
        foreach (var (key, src) in sourceByKey)
        {
            if (!dbByKey.TryGetValue(key, out var ours)) continue;

            // Команда без времени места не занимает — как у индивидуальных дисквалификаций
            // и как в RelayBandReconstructor. Источник ставит DQ-строкам номер по порядку
            // (у 1581 это 20, 20, 22, 26), но очков за него не платит, и наш движок правил
            // не должен видеть там места вовсе: TimeFail — не единственная защита.
            var position = src.TimeMs is null ? null : src.Position;

            changes.Add(new RelayBandChange(
                ours.ResultId, ours.Club, ours.StyleName, ours.Distance,
                ours.Gender, src.Gender,
                ours.EventStyleAge, src.Band,
                ours.Position, position,
                ours.OfficialClubPoints, src.OfficialClubPoints));
        }

        return new RelayBandPlan(
            changes.OrderBy(c => c.StyleName, StringComparer.Ordinal)
                .ThenBy(c => c.BandAfter, StringComparer.Ordinal)
                .ThenBy(c => c.PositionAfter ?? int.MaxValue)
                .ToList(),
            problems);
    }

    private static Dictionary<string, T> Index<T>(
        IReadOnlyList<T> rows, Func<T, string> keyOf, Action<string> onDuplicate)
    {
        var index = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = keyOf(row);
            if (!index.TryAdd(key, row)) onDuplicate(key);
        }
        return index;
    }

    /// <summary>
    /// Ключ строки. Время — в миллисекундах, а не текстом: источник печатает «01:57.00»,
    /// PDF — «01:57.0», и посимвольное сравнение теряло бы такие пары.
    /// </summary>
    private static string Key(string style, string distance, string club, int? timeMs) =>
        $"{style.ToLowerInvariant()}|{distance.ToLowerInvariant()}|{NormalizeClub(club)}|{timeMs?.ToString() ?? "no-time"}";

    /// <summary>
    /// Имя клуба у источника и в базе пишется одинаково, но апострофы бывают разными
    /// символами («מוניר אבו ח'ליס») — приводим к ивритскому гершу, как это уже делает
    /// сопоставление имён пловцов. Пробелы схлопываются: в HTML их печатает вёрстка.
    /// </summary>
    private static string NormalizeClub(string club) =>
        string.Join(' ', club
            .Replace('\'', '׳').Replace('’', '׳').Replace('‘', '׳').Replace('`', '׳')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
