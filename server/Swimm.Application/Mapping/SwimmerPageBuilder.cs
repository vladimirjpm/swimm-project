using Swimm.Application.Dtos;
using Swimm.Domain;

namespace Swimm.Application.Mapping;

/// <summary>
/// Сборка ответов страницы спортсмена из ОДНОГО набора заплывов (<see cref="SeasonSwimRow"/>).
/// Чистые функции: ни I/O, ни времени, ни кэша — всё это остаётся в репозитории и контроллере.
///
/// Зачем отдельным классом: сезонные KPI, лучшие времена, PB и прогресс обязаны считаться
/// из одного места и по одним правилам. Разложи их по репозиториям — и первое расхождение
/// цифр между табами будет неотлаживаемым (ровно это ловили на странице клуба).
/// </summary>
public static class SwimmerPageBuilder
{
    /// <summary>
    /// Площадка заплыва. Приедет из <c>Competition.WaterKind</c>, когда его заведут
    /// (docs/plans/open-water-course-plan.md, Ф0). Поле в контракте с самого начала, чтобы
    /// клиент не переучивался: открытая вода будет ОТДЕЛЬНЫМ полем, а не третьим PoolType.
    /// </summary>
    private const string PoolWaterKind = "pool";

    /// <summary>Заплывы выбранного сезона; <paramref name="season"/> = null — вся карьера.</summary>
    public static List<SeasonSwimRow> InSeason(IEnumerable<SeasonSwimRow> rows, int? season) =>
        rows.Where(r => season is null || SeasonAggregator.SeasonOf(r) == season).ToList();

    /// <summary>
    /// Сезоны, в которых есть заплывы, — от свежих к старым. Витринный сезон помечается
    /// <c>isDisplayDefault</c>; если заплывов в нём нет (пловец не стартовал), умолчание
    /// съезжает на ближайший сезон СТАРШЕ него, а не на самый свежий: иначе карусель
    /// открывалась бы на сезоне, который витрина ещё не считает состоявшимся.
    /// </summary>
    public static List<SwimmerSeasonOptionDto> Seasons(
        IReadOnlyList<SeasonSwimRow> rows, int showcaseSeason, int currentSeason)
    {
        var options = rows
            .GroupBy(SeasonAggregator.SeasonOf)
            .OrderByDescending(g => g.Key)
            .Select(g => new SwimmerSeasonOptionDto
            {
                Season = g.Key,
                Label = SeasonMath.Label(g.Key),
                IsCurrent = g.Key == currentSeason,
                Swims = g.Count(),
            })
            .ToList();

        var defaultSeason = options.FirstOrDefault(o => o.Season == showcaseSeason)
                            ?? options.FirstOrDefault(o => o.Season < showcaseSeason)
                            ?? options.FirstOrDefault();
        if (defaultSeason is not null) defaultSeason.IsDisplayDefault = true;

        return options;
    }

    /// <summary>
    /// KPI и список стартов. Медали считаются только там, где их вручали
    /// (<c>Competition.IsAward</c>): без наград место — это просто место в протоколе.
    /// Эстафетная медаль идёт в общий итог — командная награда так же личная, как своя.
    /// </summary>
    public static SwimmerSummaryDto Summary(
        IReadOnlyList<SeasonSwimRow> allRows, int? season, IReadOnlyDictionary<int, string?> standingKinds)
    {
        var rows = InSeason(allRows, season);

        var dto = new SwimmerSummaryDto
        {
            Season = season,
            Label = season is null ? "career" : SeasonMath.Label(season.Value),
            Points = rows.Sum(r => r.InternationalPoints),
            Swims = rows.Count,
            // Дисциплины считаются ТЕМ ЖЕ ключом, что строки таба Results (без категории
            // заплыва), иначе плитка «11 events» не сходилась бы с «11 best times».
            Events = rows
                .Where(SeasonAggregator.IsCountable)
                .Select(r => SeasonAggregator.DisciplineKey(r))
                .Distinct()
                .Count(),
            Medals = Medals(rows),
        };

        // Личные рекорды считаются по ВСЕЙ карьере (иначе первый заплыв каждого сезона
        // объявлялся бы личником), а показываются те, что поставлены в выбранном сезоне.
        var pbIds = SeasonAggregator.PersonalBests(allRows, includeEventCategory: true);
        dto.PersonalBests = rows.Count(r => pbIds.Contains(r.ResultId));

        dto.Competitions = Competitions(rows, standingKinds);
        dto.CompetitionCount = dto.Competitions.Count;
        return dto;
    }

