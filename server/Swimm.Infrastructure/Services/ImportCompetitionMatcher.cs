using Swimm.Domain.Entities;

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

    /// <summary>
    /// Индекс дней compID ПО ДАТЕ для <see cref="MatchDay"/> (Д2) — с двумя защитами (Р118):
    /// <list type="bullet">
    /// <item>день со СВОИМ штампом <c>OrgCompId</c>, не равным текущему compID, не берётся: событие
    /// из окружных протоколов (на проде e1, e7, e10, e12) держит под одним <c>EventId</c> дни
    /// разных compID, и <see cref="CompetitionIdentity.ResolveDaysAsync"/> отдаёт их все;</item>
    /// <item>дата, на которую пришлось больше одного дня, неоднозначна — по compID не
    /// сопоставляется вовсе (порядок строк без OrderBy случаен, «первый попавшийся» опасен),
    /// дата уходит в <paramref name="ambiguousDates"/> для предупреждения;</item>
    /// <item>то же со стороны файла: если в файле на одну дату несколько «дней» (разные заголовки
    /// <paramref name="fileDays"/>, файл без события), по дате их не различить — иначе все части
    /// легли бы в один день.</item>
    /// </list>
    /// </summary>
    public static Dictionary<string, Competition> BuildLinkedDayIndex(
        IEnumerable<Competition> days, int orgCompId,
        IEnumerable<(string Name, string Date)> fileDays, out HashSet<string> ambiguousDates)
    {
        var fileAmbiguous = fileDays
            .GroupBy(d => d.Date)
            .Where(g => g.Select(d => d.Name).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();
        ambiguousDates = [];
        var byDate = new Dictionary<string, Competition>();
        foreach (var group in days
                     .Where(d => d.OrgCompId is null || d.OrgCompId == orgCompId)
                     .Where(d => !string.IsNullOrWhiteSpace(d.Date))
                     .GroupBy(d => d.Date))
        {
            var list = group.ToList();
            // Предупреждаем только о датах, где в базе ЕСТЬ дни этого compID: иначе сопоставлять и нечего.
            if (list.Count == 1 && !fileAmbiguous.Contains(group.Key)) byDate[group.Key] = list[0];
            else ambiguousDates.Add(group.Key);
        }
        return byDate;
    }

    /// <summary>
    /// ЕДИНСТВЕННОЕ правило «этот день файла — уже существующее соревнование» для превью
    /// (<c>FindExistingCompetitionsAsync</c>) и импорта (<c>ResolveCompetitionAsync</c>), Р118.
    /// Порядок — тот же, что был у импорта с Д2: сперва ключ (Name|SubName)|Date|PoolType,
    /// и только при промахе — день того же compID по дате (имя и бассейн не важны).
    /// <paramref name="nameKey"/> = null — ключ неполон (бассейн неизвестен), по имени не ищем.
    ///
    /// Ключ указал на X, а compID по дате — на другой Y: берётся X (имя надёжнее штампа, который
    /// мог встать ошибочно), Y отдаётся в <paramref name="divergentLinked"/> для предупреждения.
    ///
    /// До Р118 превью смотрело только ключ: у c1973 (compID 16926) бассейн в базе 50m, а парсер
    /// без указания в протоколе ставит 25m → превью «не нашло», «Перезатянуть» не взводило
    /// перезапись, а импорт находил день по compID и падал «Дублем».
    /// </summary>
    public static Competition? MatchDay(
        IReadOnlyDictionary<string, Competition> linkedDaysByDate,
        IReadOnlyDictionary<string, Competition> byKey,
        string? date,
        string? nameKey,
        out Competition? divergentLinked)
    {
        divergentLinked = null;
        linkedDaysByDate.TryGetValue(date ?? string.Empty, out var linked);
        if (nameKey != null && byKey.TryGetValue(nameKey, out var byName))
        {
            if (linked != null && linked.Id != byName.Id) divergentLinked = linked;
            return byName;
        }
        return linked;
    }

    /// <summary>Текст предупреждения: ключ и compID указали на разные соревнования.</summary>
    public static string DivergenceWarning(string? date, Competition byName, Competition linked, int? orgCompId) =>
        $"⚠ День {date}: по имени, дате и бассейну найдено соревнование #{byName.Id} «{byName.Name}», " +
        $"а по compID {orgCompId} — другое, #{linked.Id} «{linked.Name}». Взято #{byName.Id}; проверь штамп OrgCompId.";

    /// <summary>Текст предупреждения: на дату пришлось несколько дней compID.</summary>
    public static string AmbiguousDateWarning(string date, int orgCompId) =>
        $"⚠ compID {orgCompId}: на {date} несколько дней (в базе или в файле) — по compID этот день не сопоставлен, только по имени, дате и бассейну.";
}
