using Swimm.Domain.Entities;

namespace Swimm.Application.Mapping;

/// <summary>
/// Ключ сравнения имён: trim/lower, финальные ивритские буквы, гереш ׳/гершаим ״ → ASCII,
/// схлопнуть пробелы. Одно правило с поиском дублей пловцов и клубов
/// (<c>SwimmerDedupService.Normalize</c> зовёт сюда) и с dedup-report.sql.
/// </summary>
public static class NameKey
{
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var chars = name.Trim().ToLowerInvariant().Select(c => c switch
        {
            'ך' => 'כ', 'ם' => 'מ', 'ן' => 'נ', 'ף' => 'פ', 'ץ' => 'צ',
            '׳' => '\'', '״' => '"', '’' => '\'',
            _ => c
        });
        return string.Join(' ',
            new string(chars.ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>
/// Название клуба с чужого сайта (loglig) → наши живые клубы. Узнаёт название, если после
/// <see cref="NameKey.Normalize"/> оно совпало с
/// <list type="bullet">
/// <item>текущим <c>Name</c> / <c>NameEn</c> живого клуба;</item>
/// <item>прежним названием (<c>ClubNames</c>, переименование или вариант написания);</item>
/// <item>названием клуба-надгробия (<c>Club.MergedIntoId</c>) — тогда это его итоговый канон.</item>
/// </list>
/// Так `TLV SWIM` на loglig совпадает с нашим `SWIM TLV`, если дубли склеены. Перестановку слов
/// сам НЕ ищет: несклеенные дубли — дело Ф0 (поиск дублей клубов), не сверки.
/// Псевдоклубы (сборная/страна) не клуб пловца — их нет в индексе.
/// </summary>
public sealed class ClubNameMatcher
{
    private readonly Dictionary<string, HashSet<int>> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<int, int?> _mergedInto;
    private readonly HashSet<int> _pseudo;

    public ClubNameMatcher(IEnumerable<ClubAliasClub> clubs, IEnumerable<ClubAliasName> names)
    {
        var clubList = clubs.ToList();
        _mergedInto = clubList.ToDictionary(c => c.Id, c => c.MergedIntoId);
        _pseudo = clubList.Where(c => c.IsPseudo).Select(c => c.Id).ToHashSet();

        foreach (var c in clubList)
            if (Live(c.Id) is int club) { Add(c.Name, club); Add(c.NameEn, club); }
        foreach (var n in names)
            if (Live(n.ClubId) is int club) { Add(n.Name, club); Add(n.NameEn, club); }
    }

    /// <summary>Итоговый живой клуб (по цепочке склеек); null — псевдоклуб, петля или клуба нет.</summary>
    public int? Live(int clubId) =>
        ClubMergeChains.FinalOf(clubId, _mergedInto) is int final && !_pseudo.Contains(final) ? final : null;

    /// <summary>Живые клубы с таким названием; пусто — название нам незнакомо.</summary>
    public IReadOnlySet<int> Find(string? name) =>
        _byKey.TryGetValue(NameKey.Normalize(name), out var ids) ? ids : EmptySet;

    private static readonly HashSet<int> EmptySet = [];

    private void Add(string? name, int clubId)
    {
        var key = NameKey.Normalize(name);
        if (key.Length == 0) return;
        if (!_byKey.TryGetValue(key, out var set)) _byKey[key] = set = [];
        set.Add(clubId);
    }
}

/// <summary>Что вернула карточка loglig для сверки: открылась ли и какой клуб на ней.</summary>
/// <param name="Fetched">Карточка открылась и распозналась.</param>
/// <param name="ClubName">Чип «אגודה» как напечатан; null/пусто — клуба на карточке нет.</param>
public sealed record LogligClubAnswer(bool Fetched, string? ClubName);

/// <summary>
/// Сверка кандидата на смену клуба с loglig.com (Р98, §6.2 docs/plans/swimmer-club-change-plan.md).
/// Решение Влада 18.11.2026: «если клуб поменялся — сверяйся с логлиг. если там не поменялся —
/// пока не меняй». Чистая функция: сеть, кэш и запись — у вызывающего.
///
/// | loglig                         | вердикт        | что делаем                         |
/// |--------------------------------|----------------|------------------------------------|
/// | новый клуб кандидата           | confirmed      | меняем карточку, журнал «loglig»   |
/// | клуб из карточки               | old            | ждём, перепроверка позже           |
/// | «ארכיון איגוד»                 | archive        | ждём, список «loglig: archive»     |
/// | третий клуб / незнакомое имя   | other          | ждём, список «loglig: другой клуб» |
/// | нет loglig-id                  | no-loglig-id   | ждём, список «нет ID»              |
/// | карточка не открылась / пусто  | unavailable    | ждём, повтор при следующем прогоне |
///
/// Если название loglig подходит и новому, и старому клубу (два наших клуба с одним именем) —
/// это не подтверждение: вердикт <c>old</c>, менять не на чем основываться.
/// Карточка на склеенном клубе, чей канон и есть новый клуб, — не смена клуба: <c>confirmed</c>
/// без loglig (склейка уже решила).
/// </summary>
public static class SwimmerClubLogligRule
{
    /// <summary>Что loglig пишет вместо клуба у пловца, который больше не зарегистрирован.</summary>
    public const string ArchiveName = "ארכיון איגוד";

    private static readonly string ArchiveKey = NameKey.Normalize("ארכיון");

    /// <param name="logligId">Подтверждённый loglig-id пловца; null — нет.</param>
    /// <param name="answer">Ответ карточки; null — в loglig не ходили или запрос упал.</param>
    /// <param name="fromClubId">Клуб в карточке пловца (может быть надгробием — сведём к канону).</param>
    /// <param name="toClubId">Клуб, который дало правило К2.</param>
    public static string Decide(
        int? logligId, LogligClubAnswer? answer, int? fromClubId, int toClubId, ClubNameMatcher clubs)
    {
        var to = clubs.Live(toClubId) ?? toClubId;
        var from = fromClubId is int f ? clubs.Live(f) ?? f : (int?)null;
        // Карточка указывает на склеенный клуб, а его канон и есть «новый» клуб: это не смена клуба,
        // а хвост склейки — подтверждать у loglig нечего.
        if (from == to) return SwimmerClubLogligVerdicts.Confirmed;

        if (logligId is null) return SwimmerClubLogligVerdicts.NoLogligId;
        if (answer is not { Fetched: true } || string.IsNullOrWhiteSpace(answer.ClubName))
            return SwimmerClubLogligVerdicts.Unavailable;

        if (NameKey.Normalize(answer.ClubName).Contains(ArchiveKey, StringComparison.Ordinal))
            return SwimmerClubLogligVerdicts.Archive;

        var matches = clubs.Find(answer.ClubName);
        if (from is int old && matches.Contains(old)) return SwimmerClubLogligVerdicts.StillOld;
        if (matches.Contains(to)) return SwimmerClubLogligVerdicts.Confirmed;
        return SwimmerClubLogligVerdicts.OtherClub;
    }

    /// <summary>Только этот вердикт меняет карточку.</summary>
    public static bool Applies(string? verdict) => verdict == SwimmerClubLogligVerdicts.Confirmed;

    /// <summary>
    /// Можно ли не ходить в loglig и взять прошлый ответ: открывали меньше суток назад, тем же
    /// loglig-id и карточка тогда открылась. Неудачу (unavailable) не кэшируем — повтор сразу.
    /// </summary>
    public static bool CacheValid(
        DateTime now, DateTime? checkedAt, int? cachedLogligId, int? logligId, string? cachedClubName) =>
        checkedAt is DateTime at && now - at < CacheTtl
        && cachedLogligId is not null && cachedLogligId == logligId
        && !string.IsNullOrWhiteSpace(cachedClubName);

    /// <summary>Сколько живёт ответ loglig (решение §6.2: кэш на сутки).</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromDays(1);
}