    /// <summary>
    /// Старты сезона от новых к старым. Дни многодневки схлопываются по <c>EventId</c> —
    /// иначе трёхдневный чемпионат выглядел бы как три соревнования (та же ошибка, что
    /// удваивала KPI клуба на двухкатегорийных стартах).
    /// </summary>
    public static List<SwimmerCompetitionDto> Competitions(
        IReadOnlyList<SeasonSwimRow> rows, IReadOnlyDictionary<int, string?> standingKinds) =>
        rows
            .GroupBy(r => r.EventId is int e ? $"e{e}" : $"c{r.CompetitionId}")
            .Select(g =>
            {
                var first = g.OrderBy(r => r.CompetitionDate).ThenBy(r => r.ResultId).First();
                // Prelim-места — ранжир сессии, не результат старта (Р34).
                var places = g.Where(r => r.Position is > 0 && r.HeatType != "prelim")
                    .Select(r => r.Position!.Value).ToList();
                return new SwimmerCompetitionDto
                {
                    CompetitionId = first.CompetitionId,
                    EventId = first.EventId,
                    // У многодневки имя первого дня несёт номер дня — но подменять его
                    // именем события нечем: события в проекции нет. Берём как есть.
                    Name = first.CompetitionName ?? string.Empty,
                    Date = first.CompetitionDate.ToString("yyyy-MM-dd"),
                    IsChampionship = first.IsChampionship,
                    Kind = standingKinds.TryGetValue(first.CompetitionId, out var kind) ? kind : null,
                    PoolType = first.PoolType,
                    WaterKind = PoolWaterKind,
                    Swims = g.Count(),
                    Points = g.Sum(r => r.InternationalPoints),
                    Medals = Medals(g.ToList()),
                    BestPlace = places.Count > 0 ? places.Min() : null,
                };
            })
            .OrderByDescending(c => c.Date)
            .ToList();

    /// <summary>
    /// Таб Results: одна дистанция — одна строка, лучшее время за выбранный сезон.
    /// Категория заплыва в ключ строки НЕ входит (три зачёта Маккабиады на 50 вольным дают
    /// одну строку «лучшее на 50»), в отличие от детекции личных рекордов, где она нужна.
    /// </summary>
    public static List<SwimmerBestTimeDto> BestTimes(
        IReadOnlyList<SeasonSwimRow> allRows, int? season, int birthYear)
    {
        var careerBest = BestPerDiscipline(allRows);
        var seasonBest = BestPerDiscipline(InSeason(allRows, season));

        return seasonBest.Values
            .OrderByDescending(r => r.InternationalPoints)
            .ThenBy(r => r.StyleName)
            .Select(r =>
            {
                var key = SeasonAggregator.DisciplineKey(r);
                var quality = Quality(r);
                return new SwimmerBestTimeDto
                {
                    DisciplineKey = key,
                    StyleId = r.StyleId,
                    Stroke = r.StyleName,
                    Distance = r.Distance,
                    PoolType = r.PoolType,
                    WaterKind = PoolWaterKind,
                    Time = r.TimeOriginal,
                    TimeMs = r.TimeMilliseconds,
                    Quality = quality,
                    // Помеченное время в зачёт не идёт: очки прячем, чтобы дуга уровня и
                    // подпись «Points» не показывали достижение, которого не было.
                    Points = quality is null ? r.InternationalPoints : null,
                    Place = r.Position,
                    HeatType = r.HeatType,
                    // Возраст в сезоне ЗАПЛЫВА, а не на его дату: осенний и весенний старты
                    // одного сезона обязаны показывать один возраст.
                    AgeInSeason = SeasonMath.AgeInSeason(SeasonAggregator.SeasonOf(r), birthYear),
                    Splits = string.IsNullOrWhiteSpace(r.TimeSplit) ? null : r.TimeSplit,
                    Date = r.CompetitionDate.ToString("yyyy-MM-dd"),
                    Competition = CompetitionRef(r),
                    ResultId = r.ResultId,
                    IsCareerBest = careerBest.TryGetValue(key, out var best) && best.ResultId == r.ResultId,
                };
            })
            .ToList();
    }

    /// <summary>
    /// Таб Records &amp; PB: личный рекорд за карьеру в каждой дисциплине выбранного бассейна
    /// плюс дельты до лучшего времени клуба и до рекорда страны своего возраста.
    /// «Держит рекорд» определяется по ВРЕМЕНИ (не медленнее рекорда), а не по имени
    /// держателя: имена в справочнике строковые и у тёзок совпадают.
    /// </summary>
    public static List<SwimmerPersonalBestDto> PersonalBests(
        IReadOnlyList<SeasonSwimRow> allRows,
        string? poolType,
        IReadOnlyDictionary<string, int> clubBestMs,
        IReadOnlyDictionary<string, NationalAgeRecordRow> nationalRecords)
    {
        var scoped = poolType is null
            ? allRows
            : allRows.Where(r => string.Equals(r.PoolType, poolType, StringComparison.OrdinalIgnoreCase)).ToList();

        return BestPerDiscipline(scoped).Values
            .OrderByDescending(r => r.InternationalPoints)
            .ThenBy(r => r.StyleName)
            .Select(r =>
            {
                var key = SeasonAggregator.DisciplineKey(r);
                var ms = r.TimeMilliseconds!.Value;
                var dto = new SwimmerPersonalBestDto
                {
                    DisciplineKey = key,
                    StyleId = r.StyleId,
                    Stroke = r.StyleName,
                    Distance = r.Distance,
                    PoolType = r.PoolType,
                    Time = r.TimeOriginal,
                    TimeMs = ms,
                    Quality = Quality(r),
                    Points = r.InternationalPoints,
                    Date = r.CompetitionDate.ToString("yyyy-MM-dd"),
                    Competition = CompetitionRef(r),
                    ResultId = r.ResultId,
                };

                if (clubBestMs.TryGetValue(key, out var club))
                {
                    dto.HoldsClubBest = ms <= club;
                    dto.DeltaToClubBestMs = ms - club;
                }

                if (nationalRecords.TryGetValue(key, out var record) && record.TimeMs is int recordMs)
                {
                    dto.HoldsNationalAgeRecord = ms <= recordMs;
                    dto.DeltaToNationalAgeRecordMs = ms - recordMs;
                    dto.NationalAgeRecordTime = record.Time;
                    dto.NationalAgeKey = record.AgeKey;
                    dto.NationalAgeRecordQuality = record.IssueReason is null
                        ? null
                        : new SwimQualityDto { Kind = "record", Reason = record.IssueReason };
                }

                return dto;
            })
            .ToList();
    }

