namespace Swimm.Infrastructure.Services;

/// <summary>
/// Единственное место, где решается «эта строка результата/день файла принадлежит уже
/// существующему Competition»: по (Name ИЛИ SubName) + Date + PoolType. Дни, привязанные к
/// CompetitionEvent, хранятся под Name = имя события, а исходно распарсенный заголовок уходит
/// в SubName (JsonImportService, ветка targetEvent != null) — значит матчить нужно и по нему,
/// иначе повторный импорт такого дня не находит существующее соревнование.
/// </summary>
/// <remarks>
/// До фикса (инцидент 2026-07-20, Маккабиада) превью (<c>FindExistingCompetitionsAsync</c>) и сам
/// импорт (кэш соревнований в <c>ImportAsync</c>) использовали ДВА РАЗНЫХ правила: превью матчило
/// по Name-или-SubName, а импорт — строго по Name. Competition.Id 1483/1484/1485 (Name «Maccabiah
/// 2026», SubName «Maccabiah 2025 -», привязаны к CompetitionEvent 6) переимпортировались без
/// EventId (обычным overwrite, не через привязку к событию) — превью нашло совпадение по SubName и
/// показало «Обновить существующее», пользователь включил чекбокс, но импорт искал по Name строго и
/// промахнулся мимо кэша → создал НОВЫЕ соревнования 1488/1489/1490 вместо апдейта. Теперь оба места
/// строят индекс через один и тот же <see cref="BuildIndex{T}"/> — разойтись им больше негде.
/// </remarks>
internal static class ImportCompetitionMatcher
{
    /// <summary>Ключ словаря матчинга: имя (или альтернативное имя) + дата + тип бассейна.</summary>
    public static string Key(string name, string date, string poolType) => $"{name}|{date}|{poolType}";

    /// <summary>
    /// Строит индекс существующих соревнований для матчинга: каждое соревнование попадает в
    /// словарь дважды — под ключом по Name и (если задан) под ключом по SubName, с одним и тем же
    /// значением. Первое совпадение по каждому ключу выигрывает (TryAdd) — как и раньше, дублей
    /// по (Name|Date|PoolType) в БД быть не может (unique index), а по SubName коллизия
    /// теоретически возможна только между разными событиями с одинаковым заголовком дня.
    /// </summary>
    public static Dictionary<string, T> BuildIndex<T>(
        IEnumerable<T> existing,
        Func<T, string> name,
        Func<T, string?> subName,
        Func<T, string> date,
        Func<T, string> poolType)
    {
        var byKey = new Dictionary<string, T>();
        foreach (var c in existing)
        {
            byKey.TryAdd(Key(name(c), date(c), poolType(c)), c);
            var sub = subName(c);
            if (!string.IsNullOrWhiteSpace(sub))
                byKey.TryAdd(Key(sub!, date(c), poolType(c)), c);
        }
        return byKey;
    }

    /// <summary>
    /// Индекс дней ПО ДАТЕ внутри соревнования/события, привязанного к штампу сайта
    /// (<c>OrgCompId</c>) — идентичность фазы Д2 (docs/data-integrity.md).
    ///
    /// Зачем в обход имени: у одного и того же протокола название на сайте и в БД
    /// расходится («מוקדמות אליפות צעירים» против «…חלק ב'»), матчинг по имени промахивался
    /// и переимпорт создавал ПОЛНЫЙ дубликат события (инцидент И-3, 1837 строк). Внутри
    /// одного события даты дней уникальны, поэтому дата — надёжный ключ, а имя остаётся
    /// косметикой.
    ///
    /// Коллизия дат внутри события (теоретически — два дня с одной датой) разрешается
    /// первым: TryAdd, как и в основном индексе.
    /// </summary>
    public static Dictionary<string, T> BuildDateIndex<T>(IEnumerable<T> days, Func<T, string> date)
    {
        var byDate = new Dictionary<string, T>();
        foreach (var d in days)
        {
            var key = date(d);
            if (!string.IsNullOrWhiteSpace(key)) byDate.TryAdd(key, d);
        }
        return byDate;
    }
}
