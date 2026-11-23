using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Что уйдёт в пачку под одной кнопкой: id строк по порядку (не больше потолка) и сколько всего
/// строк подходит под правило.
/// </summary>
/// <param name="Ids">Строки, которые уйдут в пачку (DiscoveredId), от ранней даты к поздней.</param>
/// <param name="Eligible">Сколько строк подходит под правило без потолка.</param>
public sealed record BulkPullPick(IReadOnlyList<int> Ids, int Eligible)
{
    public static readonly BulkPullPick Empty = new([], 0);

    /// <summary>Число на кнопке = сколько строк реально уйдёт.</summary>
    public int Count => Ids.Count;

    /// <summary>Сколько осталось за потолком <see cref="BulkPullLimits.MaxBatchSize"/> — «следующим заходом».</summary>
    public int Left => Eligible - Ids.Count;
}

/// <summary>
/// Кнопка пачки («за день» / «за месяц») в обоих положениях чекбокса «включая чемпионаты».
/// Страница рисует оба набора, чекбокс только выбирает один из них — отбора в JS нет.
/// </summary>
public sealed record BulkPullOffer(BulkPullPick WithoutChampionships, BulkPullPick WithChampionships)
{
    public static readonly BulkPullOffer None = new(BulkPullPick.Empty, BulkPullPick.Empty);

    public BulkPullPick For(bool includeChampionships) =>
        includeChampionships ? WithChampionships : WithoutChampionships;

    /// <summary>Без чемпионатов тянуть нечего, а с ними есть — «чемпионат — отдельно».</summary>
    public bool OnlyChampionships => WithoutChampionships.Count == 0 && WithChampionships.Count > 0;

    /// <summary>Кнопки нет ни в одном положении чекбокса.</summary>
    public bool IsEmpty => WithChampionships.Count == 0;
}

/// <summary>
/// ЕДИНСТВЕННОЕ правило «кто попадает в пачку» /Admin/Competitions (вариант E, хэндофф 4).
/// По нему страница считает N на кнопках «Затянуть за день (N)» / «за месяц (N)» и кладёт в
/// кнопку те же id, которые уйдут в <see cref="IBulkPullService.StartAsync"/>: второго отбора
/// (в JS по видимым строкам или в сервисе по своим правилам) нет. Сервис по этим id только
/// отбрасывает строки, которые успели перестать быть долгом между показом и кликом
/// (затянуты, скрыты), — и это видно в панели пачки как «ушло меньше, чем на кнопке».
///
/// Правило: строки уже отфильтрованы списком (сезон, месяц, дисциплина, поиск и прочие
/// фильтры страницы); из них берутся состояния «ждёт затягивания», которых нет в базе
/// (недотянутая многодневка Р115 «ждёт затягивания», но её дни в базе — не берётся);
/// «ждём протокол», «идёт», «впереди» и закрытые не берутся никогда; чемпионаты Израиля —
/// только при «включая чемпионаты». Порядок — по дате начала от ранней, не больше потолка.
/// </summary>
public static class BulkPullSelection
{
    /// <summary>Строка — кандидат пачки (без учёта чемпионатов и потолка).</summary>
    public static bool IsCandidate(UnifiedCompetitionRowDto u) =>
        u.Db == null && u.Site != null && u.Status?.State == CompetitionRowState.ReadyToPull;

    /// <summary>Что уйдёт в пачку из этих строк.</summary>
    public static BulkPullPick Pick(IEnumerable<UnifiedCompetitionRowDto> rows, bool includeChampionships,
        int max = BulkPullLimits.MaxBatchSize)
    {
        var eligible = rows
            .Where(IsCandidate)
            // Чемпионат у строки вне базы — по названию (CompetitionNameRules.IsChampionship), тем же
            // правилом сервис отбрасывает чемпионаты по своим DTO: расхождения нет.
            .Where(u => includeChampionships || !u.IsChampionship)
            .OrderBy(u => u.Site!.DateStart)
            .ThenBy(u => u.Site!.DiscoveredId)
            .Select(u => u.Site!.DiscoveredId)
            .Distinct()
            .ToList();
        return new BulkPullPick(eligible.Take(max).ToList(), eligible.Count);
    }

    /// <summary>Кнопка в обоих положениях чекбокса.</summary>
    public static BulkPullOffer Offer(IEnumerable<UnifiedCompetitionRowDto> rows, int max = BulkPullLimits.MaxBatchSize)
    {
        var list = rows as IReadOnlyCollection<UnifiedCompetitionRowDto> ?? rows.ToList();
        return new BulkPullOffer(Pick(list, false, max), Pick(list, true, max));
    }
}
