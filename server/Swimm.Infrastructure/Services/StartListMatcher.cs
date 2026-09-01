namespace Swimm.Infrastructure.Services;

/// <summary>
/// Ключ физической идентичности заявки: заплыв × дорожка × пловец.
///
/// <c>SwimmerId</c> в ключе — в отличие от <see cref="ResultMatchKey"/>, где он сознательно
/// исключён: четыре ноги эстафеты делят один <c>Heat</c> и одну <c>Lane</c>, и без пловца
/// они неразличимы.
/// </summary>
public readonly record struct StartListKey(int OrgDisciplineId, int Heat, int Lane, int SwimmerId);

/// <summary>Итог сопоставления заявок предыдущего забора с только что прочитанными.</summary>
public sealed class StartListMatch<TOld, TNew>
{
    /// <summary>Ключ совпал: пловец там же, где был.</summary>
    public List<(TOld Old, TNew New)> Matched { get; } = [];

    /// <summary>Тот же пловец в том же заплыве, но сменились заплыв/дорожка — ПЕРЕСЕВ.</summary>
    public List<(TOld Old, TNew New)> Moved { get; } = [];

    /// <summary>Новые строки: записались после прошлого забора.</summary>
    public List<TNew> Added { get; } = [];

    /// <summary>Исчезли из источника: снялись до старта.</summary>
    public List<TOld> Removed { get; } = [];
}

/// <summary>
/// Сопоставление заявок при ПЕРЕЗАБОРЕ стартового протокола. Чистая функция без БД —
/// как <see cref="ResultMatcher"/> для переимпорта результатов, и по тем же причинам.
///
/// Почему двумя проходами, а не одним по ключу. Стартовый протокол МЕНЯЕТСЯ до последнего
/// дня: снятия сдвигают дорожки, заплывы объединяют. <c>Heat</c> и <c>Lane</c> входят в ключ
/// (иначе не различить ноги эстафеты), поэтому пересев выглядит как «строку удалили и завели
/// другую» — заявка теряла бы свой <c>Id</c>, а вместе с ним историю и связь с результатом.
/// Это ровно та ловушка, из-за которой в <see cref="ResultMatcher"/> появился дискриминатор:
/// изменчивое поле в ключе превращает правку в пару «удаление + вставка».
///
/// Поэтому: сперва точный ключ, затем — среди оставшихся — «тот же пловец в той же
/// дисциплине». Второй проход срабатывает, только когда кандидат С ОБЕИХ сторон РОВНО ОДИН:
/// у одного пловца в одной эстафетной дисциплине бывает две команды (инцидент comp #1513),
/// и угадывать, какая из них куда переехала, нельзя — такие строки честно уходят
/// в <see cref="StartListMatch{TOld,TNew}.Removed"/> и <c>Added</c>.
/// </summary>
public static class StartListMatcher
{
    public static StartListMatch<TOld, TNew> Match<TOld, TNew>(
        IEnumerable<TOld> old,
        IEnumerable<TNew> fresh,
        Func<TOld, StartListKey> keyOfOld,
        Func<TNew, StartListKey> keyOfNew)
    {
        var result = new StartListMatch<TOld, TNew>();

        var oldByKey = new Dictionary<StartListKey, List<TOld>>();
        foreach (var o in old)
        {
            var key = keyOfOld(o);
            if (!oldByKey.TryGetValue(key, out var list)) oldByKey[key] = list = [];
            list.Add(o);
        }

        // Проход 1 — точный ключ.
        var leftoverNew = new List<TNew>();
        foreach (var n in fresh)
        {
            var key = keyOfNew(n);
            if (oldByKey.TryGetValue(key, out var candidates) && candidates.Count > 0)
            {
                result.Matched.Add((candidates[0], n));
                candidates.RemoveAt(0);
                continue;
            }

            leftoverNew.Add(n);
        }

        var leftoverOld = oldByKey.SelectMany(kv => kv.Value).ToList();

        // Проход 2 — пересев: тот же пловец в той же дисциплине, но на другом месте.
        var oldBySwimmer = leftoverOld
            .GroupBy(o => (keyOfOld(o).OrgDisciplineId, keyOfOld(o).SwimmerId))
            .ToDictionary(g => g.Key, g => g.ToList());
        var newBySwimmer = leftoverNew
            .GroupBy(n => (keyOfNew(n).OrgDisciplineId, keyOfNew(n).SwimmerId))
            .ToDictionary(g => g.Key, g => g.ToList());

        var movedOld = new HashSet<TOld>();
        var movedNew = new HashSet<TNew>();
        foreach (var (pair, olds) in oldBySwimmer)
        {
            if (olds.Count != 1) continue;
            if (!newBySwimmer.TryGetValue(pair, out var news) || news.Count != 1) continue;

            result.Moved.Add((olds[0], news[0]));
            movedOld.Add(olds[0]);
            movedNew.Add(news[0]);
        }

        result.Added.AddRange(leftoverNew.Where(n => !movedNew.Contains(n)));
        result.Removed.AddRange(leftoverOld.Where(o => !movedOld.Contains(o)));
        return result;
    }
}