    /// <summary>
    /// Таб Progress: все заплывы одной дисциплины по возрастанию даты.
    /// Личник отмечается по РАЗВЁРТКЕ линии (running best внутри этой же дисциплины), а не
    /// по карьерной детекции с категорией: на графике «личник» значит «в этот день я поплыл
    /// быстрее, чем когда-либо на этой дистанции».
    /// Незачётные строки (DSQ, помеченные, эстафеты) остаются в списке попыток, но личником
    /// не становятся — их исключает <see cref="SeasonAggregator.IsCountable"/>.
    /// </summary>
    public static SwimmerProgressDto Progress(
        IReadOnlyList<SeasonSwimRow> allRows, string disciplineKey, int birthYear)
    {
        var rows = allRows
            .Where(r => SeasonAggregator.DisciplineKey(r) == disciplineKey)
            .OrderBy(r => r.CompetitionDate)
            .ThenBy(r => r.ResultId)
            .ToList();

        var dto = new SwimmerProgressDto { DisciplineKey = disciplineKey };
        if (rows.Count == 0) return dto;

        var first = rows[0];
        dto.StyleId = first.StyleId;
        dto.Stroke = first.StyleName;
        dto.Distance = first.Distance;
        dto.PoolType = first.PoolType;

        var pbIds = SeasonAggregator.PersonalBests(rows);

        dto.Points = rows.Select(r =>
        {
            var quality = Quality(r);
            return new SwimmerProgressPointDto
            {
                Date = r.CompetitionDate.ToString("yyyy-MM-dd"),
                Time = r.TimeOriginal,
                TimeMs = r.TimeMilliseconds,
                IsPb = pbIds.Contains(r.ResultId),
                Quality = quality,
                Points = quality is null ? r.InternationalPoints : null,
                Place = r.Position,
                HeatType = r.HeatType,
                Season = SeasonAggregator.SeasonOf(r),
                AgeInSeason = SeasonMath.AgeInSeason(SeasonAggregator.SeasonOf(r), birthYear),
                Competition = CompetitionRef(r),
                ResultId = r.ResultId,
            };
        }).ToList();

        return dto;
    }

    /// <summary>Лучший зачётный заплыв в каждой дисциплине (без категории заплыва).</summary>
    private static Dictionary<string, SeasonSwimRow> BestPerDiscipline(IEnumerable<SeasonSwimRow> rows)
    {
        var best = new Dictionary<string, SeasonSwimRow>();
        foreach (var row in rows)
        {
            if (!SeasonAggregator.IsCountable(row)) continue;
            var key = SeasonAggregator.DisciplineKey(row);
            if (!best.TryGetValue(key, out var cur)
                || row.TimeMilliseconds!.Value < cur.TimeMilliseconds!.Value)
                best[key] = row;
        }
        return best;
    }

    private static SwimQualityDto? Quality(SeasonSwimRow row) =>
        row.SuspectReason is null ? null : new SwimQualityDto { Kind = "protocol", Reason = row.SuspectReason };

    private static SwimmerCompetitionRefDto CompetitionRef(SeasonSwimRow row) => new()
    {
        Id = row.CompetitionId,
        EventId = row.EventId,
        Name = row.CompetitionName ?? string.Empty,
        IsChampionship = row.IsChampionship,
    };

    private static MedalCountsDto Medals(IReadOnlyList<SeasonSwimRow> rows)
    {
        // Prelim-заплывы в медали не идут: их место — ранжир сессии, медаль дают за финал.
        var awarded = rows.Where(r => r.IsAward && r.HeatType != "prelim").ToList();
        return new MedalCountsDto
        {
            Gold = awarded.Count(r => r.Position == 1),
            Silver = awarded.Count(r => r.Position == 2),
            Bronze = awarded.Count(r => r.Position == 3),
        };
    }
}
