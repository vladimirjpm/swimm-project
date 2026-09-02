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
                var places = g.Where(r => r.Position is > 0 && HeatTypes.GivesOfficialPlace(r.HeatType))
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
                    IsMasters = r.IsMasters,
                };
            })
            .ToList();
    }

    /// <summary>
    /// Таб Records &amp; PB: личный рекорд за карьеру в каждой дисциплине выбранного бассейна
    /// плюс дельты до лучшего времени клуба и до рекорда страны.
    /// «Держит рекорд» определяется по ВРЕМЕНИ (не медленнее рекорда), а не по имени
    /// держателя: имена в справочнике строковые и у тёзок совпадают.
    ///
    /// Ступень справочника выбирается ПО ЗАПЛЫВУ (<see cref="RecordStepsOf"/>), а не по
    /// возрасту пловца: у мастерского старта эталон — полоса «45-49», у обычного взрослого
    /// — открытый рекорд страны, у ребёнка — его возрастная ступень. Пока спрашивалась одна
    /// ступень «age/{возраст}», у всех взрослых дельта была пустой (поймано 02.09.2026).
    ///
    /// Приоритет — возрастная ступень: она ближе пловцу, чем открытый рекорд страны, и
    /// именно её он может держать. Открытый берётся, когда возрастной ступени нет в
    /// справочнике (у взрослых её и не бывает).
    /// </summary>
    public static List<SwimmerPersonalBestDto> PersonalBests(
        IReadOnlyList<SeasonSwimRow> allRows,
        string? poolType,
        IReadOnlyDictionary<string, int> clubBestMs,
        IReadOnlyDictionary<RecordStep, IReadOnlyDictionary<string, NationalAgeRecordRow>> nationalRecords,
        int birthYear = 0,
        RecordAgeAxis axis = RecordAgeAxis.Calendar)
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

                // Возрастная ступень впереди открытой: «свой» рекорд ближе, чем рекорд
                // страны без возраста, и держать пловец может именно его.
                var steps = RecordStepsOf(r, birthYear, axis)
                    .OrderBy(step => step.Category == "open" ? 1 : 0)
                    .ToList();

                foreach (var step in steps)
                {
                    // Открытый рекорд страны — эталон ВЗРОСЛОГО. Девятилетней он не эталон,
                    // а шум: детская лестница в справочнике начинается с 10, и без этой
                    // отсечки в её личниках появлялась «Δ Israel +19.18» до взрослого
                    // рекорда. Держит она его или нет, показывает бейдж, а не дельта.
                    if (step.Category == "open" && !IsAdultSwim(r, birthYear, axis)) continue;

                    if (!nationalRecords.TryGetValue(step, out var slice)) continue;
                    if (!slice.TryGetValue(key, out var record) || record.TimeMs is not int recordMs) continue;

                    dto.HoldsNationalAgeRecord = ms <= recordMs;
                    dto.DeltaToNationalAgeRecordMs = ms - recordMs;
                    dto.NationalAgeRecordTime = record.Time;
                    dto.NationalAgeKey = record.AgeKey;
                    dto.NationalRecordScope = RecordScopeLabel(step);
                    dto.NationalAgeRecordQuality = record.IssueReason is null
                        ? null
                        : new SwimQualityDto { Kind = "record", Reason = record.IssueReason };
                    break;
                }

                return dto;
            })
            .ToList();
    }

    /// <summary>
    /// Фильтр «Season best»: место пловца среди СВЕРСТНИКОВ в каждой дисциплине, где он плавал
    /// в выбранном сезоне. Сверстники — пловцы того же года рождения; пол разделяет сам ключ
    /// дисциплины, поэтому отдельного фильтра по полу тут нет.
    ///
    /// Ранжир спортивный: равные времена делят место (двое по 41.23 — оба вторые, следующий
    /// четвёртый). Ровно поэтому место считается «сколько строго быстрее + 1», а не позицией
    /// в отсортированном списке — иначе один из двух одинаковых был бы объявлен быстрее.
    ///
    /// Своё лучшее берётся ТЕМ ЖЕ <see cref="BestPerDiscipline"/>, что и строки
    /// <c>/best-times</c>: иначе «первое место» и «лучшее время сезона» могли бы указывать
    /// на разные заплывы.
    /// </summary>
    public static SwimmerSeasonRankDto SeasonRanks(
        IReadOnlyList<SeasonSwimRow> allRows,
        int? season,
        int birthYear,
        string? gender,
        IReadOnlyList<PeerSeasonBest> cohort)
    {
        var age = season is int s ? SeasonMath.AgeInSeason(s, birthYear) : null;
        var sex = NormalizeGender(gender);
        var dto = new SwimmerSeasonRankDto
        {
            Season = season,
            Label = season is null ? "career" : SeasonMath.Label(season.Value),
            Age = age,
            Gender = sex,
            GroupLabel = PeerGroupLabel(age, sex),
        };

        // Карьера — не сезон: «где я среди сверстников» имеет смысл только внутри одного
        // сезона, потому что сравниваются лучшие времена ЭТОГО сезона.
        if (season is null || age is null) return dto;

        var byDiscipline = cohort
            .GroupBy(p => p.DisciplineKey)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (key, mine) in BestPerDiscipline(InSeason(allRows, season)))
        {
            var ms = mine.TimeMilliseconds!.Value;
            if (!byDiscipline.TryGetValue(key, out var peers))
            {
                // Своей же строки в когорте нет — значит когорту собрали по другому году
                // рождения (в справочнике он мог поменяться). Молча выдавать «первое место»
                // нельзя: это ровно тот случай, когда цифра выглядит достижением, не будучи им.
                continue;
            }

            dto.Rows.Add(new SwimmerDisciplineRankDto
            {
                DisciplineKey = key,
                Rank = peers.Count(p => p.TimeMs < ms) + 1,
                PeerCount = peers.Select(p => p.SwimmerId).Distinct().Count(),
                TimeMs = ms,
                LeaderTimeMs = peers.Min(p => p.TimeMs),
                GapToLeaderMs = ms - peers.Min(p => p.TimeMs),
            });
        }

        dto.Rows = dto.Rows.OrderBy(r => r.Rank).ThenByDescending(r => r.PeerCount).ToList();
        return dto;
    }

    /// <summary>
    /// Подпись группы сверстников для UI: «girls 9», у взрослых — «women 25» (иначе
    /// мастерс читался бы как «boys 45»). null — возраст или пол неизвестны.
    /// </summary>
    private static string? PeerGroupLabel(int? age, string? gender)
    {
        if (age is not int a || gender is null) return null;
        var noun = gender == "female"
            ? (a >= AdultAge ? "women" : "girls")
            : (a >= AdultAge ? "men" : "boys");
        return $"{noun} {a}";
    }

    /// <summary>С этого возраста группа называется «women/men», а не «girls/boys».</summary>
    private const int AdultAge = 18;

    /// <summary>Пол к виду ключа дисциплины. В базе он живёт и как «male», и как «M».</summary>
    private static string? NormalizeGender(string? gender) => gender?.Trim().ToLowerInvariant() switch
    {
        "male" or "m" => "male",
        "female" or "f" => "female",
        _ => null,
    };

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
                IsMasters = r.IsMasters,
                Competition = CompetitionRef(r),
                ResultId = r.ResultId,
            };
        }).ToList();

        return dto;
    }

    /// <summary>
    /// Таб H2H: лучшие времена двух пловцов бок о бок за один период (сезон карусели или
    /// карьера при <paramref name="season"/> = null).
    ///
    /// Ключ строки — БЕЗ пола, в отличие от <c>disciplineKey</c> остальных табов: пол там
    /// нужен, чтобы «50 вольным» мальчиков и девочек не сливались в справочниках, а здесь он
    /// ровно наоборот — разнополая пара не совпала бы ни одной строкой, и таблица вышла бы
    /// пустой при полных данных с обеих сторон.
    /// </summary>
    public static SwimmerCompareDto Compare(
        SwimmerCompareInput mineInput, SwimmerCompareInput rivalInput, int? season,
        int? seasonBestSeason = null, RecordAgeAxis axis = RecordAgeAxis.Calendar)
    {
        // Места среди сверстников живут ВНУТРИ сезона. В режиме ∞ показывать их всё равно
        // надо (иначе рекордсмен выглядит пустым), поэтому считаем их за витринный сезон —
        // ровно как фильтр «Season best» страницы пловца, который в ∞ делает то же самое.
        var sbSeason = season ?? seasonBestSeason;
        var mineSeason = InSeason(mineInput.Rows, season);
        var rivalSeason = InSeason(rivalInput.Rows, season);
        var mine = BestPerPairDiscipline(mineSeason);
        var rival = BestPerPairDiscipline(rivalSeason);

        // Бейджи считаются ОДИН раз на сторону и раздаются строкам по ключу дисциплины
        // (с полом — в справочниках и когортах он есть, в отличие от ключа пары).
        var mineFlags = Flags(mineInput, season, sbSeason, axis);
        var rivalFlags = Flags(rivalInput, season, sbSeason, axis);

        var dto = new SwimmerCompareDto
        {
            Season = season,
            Label = season is null ? "career" : SeasonMath.Label(season.Value),
            Mine = Side(mineInput.Profile, season, mineSeason, mineFlags),
            Rival = Side(rivalInput.Profile, season, rivalSeason, rivalFlags),
            SeasonBestSeason = sbSeason,
            SeasonBestLabel = sbSeason is int y ? SeasonMath.Label(y) : null,
        };

        // Пара времён на КАЖДЫЙ бассейн — это и есть единица сравнения. Строку из них
        // собираем ниже, но считать «кто быстрее» можно только здесь: 25м и 50м несравнимы.
        var pools = mine.Keys.Union(rival.Keys).Select(key =>
        {
            mine.TryGetValue(key, out var m);
            rival.TryGetValue(key, out var r);
            // Заголовок берём с той стороны, что есть: у общей пары стиль, дистанция и
            // бассейн одинаковы по построению ключа, а у односторонней выбора нет.
            var shape = m ?? r!;
            return new
            {
                shape.StyleId,
                Stroke = shape.StyleName,
                shape.Distance,
                Pool = new SwimmerComparePoolDto
                {
                    PoolType = shape.PoolType,
                    Mine = Swim(m, mineFlags),
                    Rival = Swim(r, rivalFlags),
                    DeltaMs = m is not null && r is not null
                        ? m.TimeMilliseconds!.Value - r.TimeMilliseconds!.Value
                        : null,
                },
            };
        }).ToList();

        // «50 брасс» — ОДНА дистанция: 25м и 50м складываются в одну строку своими парами
        // времён, а не в две строки, которые читались бы как две разные дистанции.
        var rows = pools
            .GroupBy(x => new { x.StyleId, x.Distance })
            .Select(g => new SwimmerCompareRowDto
            {
                Key = $"{g.Key.StyleId}|{g.Key.Distance}",
                StyleId = g.Key.StyleId,
                Stroke = g.First().Stroke,
                Distance = g.Key.Distance,
                // Короткая вода первой: в ней плавают чаще, и строка открывается тем
                // бассейном, где у обоих скорее всего есть время.
                Pools = g.Select(x => x.Pool)
                    .OrderBy(x => string.Equals(x.PoolType, "25m", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(x => x.PoolType)
                    .ToList(),
            })
            .ToList();

        // Строки, где есть что сравнивать, — сверху; внутри порядок тот же, что у остальных
        // табов страницы (очки убывают), чтобы они не «прыгали» при смене сезона.
        dto.Rows = rows
            .OrderByDescending(r => r.Pools.Any(p => p.DeltaMs is not null))
            .ThenByDescending(r => r.Pools.Max(p => Math.Max(p.Mine?.Points ?? 0, p.Rival?.Points ?? 0)))
            .ThenBy(r => r.Stroke)
            .ToList();

        // Счёт — ПО БАССЕЙНАМ: 50 брасс в 25м и в 50м это два разных сравнения, и складывать
        // их в одно значило бы потерять половину результата.
        var shared = pools.Select(x => x.Pool).Where(p => p.DeltaMs is not null).ToList();
        dto.SharedCount = shared.Count;
        dto.MineFaster = shared.Count(p => p.DeltaMs < 0);
        dto.RivalFaster = shared.Count(p => p.DeltaMs > 0);
        dto.Ties = shared.Count(p => p.DeltaMs == 0);
        return dto;
    }

    /// <summary>
    /// Шапка одной стороны сравнения: имя уже выбрано репозиторием (иврит → EN), статы —
    /// за тот же период, что и времена (медали только там, где их вручали).
    /// </summary>
    private static SwimmerCompareSideDto Side(
        SwimmerProfileDto? profile, int? season,
        IReadOnlyList<SeasonSwimRow> rows, CompareFlags flags) => new()
    {
        Id = profile?.Id ?? 0,
        Name = profile?.FullName ?? string.Empty,
        BirthYear = profile is { BirthYear: > 0 } ? profile.BirthYear : null,
        Gender = profile?.Gender,
        ClubName = profile?.ClubName,
        AgeInSeason = season is int s && profile is { BirthYear: > 0 }
            ? SeasonMath.AgeInSeason(s, profile.BirthYear)
            : null,
        SeasonBests = flags.SeasonBests,
        Medals = Medals(rows),
        BestPoints = rows.Where(SeasonAggregator.IsCountable)
            .Select(r => r.InternationalPoints)
            .DefaultIfEmpty(0)
            .Max(),
    };

    private static SwimmerCompareSwimDto? Swim(SeasonSwimRow? row, CompareFlags flags)
    {
        if (row is null) return null;

        var key = SeasonAggregator.DisciplineKey(row);
        return new SwimmerCompareSwimDto
        {
            Time = row.TimeOriginal,
            TimeMs = row.TimeMilliseconds,
            // Тем же хелпером, что и остальные табы: сегодня отбор помеченных сюда не пускает,
            // но признак обязан ехать из ОДНОГО места (инвариант И11).
            Quality = Quality(row),
            Points = row.InternationalPoints,
            Date = row.CompetitionDate.ToString("yyyy-MM-dd"),
            Competition = CompetitionRef(row),
            ResultId = row.ResultId,
            IsSeasonBest = flags.SeasonBestResultIds.Contains(row.ResultId),
            HoldsRecord = flags.RecordKeys.Contains(key),
        };
    }

    /// <summary>
    /// Вход одной стороны сравнения. Собран в запись, потому что сторон две и у каждой
    /// ЧЕТЫРЕ независимых источника: заплывы, профиль, своя когорта сверстников (год
    /// рождения у соперника другой) и свой срез справочника рекордов (своя возрастная
    /// ступень). Шесть позиционных аргументов на вызов путались бы местами.
    /// </summary>
    public sealed record SwimmerCompareInput(
        IReadOnlyList<SeasonSwimRow> Rows,
        SwimmerProfileDto? Profile,
        IReadOnlyList<PeerSeasonBest> Cohort,
        /// <summary>
        /// Срезы справочника ПО СТУПЕНЯМ: ключ — («age»,«12») / («masters»,«45-49») /
        /// («open»,«»), значение — рекорды этой ступени по ключу дисциплины. Ступеней
        /// несколько, потому что «свой рекорд» зависит от ЗАПЛЫВА: мастерский старт
        /// меряется полосой возраста, обычный — детской ступенью либо открытым рекордом.
        /// </summary>
        IReadOnlyDictionary<RecordStep, IReadOnlyDictionary<string, NationalAgeRecordRow>> Records);

    /// <summary>Ступень справочника рекордов: пара «категория × возрастной ключ».</summary>
    public readonly record struct RecordStep(string Category, string AgeKey);

    /// <summary>
    /// Взрослый ли это заплыв: мастерский старт либо возраст выше детской лестницы
    /// справочника (она идёт по 18 включительно). От этого зависит, годится ли открытый
    /// рекорд страны как эталон дельты.
    /// </summary>
    private static bool IsAdultSwim(SeasonSwimRow row, int birthYear, RecordAgeAxis axis)
    {
        if (row.IsMasters) return true;
        if (birthYear <= 0) return false;

        var age = axis == RecordAgeAxis.Season
            ? SeasonMath.AgeInSeason(SeasonAggregator.SeasonOf(row), birthYear)
            : row.CompetitionDate.Year - birthYear;
        return age is int years && years > TopChildStepAge;
    }

    /// <summary>Верх детской лестницы справочника («age/18»); дальше только open и masters.</summary>
    private const int TopChildStepAge = 18;

    /// <summary>Подпись ступени для UI: «age 14», «masters 45-49», «open».</summary>
    public static string RecordScopeLabel(RecordStep step) =>
        string.IsNullOrEmpty(step.AgeKey) ? step.Category : $"{step.Category} {step.AgeKey}";

    /// <summary>
    /// Ступени справочника, к которым относится ЗАПЛЫВ. Правила те же, что у детектора
    /// рекордов соревнования (<see cref="CompetitionRecordsDetector"/>): открытый рекорд
    /// страны плюс возрастная ступень — мастерская полоса для мастерского старта, иначе
    /// детская ступень по возрасту. Разъедутся правила — бейдж «REC» на странице начнёт
    /// спорить с карточкой «New records» соревнования.
    ///
    /// Возраст берётся по той же оси, что у детектора (<see cref="RecordAgeAxis"/>):
    /// справочник ведёт федерация, и по умолчанию мы сверяемся в её системе координат.
    /// </summary>
    public static IEnumerable<RecordStep> RecordStepsOf(
        SeasonSwimRow row, int birthYear, RecordAgeAxis axis)
    {
        yield return new RecordStep("open", string.Empty);

        if (birthYear <= 0) yield break;

        var age = axis == RecordAgeAxis.Season
            ? SeasonMath.AgeInSeason(SeasonAggregator.SeasonOf(row), birthYear)
            : row.CompetitionDate.Year - birthYear;
        if (age is not int years || years is < 5 or > 120) yield break;

        yield return row.IsMasters
            // Masters: диапазонный ключ «45-49» — нижняя граница кратна 5, ширина 5.
            ? new RecordStep("masters", $"{years / 5 * 5}-{years / 5 * 5 + 4}")
            : new RecordStep("age", years.ToString());
    }

    /// <summary>
    /// Бейджи стороны. SB держится по <c>ResultId</c>, а не по дисциплине: в режиме ∞ строка
    /// показывает КАРЬЕРНОЕ лучшее, а место среди сверстников посчитано за витринный сезон, и
    /// это разные заплывы. Бейдж ставим, только когда это ОДИН и тот же заплыв, — иначе он
    /// обещал бы первое место времени, которое в том сезоне не показывали.
    /// </summary>
    private sealed record CompareFlags(HashSet<long> SeasonBestResultIds, HashSet<string> RecordKeys, int SeasonBests);

    /// <summary>
    /// Бейджи стороны: где она быстрейшая среди сверстников (SB) и где держит официальный
    /// рекорд своей возрастной ступени (REC).
    ///
    /// SB считается ТОЙ ЖЕ арифметикой, что фильтр «Season best» (<see cref="SeasonRanks"/>):
    /// место — «сколько строго быстрее + 1», группа — свой год рождения и пол. За карьеру
    /// бейджа нет вовсе: сравнение живёт внутри одного сезона.
    /// </summary>
    private static CompareFlags Flags(
        SwimmerCompareInput input, int? season, int? sbSeason, RecordAgeAxis axis)
    {
        var seasonBestIds = new HashSet<long>();
        var records = new HashSet<string>();
        var shownBest = BestPerDiscipline(InSeason(input.Rows, season));
        var seasonBests = 0;

        // Места считаются по лучшему времени СЕЗОНА (за карьеру их не бывает), а бейдж
        // достаётся показанному заплыву только если это он и есть.
        if (sbSeason is int sb && input.Cohort.Count > 0)
        {
            var byDiscipline = input.Cohort.GroupBy(p => p.DisciplineKey)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var (key, row) in BestPerDiscipline(InSeason(input.Rows, sb)))
            {
                if (!byDiscipline.TryGetValue(key, out var peers)) continue;

                var ms = row.TimeMilliseconds!.Value;
                var rank = peers.Count(p => p.TimeMs < ms) + 1;
                var peerCount = peers.Select(p => p.SwimmerId).Distinct().Count();
                // «Первый среди одного» — не достижение: тот же порог, что у остальных
                // экранов (MIN_PEERS_FOR_RANK на клиенте).
                if (rank != 1 || peerCount < MinPeersForSeasonBest) continue;

                seasonBests++;
                seasonBestIds.Add(row.ResultId);
            }
        }

        var birthYear = input.Profile?.BirthYear ?? 0;
        foreach (var (key, row) in shownBest)
        {
            // Ступень зависит от ЗАПЛЫВА: мастерский старт меряется полосой возраста,
            // обычный — детской ступенью, и оба сверяются ещё с открытым рекордом страны.
            foreach (var step in RecordStepsOf(row, birthYear, axis))
            {
                if (!input.Records.TryGetValue(step, out var slice)) continue;
                if (!slice.TryGetValue(key, out var record) || record.TimeMs is not int recordMs) continue;
                if (row.TimeMilliseconds!.Value > recordMs) continue;

                records.Add(key);
                break;
            }
        }

        return new CompareFlags(seasonBestIds, records, seasonBests);
    }

    /// <summary>Меньше двух сверстников — места нет, бейдж SB не выдаётся.</summary>
    private const int MinPeersForSeasonBest = 2;

    /// <summary>
    /// Лучший зачётный заплыв в каждой дисциплине ПАРЫ — ключ без пола (см. Compare).
    /// Помеченные, DSQ и эстафеты сюда не попадают: их отсекает <c>IsCountable</c>.
    /// </summary>
    private static Dictionary<string, SeasonSwimRow> BestPerPairDiscipline(IEnumerable<SeasonSwimRow> rows)
    {
        var best = new Dictionary<string, SeasonSwimRow>();
        foreach (var row in rows)
        {
            if (!SeasonAggregator.IsCountable(row)) continue;
            var key = SeasonAggregator.DisciplineKey(row.StyleId, row.Distance, row.PoolType, gender: null);
            if (!best.TryGetValue(key, out var cur)
                || row.TimeMilliseconds!.Value < cur.TimeMilliseconds!.Value)
                best[key] = row;
        }
        return best;
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
        var awarded = rows.Where(r => r.IsAward && HeatTypes.GivesOfficialPlace(r.HeatType)).ToList();
        return new MedalCountsDto
        {
            Gold = awarded.Count(r => r.Position == 1),
            Silver = awarded.Count(r => r.Position == 2),
            Bronze = awarded.Count(r => r.Position == 3),
        };
    }
}
