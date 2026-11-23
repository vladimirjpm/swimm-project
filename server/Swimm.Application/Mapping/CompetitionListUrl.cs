using Swimm.Application.Dtos;
using Swimm.Domain;

namespace Swimm.Application.Mapping;

/// <summary>
/// Ключ строки /Admin/Competitions: <c>e{EventId}</c> — многодневное событие, <c>c{Id}</c> —
/// одиночное соревнование базы, <c>s{DiscoveredId}</c> — строка «Входящих», которой в базе нет.
/// Тот же ключ, что был у раскрытия строк в sessionStorage; теперь он же — <c>?sel=</c>.
/// </summary>
public static class CompetitionRowKey
{
    public static string Of(UnifiedCompetitionRowDto u) =>
        u.Db is { IsEvent: true } ev ? $"e{ev.EventId}"
        : u.Db?.Single is { } c ? $"c{c.Id}"
        : $"s{u.Site?.DiscoveredId ?? 0}";

    /// <summary>
    /// Подходит ли строка под ключ. <c>s{N}</c> находит и уже затянутую строку с той же
    /// записью «Входящих»: после «Затянуть» ключ строки меняется на <c>c…</c>/<c>e…</c>, а
    /// адрес с <c>sel=s…</c> должен открывать то же соревнование.
    /// </summary>
    public static bool Matches(UnifiedCompetitionRowDto u, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        key = key.Trim();
        if (string.Equals(Of(u), key, StringComparison.OrdinalIgnoreCase)) return true;
        return key.Length > 1 && (key[0] is 's' or 'S')
            && int.TryParse(key.AsSpan(1), out var discId)
            && u.Site?.DiscoveredId == discId;
    }

    /// <summary>Похоже ли значение на ключ (буква e/c/s + положительное число) — мусор в адресе отбрасывается.</summary>
    public static bool IsValid(string? key) =>
        !string.IsNullOrWhiteSpace(key) && key.Trim() is { Length: > 1 } k
        && (k[0] is 'e' or 'E' or 'c' or 'C' or 's' or 'S')
        && int.TryParse(k.AsSpan(1), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0;
}

/// <summary>Правила адреса /Admin/Competitions, которые не зависят от базы.</summary>
public static class CompetitionListUrl
{
    /// <summary>Значение <c>?season=</c> «все сезоны»: явный выбор чипа «Все» — без него голый
    /// адрес открыл бы текущий месяц, и к списку всех сезонов было бы не вернуться.</summary>
    public const string AllSeasons = "all";

    /// <summary>
    /// Весь список (все сезоны, «p» по 20) — для ссылок с других экранов, которым нужен не текущий
    /// месяц, а всё: дашборд «Без правила очков», Health «дубль даты у события», Jobs «Входящие»
    /// (решение Влада 23.11.2026). Сайдбар, admin-home и Import остаются голыми — текущий месяц.
    /// </summary>
    public const string AllSeasonsHref = "/Admin/Competitions?season=" + AllSeasons;

    /// <summary>Размер страницы, когда месяц не выбран (поиск, <c>filter=</c>, весь сезон).</summary>
    public const int PageSize = 20;

    /// <summary>
    /// Открывать ли текущий сезон и месяц по умолчанию: только когда в адресе нет НИ ОДНОГО
    /// непустого параметра (хэндофф, раздел 6.1). Любой параметр — <c>q</c>, <c>filter</c>,
    /// <c>stage</c>, <c>cat</c>, <c>kind</c>, <c>season</c> без <c>month</c>, <c>p</c>… — значит,
    /// что выборку задали явно: ссылки с Jobs, Health и дашборда ищут по всему сезону или по
    /// всем сезонам, подставленный месяц спрятал бы искомую строку.
    /// </summary>
    public static bool ShouldOpenOnCurrentMonth(IEnumerable<KeyValuePair<string, string?>> query) =>
        !query.Any(kv => !string.IsNullOrWhiteSpace(kv.Value));

    /// <summary>Имя, под которым ФОРМА фильтров шлёт сезон (не <c>season</c>): отличает форму от готовой ссылки.</summary>
    public const string FormSeasonKey = "fseason";
    /// <summary>Имя, под которым форма фильтров шлёт месяц (не <c>month</c>).</summary>
    public const string FormMonthKey = "fmonth";

    /// <summary>
    /// Адрес после отправки формы фильтров (решение Влада 23.11.2026): «поиск — всегда по всем
    /// сезонам, остальные фильтры держат месяц». Форма шлёт сезон и месяц как <see cref="FormSeasonKey"/> /
    /// <see cref="FormMonthKey"/>; здесь они становятся <c>season</c>/<c>month</c> — или выбрасываются,
    /// если в <c>q</c> что-то есть. Готовые ссылки с <c>q</c> и <c>season</c> (Jobs «Что изменилось»)
    /// этих ключей не несут и не трогаются: возвращается null. Пустые значения и <c>p</c> (новая
    /// выборка — первая страница) в адрес не идут; остальное — как пришло.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>>? FormRedirect(IEnumerable<KeyValuePair<string, string?>> query)
    {
        var all = query.ToList();
        if (!all.Any(kv => kv.Key.Equals(FormSeasonKey, StringComparison.OrdinalIgnoreCase)
                        || kv.Key.Equals(FormMonthKey, StringComparison.OrdinalIgnoreCase)))
            return null;

        string? Get(string key) => all.FirstOrDefault(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
        var searching = !string.IsNullOrWhiteSpace(Get("q"));
        var result = new List<KeyValuePair<string, string>>();
        foreach (var (key, value) in all)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (key.Equals("p", StringComparison.OrdinalIgnoreCase)) continue;
            var isSeason = key.Equals(FormSeasonKey, StringComparison.OrdinalIgnoreCase) || key.Equals("season", StringComparison.OrdinalIgnoreCase);
            var isMonth = key.Equals(FormMonthKey, StringComparison.OrdinalIgnoreCase) || key.Equals("month", StringComparison.OrdinalIgnoreCase);
            if ((isSeason || isMonth) && searching) continue;
            var name = isSeason ? "season" : isMonth ? "month" : key;
            if (result.Any(kv => kv.Key == name)) continue;
            result.Add(new(name, value!.Trim()));
        }
        return result;
    }

    /// <summary>Текущий сезон (год окончания, нумерация федерации) и месяц по местной дате.</summary>
    public static (int Season, int Month) CurrentSeasonMonth(DateOnly today)
    {
        var d = today.ToDateTime(TimeOnly.MinValue);
        return (SeasonMath.FederationYearOf(d), today.Month);
    }

    /// <summary><c>?season=</c> → год сезона; <c>all</c>, пусто и мусор — null (все сезоны).</summary>
    public static int? ParseSeason(string? value) =>
        int.TryParse(value?.Trim(), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var y) && y is > 1900 and < 3000 ? y : null;
}
