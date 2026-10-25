using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Services;

namespace Swimm.Infrastructure.Repositories;

// Обзор соревнования (/api/competition-overview): по методу на карточку Overview. Что считает
// каждая карточка и почему именно так — docs/competition-overview-cards.md.
// Порядок запросов в LoadCompetitionOverviewAsync — прежний (один контекст, запросы по очереди).
public partial class ResultRepository
{
    public Task<CompetitionOverviewDto> GetCompetitionOverviewAsync(ResultFilter filter) =>
        _cache.GetOrCreateAsync($"competition-overview:{FilterCacheKey(filter)}",
            () => LoadCompetitionOverviewAsync(filter), ResultsTtl);

    private async Task<CompetitionOverviewDto> LoadCompetitionOverviewAsync(ResultFilter filter)
    {
        var query = await BuildFilteredQueryAsync(filter);
        if (query is null)
            return new CompetitionOverviewDto();

        var days = await LoadOverviewDaysAsync(query);
        var orgCompId = await LoadOrgCompIdAsync(days);

        // Источники стартового протокола (CompetitionSources): у окружных чемпионатов их
        // несколько на одно соревнование, и подтабы таба Start list строятся по ним.
        // Совместимость: привязок ещё может не быть — тогда список выводим из скалярного
        // OrgCompId, и клиент видит ровно один источник, как до этой таблицы.
        var startListSources = await BuildStartListSourcesAsync(days, orgCompId);

        var resultCount = days.Sum(d => d.ResultCount);
        // Личные пловцы; эстафетные строки не раздувают счётчик участников.
        var swimmerCount = await query.Where(r => r.RelayId == null)
            .Select(r => r.SwimmerId).Distinct().CountAsync();
        var clubCount = await query.Where(r => !r.Club.IsPseudo)
            .Select(r => r.ClubId).Distinct().CountAsync();

        // Наградной ли протокол: у ненаградных места в протоколе есть, а медалей нет —
        // клиент по этому флагу прячет медальные блоки Overview (см. CompetitionOverviewDto).
        // Any, а не All: если у многодневного события наградной хотя бы один день, медали есть.
        var hasAwards = await query.AnyAsync(r => r.Competition.IsAward);

        // ♂/♀ (design_handoff вариант 4) — та же проекция с фильтром по полу.
        var bestSwim = await BestSwimProjection(query).FirstOrDefaultAsync();
        var bestSwimMale = await BestSwimProjection(query.Where(r => r.Gender == "male")).FirstOrDefaultAsync();
        var bestSwimFemale = await BestSwimProjection(query.Where(r => r.Gender == "female")).FirstOrDefaultAsync();

        var medalRows = await LoadMedalRowsAsync(query, filter);
        var topMedalists = BuildMedalists(medalRows, null);
        var topMedalistsMale = BuildMedalists(medalRows, "male");
        var topMedalistsFemale = BuildMedalists(medalRows, "female");

        var highPointAwards = await LoadHighPointAwardsAsync(query, filter);

        // Клубный зачёт — переиспользуем GetClubSummaryAsync (свой кэш внутри);
        // по полу — тот же расчёт с Gender-фильтром. Значения в Results.Gender —
        // "male"/"female" (плюс "mixed" у эстафет и "none"), НЕ "M"/"F"; у Swimmer.Gender
        // те же "male"/"female" (сверено по базе 10.10.2026).
        var topClubs = (await GetClubSummaryAsync(filter)).Take(10).ToList();
        var appliedRules = await LoadAppliedClubRulesAsync(query);
        var clubPointsVerified = await LoadClubPointsVerifiedAsync(query);
        var clubPointsUnofficial = await IsClubPointsUnofficialAsync(query);
        var clubPointsMismatchNote = await LoadClubPointsMismatchNoteAsync(query, clubPointsVerified);
        var topClubsMen = (await GetClubSummaryAsync(CloneWithGender(filter, "male"))).Take(3).ToList();
        var topClubsWomen = (await GetClubSummaryAsync(CloneWithGender(filter, "female"))).Take(3).ToList();

        var newRecords = await DetectNewRecordsAsync(query);

        var overview = new CompetitionOverviewDto
        {
            OrgCompId = orgCompId,
            StartListSources = startListSources,
            Summary = new OverviewSummaryDto
            {
                ResultCount = resultCount,
                DayCount = days.Count,
                SwimmerCount = swimmerCount,
                ClubCount = clubCount
            },
            HasAwards = hasAwards,
            Days = days,
            BestSwim = bestSwim,
            BestSwimMale = bestSwimMale,
            BestSwimFemale = bestSwimFemale,
            TopClubs = topClubs,
            ClubPointsRules = appliedRules,
            ClubPointsVerified = clubPointsVerified,
            ClubPointsMismatchNote = clubPointsMismatchNote,
            ClubPointsUnofficial = clubPointsUnofficial,
            TopClubsMen = topClubsMen,
            TopClubsWomen = topClubsWomen,
            TopMedalists = topMedalists,
            TopMedalistsMale = topMedalistsMale,
            TopMedalistsFemale = topMedalistsFemale,
            HighPointAwards = highPointAwards,
            Records = newRecords
        };

        return overview;
    }

    // ── Дни и стартовый протокол ───────────────────────────────────────────────

    /// <summary>Дни источника: для события — по одному на Competition-день, для однодневного — один.</summary>
    private static async Task<List<OverviewDayDto>> LoadOverviewDaysAsync(IQueryable<ResultRecord> query)
    {
        var days = await query
            .GroupBy(r => new
            {
                r.CompetitionId,
                r.Competition.Date,
                r.Competition.DayNumber,
                r.Competition.SubName
            })
            .Select(g => new OverviewDayDto
            {
                CompetitionId = g.Key.CompetitionId,
                Date = g.Key.Date,
                DayNumber = g.Key.DayNumber,
                SubName = g.Key.SubName,
                ResultCount = g.Count()
            })
            .ToListAsync();
        days = days
            .OrderBy(d => d.DayNumber ?? int.MaxValue)
            .ThenBy(d => ParseDayDate(d.Date))
            .ToList();
        return days;
    }

    /// <summary>
    /// compID федерации для таба Start list (шаг С7 плана start-list-plan.md): берём у
    /// первого дня в выборке — Competition.OrgCompId, а если пуст (штамп многодневки
    /// стоит на событии, см. CompetitionIdentity) — Competition.Event.OrgCompId.
    /// </summary>
    private async Task<int?> LoadOrgCompIdAsync(IReadOnlyList<OverviewDayDto> days)
    {
        var firstCompetitionId = days.Select(d => d.CompetitionId).FirstOrDefault();
        var orgCompId = firstCompetitionId != 0
            ? await _db.Competitions.AsNoTracking()
                .Where(c => c.Id == firstCompetitionId)
                .Select(c => c.OrgCompId ?? (c.Event != null ? c.Event.OrgCompId : null))
                .FirstOrDefaultAsync()
            : null;
        return orgCompId;
    }

    /// <summary>
    /// Источники стартового протокола соревнования для подтабов таба Start list.
    ///
    /// Порядок ровно тот, в котором подтабы нумеруются, поэтому он фиксирован здесь, а не
    /// на клиенте: SortOrder → дата → compID. Номер (<c>index</c>) назначается тут же — на
    /// клиенте он оказался бы производным от порядка массива и «поехал» бы при любой
    /// пересортировке.
    ///
    /// Когда привязок нет (а их нет у всех соревнований, заведённых до CompetitionSources),
    /// возвращаем один синтетический источник из скалярного OrgCompId. Так у клиента ОДИН
    /// путь кода вместо двух, и старые соревнования продолжают показывать свой протокол.
    /// </summary>
    private async Task<List<OverviewStartListSourceDto>> BuildStartListSourcesAsync(
        IReadOnlyList<OverviewDayDto> days, int? fallbackOrgCompId)
    {
        var dayIds = days.Select(d => d.CompetitionId).ToList();

        var links = dayIds.Count > 0
            ? await _db.CompetitionSources.AsNoTracking()
                .Where(s => dayIds.Contains(s.CompetitionId))
                .Select(s => new { s.CompetitionId, s.OrgCompId, s.SourceDate, s.SourceName, s.SortOrder })
                .ToListAsync()
            : [];

        var ordered = links
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.SourceDate ?? DateTime.MaxValue)
            .ThenBy(s => s.OrgCompId)
            .ToList();

        // Синтетический источник вместо пустого списка — см. сводку метода.
        if (ordered.Count == 0)
        {
            if (fallbackOrgCompId is null) return [];
            var firstDay = days.FirstOrDefault();
            return
            [
                new OverviewStartListSourceDto
                {
                    OrgCompId = fallbackOrgCompId.Value,
                    CompetitionId = firstDay?.CompetitionId ?? 0,
                    Index = 1,
                    Date = ShortDay(firstDay?.Date),
                    DateIso = IsoDay(firstDay?.Date),
                    SourceName = firstDay?.SubName,
                    EntryCount = await CountEntriesAsync(fallbackOrgCompId.Value)
                }
            ];
        }

        // Счётчики заявок одним запросом: подтаб подписан числом, а заборов может не быть
        // вовсе (привязка сделана, кнопку ещё не нажимали) — тогда честный ноль.
        var orgIds = ordered.Select(s => s.OrgCompId).Distinct().ToList();
        var counts = await _db.CompetitionEntries.AsNoTracking()
            .Where(e => orgIds.Contains(e.OrgCompId))
            .GroupBy(e => e.OrgCompId)
            .Select(g => new { OrgCompId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OrgCompId, x => x.Count);

        return ordered.Select((s, i) => new OverviewStartListSourceDto
        {
            OrgCompId = s.OrgCompId,
            CompetitionId = s.CompetitionId,
            Index = i + 1,
            Date = s.SourceDate?.ToString("dd/MM", CultureInfo.InvariantCulture)
                   ?? ShortDay(days.FirstOrDefault(d => d.CompetitionId == s.CompetitionId)?.Date),
            DateIso = s.SourceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                      ?? IsoDay(days.FirstOrDefault(d => d.CompetitionId == s.CompetitionId)?.Date),
            SourceName = s.SourceName,
            EntryCount = counts.TryGetValue(s.OrgCompId, out var c) ? c : 0
        }).ToList();
    }

    private async Task<int> CountEntriesAsync(int orgCompId)
        => await _db.CompetitionEntries.AsNoTracking().CountAsync(e => e.OrgCompId == orgCompId);

    /// <summary>dd/MM/yyyy → dd/MM для подписи подтаба; непарсимая дата → null.</summary>
    private static string? ShortDay(string? date)
        => date is not null && DateTime.TryParseExact(date, "dd/MM/yyyy", CultureInfo.InvariantCulture,
               DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM", CultureInfo.InvariantCulture)
            : null;

    /// <summary>Дата дня dd/MM/yyyy → yyyy-MM-dd; непарсимая → null. День недели чипа сессии.</summary>
    private static string? IsoDay(string? date)
        => date is not null && DateTime.TryParseExact(date, "dd/MM/yyyy", CultureInfo.InvariantCulture,
               DateTimeStyles.None, out var d)
            ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;

    /// <summary>Дата дня dd/MM/yyyy → DateTime для сортировки; непарсимая → MaxValue (в конец).</summary>
    private static DateTime ParseDayDate(string date)
        => DateTime.TryParseExact(date, "dd/MM/yyyy", CultureInfo.InvariantCulture,
               DateTimeStyles.None, out var d) ? d : DateTime.MaxValue;

    // ── Best swim ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Лучший заплыв — максимум FINA-очков; тай-брейк по времени, затем Id (стабильность).
    /// Пара-очки с FINA не сравнимы (Р67): 931 за 1:22.76 на 100 в/с иначе забирал «лучший
    /// заплыв» чемпионата. Помеченные ошибки протокола тоже (Р69): отсечка 50 м вместо финиша
    /// на 400 в/с дала 117 333 очка — как и рекорды, помеченная строка в зачётных выборках не участвует.
    /// </summary>
    private static IQueryable<OverviewBestSwimDto> BestSwimProjection(IQueryable<ResultRecord> q) =>
        q.Where(r => !r.TimeFail && !r.IsParaPoints && r.SuspectReason == null && r.InternationalPoints > 0)
         .OrderByDescending(r => r.InternationalPoints)
         .ThenBy(r => r.TimeMillisecond)
         .ThenBy(r => r.Id)
         .Select(r => new OverviewBestSwimDto
         {
             ResultId = r.Id,
             SwimmerId = r.SwimmerId,
             FirstName = r.Swimmer.FirstName,
             LastName = r.Swimmer.LastName,
             FirstNameEn = r.Swimmer.FirstNameEn,
             LastNameEn = r.Swimmer.LastNameEn,
             Club = r.Club.Name,
             ClubId = r.ClubId,
             StyleName = r.Style.Name,
             Distance = r.Distance,
             Gender = r.Gender,
             Time = r.TimeOriginal,
             SuspectReason = r.SuspectReason,
             Points = r.InternationalPoints,
             IsRelay = r.RelayId != null,
             RelayTeamName = r.Relay != null ? r.Relay.TeamName : null,
             DayNumber = r.Competition.DayNumber,
             CompetitionId = r.CompetitionId
         });

    // ── Most decorated ─────────────────────────────────────────────────────────

    /// <summary>
    /// Медальный зачёт: личные заплывы + эстафеты. TimeFail медаль не даёт.
    /// Медаль возрастной ступени одна, даже если ступень разыграна дважды за день
    /// (утренний зачёт + вечерний финал): раунды схлопываются по лучшему месту.
    /// Клубных очков это не касается — там задвоение официальное.
    /// </summary>
    private async Task<List<MedalRow>> LoadMedalRowsAsync(IQueryable<ResultRecord> query, ResultFilter filter)
    {
        var personalMedals = await LoadPersonalMedalsAsync(query, filter);
        var relayMedals = await LoadRelayMedalsAsync(query, filter);

        var medalRows = RoundMedalCollapser
            .Collapse(personalMedals, r => r.MedalKey, r => r.Round, r => r.Position)
            .Concat(relayMedals)
            .ToList();
        return medalRows;
    }

    /// <summary>
    /// При combine-all медаль определяется объединённым местом дисциплины — иначе на
    /// одной странице «золото» по протоколу заплыва, а очки клубу по общему зачёту.
    /// Проекция — в анонимный тип, маппинг в MedalRow уже в памяти: фильтр по месту стоит
    /// ПОСЛЕ проекции, а обращение к свойству record'а EF перевести в SQL не может
    /// (InMemory-провайдер это не ловит — он считает всё клиентски).
    /// </summary>
    private static async Task<IEnumerable<MedalRow>> LoadPersonalMedalsAsync(
        IQueryable<ResultRecord> query, ResultFilter filter)
    {
        var personalMedals = (await query
            .Where(r => r.RelayId == null && !r.TimeFail)
            .Select(r => new
            {
                r.SwimmerId,
                r.Swimmer.FirstName,
                r.Swimmer.LastName,
                r.Swimmer.FirstNameEn,
                r.Swimmer.LastNameEn,
                Club = r.Club.Name,
                r.ClubId,
                r.Gender,
                // Prelim: протокольное место не медаль (ранжир сессии); объединённое место,
                // если оно лежит на prelim-строке (лучший заплыв был утром), остаётся.
                // Общий финал «כללי» медали тоже не даёт — она у возрастной ступени (Р43).
                Position = filter.Combined && r.Competition.ShowCombineAllResults
                    ? (r.CombinedPlace ?? (r.HeatType == "prelim" || r.HeatType == "extra"
                        || r.Round == ResultRounds.FinalOpen ? null : r.Position))
                    : (r.HeatType == "prelim" || r.HeatType == "extra"
                        || r.Round == ResultRounds.FinalOpen ? null : r.Position),
                StyleName = r.Style.Name,
                r.Distance,
                r.EventStyleAge,
                r.Round
            })
            .Where(r => r.Position != null && r.Position <= 3)
            .ToListAsync())
            .Select(r => new MedalRow(
                r.SwimmerId, r.FirstName, r.LastName, r.FirstNameEn, r.LastNameEn,
                r.Club, r.ClubId, r.Gender, r.Position, false,
                $"{r.SwimmerId}|{r.StyleName}|{r.Distance}|{r.EventStyleAge}", r.Round));
        return personalMedals;
    }

    /// <summary>
    /// Эстафетная медаль принадлежит ВСЕЙ команде — разворачиваем строку на ноги через
    /// RelayMembers (docs/relays.md: считать по владельцу строки — классический баг, медаль
    /// получил бы только якорь). Пол берём у самого пловца: эстафеты бывают смешанные,
    /// и Gender строки результата для разбивки ♂/♀ не годится.
    /// </summary>
    private async Task<IEnumerable<MedalRow>> LoadRelayMedalsAsync(IQueryable<ResultRecord> query, ResultFilter filter)
    {
        var relayMedals = (await query
            .Where(r => r.RelayId != null && !r.TimeFail)
            .Select(r => new
            {
                r.RelayId,
                Club = r.Club.Name,
                r.ClubId,
                Position = filter.Combined && r.Competition.ShowCombineAllResults
                    ? (r.CombinedPlace ?? (r.HeatType == "prelim" || r.HeatType == "extra" ? null : r.Position))
                    : (r.HeatType == "prelim" || r.HeatType == "extra" ? null : r.Position)
            })
            .Where(r => r.Position != null && r.Position <= 3)
            .Join(_db.RelayMembers.AsNoTracking(),
                r => r.RelayId, m => m.RelayId,
                (r, m) => new
                {
                    m.SwimmerId,
                    m.Swimmer.FirstName,
                    m.Swimmer.LastName,
                    m.Swimmer.FirstNameEn,
                    m.Swimmer.LastNameEn,
                    r.Club,
                    r.ClubId,
                    m.Swimmer.Gender,
                    r.Position
                })
            .ToListAsync())
            .Select(r => new MedalRow(
                r.SwimmerId, r.FirstName, r.LastName, r.FirstNameEn, r.LastNameEn,
                r.Club, r.ClubId, r.Gender, r.Position, true));
        return relayMedals;
    }

    /// <summary>
    /// Топ-медалисты общие и по полу (design_handoff вариант 4). gender == null → без фильтра.
    /// Порядок — золото → серебро → бронза, а НЕ по сумме наград: три бронзы не выше одного
    /// золота. Отдаём всех с идентичным набором (ничья), как в High Point Award.
    /// </summary>
    private static IReadOnlyList<OverviewMedalistDto> BuildMedalists(List<MedalRow> medalRows, string? gender)
    {
        var ranked = medalRows
            .Where(r => gender == null || r.Gender == gender)
            .GroupBy(r => r.SwimmerId)
            .Select(g => new OverviewMedalistDto
            {
                SwimmerId = g.Key,
                FirstName = g.First().FirstName,
                LastName = g.First().LastName,
                FirstNameEn = g.First().FirstNameEn,
                LastNameEn = g.First().LastNameEn,
                Club = g.First().Club,
                ClubId = g.First().ClubId,
                Gold = g.Count(r => r.Position == 1),
                Silver = g.Count(r => r.Position == 2),
                Bronze = g.Count(r => r.Position == 3),
                RelayMedals = g.Count(r => r.IsRelay)
            })
            .OrderByDescending(m => m.Gold)
            .ThenByDescending(m => m.Silver)
            .ThenByDescending(m => m.Bronze)
            .ThenBy(m => m.SwimmerId)
            .ToList();

        if (ranked.Count == 0) return [];

        var top = ranked[0];
        var winners = ranked
            .Where(m => m.Gold == top.Gold && m.Silver == top.Silver && m.Bronze == top.Bronze)
            .ToList();

        return winners.Count == 1
            ? winners
            : winners.Select(m => new OverviewMedalistDto
            {
                SwimmerId = m.SwimmerId,
                FirstName = m.FirstName,
                LastName = m.LastName,
                FirstNameEn = m.FirstNameEn,
                LastNameEn = m.LastNameEn,
                Club = m.Club,
                ClubId = m.ClubId,
                Gold = m.Gold,
                Silver = m.Silver,
                Bronze = m.Bronze,
                RelayMedals = m.RelayMedals,
                IsTie = true
            }).ToList();
    }

    /// <summary>
    /// Одна медаль в зачёте «Most decorated»: личная или эстафетная (у эстафетной строка
    /// развёрнута на каждую ногу через RelayMembers). <paramref name="Gender"/> — пол самого
    /// пловца, а не строки результата: эстафеты бывают смешанные.
    /// </summary>
    /// <param name="MedalKey">
    /// Единица награждения — пловец + дисциплина + возрастная ступень. Нужна, чтобы схлопнуть
    /// медали, задвоенные раундами чемпионата «мокдамот и финал» (<see cref="RoundMedalCollapser"/>).
    /// </param>
    private sealed record MedalRow(
        int SwimmerId,
        string FirstName,
        string LastName,
        string FirstNameEn,
        string LastNameEn,
        string Club,
        int ClubId,
        string? Gender,
        int? Position,
        bool IsRelay,
        string MedalKey = "",
        string? Round = null);

    // ── High Point Award ───────────────────────────────────────────────────────

    /// <summary>
    /// High Point Award: лучший по СУММЕ очков в каждом (возраст × пол), раздельно ♂/♀
    /// (design_handoff §High Point Award). Ничья по сумме → несколько наград (is_tie).
    /// Э2.5: если соревнованию привязано правило — счёт по нему, а не по зашитой
    /// FINA-сумме. Именно ради этого правило и заведено: у возрастных соревнований очки
    /// за место плюс замещающий бонус за возрастной рекорд, у «бугрим» — сумма FINA с
    /// одним кубком на пол (§8.A / §8.B.1 плана). Legacy-ветка остаётся для
    /// соревнований без привязки.
    /// </summary>
    private async Task<List<OverviewHighPointDto>> LoadHighPointAwardsAsync(
        IQueryable<ResultRecord> query, ResultFilter filter)
    {
        var hpRows = await LoadHighPointRowsAsync(query, filter);

        // Правило High Point: привязка соревнования важнее подбора по дате (CompetitionRuleResolver).
        // Правил единицы — грузим целиком и применяем в памяти, как в клубном зачёте.
        var swimmerRules = await _db.PointRulesSwimmers.AsNoTracking()
            .Include(r => r.Entries)
            .ToListAsync();

        var hpRule = hpRows.Count == 0 ? null : CompetitionRuleResolver.Resolve(
            swimmerRules,
            hpRows.Select(r => r.RuleId).FirstOrDefault(id => id != null),
            hpRows.Any(r => r.IsMasters),
            DateOnly.FromDateTime(hpRows.Min(r => r.CompetitionDate)));

        return hpRule is null
            ? BuildLegacyHighPointAwards(hpRows)
            : await BuildRuleHighPointAwardsAsync(hpRule, hpRows);
    }

    /// <summary>
    /// Строки зачёта High Point. Возраст = год соревнования − год рождения
    /// (израильская возрастная конвенция).
    /// InternationalPoints > 0 в выборку НЕ входит: правило может считать очки за место
    /// (§8.A), где FINA-очков может не быть вовсе. Legacy-ветка накладывает это
    /// условие в памяти, чтобы её результат остался байт-в-байт прежним.
    /// Эстафеты в личный зачёт НЕ идут — решение Влада 2026-07-28 (§8.2 плана закрыт).
    /// Исключаются здесь, а не флагом IncludeRelays: фильтр обязан действовать и для
    /// legacy-ветки (соревнование без правила), где эстафетные FINA-очки иначе попали бы
    /// в сумму пловца. Флаг правила остаётся вторым рубежом в PointRulesSwimmersScoring.
    /// NB: в медальном зачёте «Most decorated» эстафеты, наоборот, считаются.
    /// Помеченные ошибки протокола (SuspectReason) не участвуют ни очками, ни местом (Р69):
    /// у 1512 неверные времена переставили и места 1–7 заплыва.
    /// </summary>
    private static async Task<List<HighPointSourceRow>> LoadHighPointRowsAsync(
        IQueryable<ResultRecord> query, ResultFilter filter)
    {
        var hpRows = await query
            .Where(r => r.RelayId == null && !r.TimeFail && r.SuspectReason == null
                        && (r.Gender == "male" || r.Gender == "female")
                        && r.Swimmer.BirthYear > 0
                        // combine-all: дисциплина зачитывается один раз — по лучшему заплыву,
                        // иначе повторный старт удваивал бы вклад в сумму FINA.
                        && (!filter.Combined || !r.Competition.ShowCombineAllResults || r.IsBestResult != false))
            .Select(r => new HighPointSourceRow
            {
                SwimmerId = r.SwimmerId,
                FirstName = r.Swimmer.FirstName,
                LastName = r.Swimmer.LastName,
                FirstNameEn = r.Swimmer.FirstNameEn,
                LastNameEn = r.Swimmer.LastNameEn,
                Club = r.Club.Name,
                ClubId = r.ClubId,
                Gender = r.Gender,
                BirthYear = r.Swimmer.BirthYear,
                Year = r.CompetitionDate.Year,
                AgeGroup = r.AgeGroup,
                IsMasters = r.Competition.IsMasters,
                // Пара-очки в сумму FINA не идут (Р67); очки за место правило считает по месту.
                InternationalPoints = r.IsParaPoints ? 0 : r.InternationalPoints,
                // Э2.5: поля для расчёта по правилу. Место берём объединённое, если
                // соревнование его считает и тоггл включён — иначе место в заплыве.
                Place = filter.Combined && r.Competition.ShowCombineAllResults && r.CombinedPlace != null
                    ? r.CombinedPlace
                    : r.Position,
                RuleId = r.Competition.PointRuleSwimmersId,
                CompetitionDate = r.CompetitionDate,
                // Для бонуса за возрастной рекорд (§8.A: 13 за установленный, 8 за повторённый).
                StyleName = r.Style.Name,
                Distance = r.Distance,
                PoolType = r.Competition.PoolType,
                TimeMillisecond = r.TimeMillisecond,
                HeatType = r.HeatType
            })
            .ToListAsync();
        return hpRows;
    }

    /// <summary>
    /// Строка зачёта High Point. Класс с init-свойствами, а не позиционный record: EF
    /// переводит такую проекцию в SQL так же, как анонимный тип, а нужна она в трёх методах.
    /// </summary>
    private sealed class HighPointSourceRow
    {
        public int SwimmerId { get; init; }
        public string FirstName { get; init; } = "";
        public string LastName { get; init; } = "";
        public string FirstNameEn { get; init; } = "";
        public string LastNameEn { get; init; } = "";
        public string Club { get; init; } = "";
        public int ClubId { get; init; }
        public string Gender { get; init; } = "";
        public int BirthYear { get; init; }
        public int Year { get; init; }
        public string AgeGroup { get; init; } = "";
        public bool IsMasters { get; init; }
        public int InternationalPoints { get; init; }
        public int? Place { get; init; }
        public int? RuleId { get; init; }
        public DateTime CompetitionDate { get; init; }
        public string StyleName { get; init; } = "";
        public string Distance { get; init; } = "";
        public string PoolType { get; init; } = "";
        public int? TimeMillisecond { get; init; }
        public string? HeatType { get; init; }
    }

    /// <summary>
    /// Legacy-ветка (правило не привязано): та же логика, что была до Э2.5, включая
    /// условие InternationalPoints > 0 — оно перенесено сюда из SQL-запроса.
    /// Masters: корзина — возрастная ГРУППА ("25-29", как в фильтрах), топ-5 по очкам на пол.
    /// Не-masters: корзина — отдельный возраст (год − год рождения), все возрасты по порядку.
    /// Группировка по возрастным ГРУППАМ — только для masters (Competition.IsMasters).
    /// Молодёжные категории (8-11, 11-14) тоже имеют AgeGroup в данных, но там HPA
    /// должен идти по отдельным возрастам, поэтому детектим строго по флагу masters.
    /// </summary>
    private static List<OverviewHighPointDto> BuildLegacyHighPointAwards(List<HighPointSourceRow> hpRows)
    {
        var isMastersOverview = hpRows.Any(r => r.IsMasters);

        // Сумма очков пловца в (корзина × пол), затем max по корзине; ничья по сумме → все.
        var perSwimmer = hpRows
            .Where(r => r.InternationalPoints > 0)
            .Where(r => isMastersOverview ? !string.IsNullOrEmpty(r.AgeGroup) : (r.Year - r.BirthYear) > 0)
            .GroupBy(r => new
            {
                Bucket = isMastersOverview ? r.AgeGroup : (r.Year - r.BirthYear).ToString(),
                r.Gender,
                r.SwimmerId,
            })
            .Select(g => new
            {
                g.Key.Bucket,
                g.Key.Gender,
                g.Key.SwimmerId,
                g.First().FirstName,
                g.First().LastName,
                g.First().FirstNameEn,
                g.First().LastNameEn,
                g.First().Club,
                g.First().ClubId,
                Age = g.First().Year - g.First().BirthYear,
                g.First().AgeGroup,
                Points = g.Sum(r => r.InternationalPoints)
            })
            .ToList();

        var winners = perSwimmer
            .GroupBy(x => new { x.Bucket, x.Gender })
            .SelectMany(g =>
            {
                var max = g.Max(x => x.Points);
                var w = g.Where(x => x.Points == max).ToList();
                var tie = w.Count > 1;
                return w.Select(x => new OverviewHighPointDto
                {
                    Age = isMastersOverview ? 0 : x.Age,
                    AgeGroup = isMastersOverview ? x.AgeGroup : "",
                    Gender = x.Gender,
                    SwimmerId = x.SwimmerId,
                    FirstName = x.FirstName,
                    LastName = x.LastName,
                    FirstNameEn = x.FirstNameEn,
                    LastNameEn = x.LastNameEn,
                    Club = x.Club,
                    ClubId = x.ClubId,
                    Points = x.Points,
                    IsTie = tie
                });
            });

        // Masters: все возрастные группы по порядку (нижняя граница ↑). Не-masters: по возрасту.
        var highPointAwards = isMastersOverview
            ? winners.OrderBy(a => AgeGroupLow(a.AgeGroup)).ThenBy(a => a.LastName).ToList()
            : winners.OrderBy(a => a.Age).ThenBy(a => a.LastName).ToList();
        return highPointAwards;
    }

    /// <summary>Счёт High Point по привязанному правилу (Э2.5).</summary>
    private async Task<List<OverviewHighPointDto>> BuildRuleHighPointAwardsAsync(
        PointRuleSwimmers hpRule, List<HighPointSourceRow> hpRows)
    {
        var byId = hpRows
            .GroupBy(r => r.SwimmerId)
            .ToDictionary(g => g.Key, g => g.First());

        var recordIndex = await LoadAgeRecordIndexAsync(hpRule);

        var ruleWinners = PointRulesSwimmersScoring.Winners(hpRule, hpRows
            .Select(r => new SwimmerHighPointRow(
                SwimmerId: r.SwimmerId,
                Gender: r.Gender,
                Age: r.Year - r.BirthYear,
                AgeGroup: r.AgeGroup,
                Place: r.Place,
                InternationalPoints: r.InternationalPoints,
                IsRelay: false,
                TimeFail: false,
                Record: RecordStatusOf(recordIndex, r.Year - r.BirthYear, r.Gender, r.PoolType, r.StyleName,
                    r.Distance, r.AgeGroup, r.IsMasters, r.TimeMillisecond),
                HeatType: r.HeatType))
            .ToList());

        // «Только финалы» невыполнимо, лишь пока признака заплыва нет НИ У ОДНОЙ строки
        // (данные импортированы до HeatType). Есть хоть одна — расчёт честный, сноска
        // «по всем заплывам» на клиенте не нужна.
        var finalsOnlyUnavailable = hpRule.FinalsOnly && hpRows.All(r => r.HeatType == null);

        var highPointAwards = ruleWinners
            .Select(w =>
            {
                var src = byId[w.SwimmerId];
                return new OverviewHighPointDto
                {
                    // GroupBy=age-group подаёт номинацию как группу (карточка показывает
                    // её вместо возраста) — так же, как в masters-ветке legacy.
                    Age = hpRule.GroupBy == "age" ? w.Age : 0,
                    AgeGroup = hpRule.GroupBy == "age" ? "" : w.Bucket,
                    Gender = w.Gender,
                    SwimmerId = w.SwimmerId,
                    FirstName = src.FirstName,
                    LastName = src.LastName,
                    FirstNameEn = src.FirstNameEn,
                    LastNameEn = src.LastNameEn,
                    Club = src.Club,
                    ClubId = src.ClubId,
                    Points = w.Points,
                    IsTie = w.IsTie,
                    RuleVersion = hpRule.Version,
                    GroupLabel = hpRule.GroupBy == "age" ? null : w.Bucket,
                    FinalsOnlyUnavailable = finalsOnlyUnavailable
                };
            })
            .OrderBy(a => a.AgeGroup.Length > 0 ? AgeGroupLow(a.AgeGroup) : a.Age)
            .ThenBy(a => a.LastName)
            .ToList();
        return highPointAwards;
    }

    /// <summary>
    /// Бонус за возрастной рекорд начисляется, только если правило его задаёт (иначе индекс пуст).
    /// Индекс строим по той же оси, что и CompetitionRecordsDetector:
    /// gender|pool|style|distance|category|ageKey, где category — age или masters.
    /// </summary>
    private async Task<Dictionary<string, int>> LoadAgeRecordIndexAsync(PointRuleSwimmers hpRule)
    {
        var recordIndex = new Dictionary<string, int>();
        if (hpRule.RecordPoints is not null || hpRule.RecordTiePoints is not null)
        {
            foreach (var rec in await GetIsraelRecordsAsync())
            {
                if (rec.Category is not ("age" or "masters")) continue;
                var ms = CompetitionRecordsDetector.ParseTimeToMs(rec.Time);
                if (ms is null) continue;
                var dist = rec.Distance.EndsWith('m') ? rec.Distance[..^1] : rec.Distance;
                var recKey = $"{rec.Gender}|{rec.PoolType}|{rec.Style}|{dist}|{rec.Category}|{rec.AgeKey}";
                if (!recordIndex.TryGetValue(recKey, out var cur) || ms.Value < cur)
                    recordIndex[recKey] = ms.Value;
            }
        }
        return recordIndex;
    }

    private static RecordStatus RecordStatusOf(Dictionary<string, int> recordIndex, int age, string gender,
        string pool, string style, string distance, string ageGroup, bool isMasters, int? timeMs)
    {
        if (recordIndex.Count == 0 || timeMs is null or <= 0) return RecordStatus.None;
        var category = isMasters ? "masters" : "age";
        var ageKey = isMasters ? ageGroup : age.ToString();
        if (!recordIndex.TryGetValue($"{gender}|{pool}|{style}|{distance}|{category}|{ageKey}", out var recMs))
            return RecordStatus.None;
        if (timeMs.Value < recMs) return RecordStatus.Broken;
        return timeMs.Value == recMs ? RecordStatus.Tied : RecordStatus.None;
    }

    /// <summary>Ведущие цифры возрастной группы — чтобы "90+" (без дефиса) шёл после "85-89", а не в начало.</summary>
    private static int AgeGroupLow(string ag)
    {
        var digits = new string(ag.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : 0;
    }

    // ── Top clubs: правила, сверка, «Unofficial» ───────────────────────────────

    /// <summary>
    /// Правила, по которым посчитан этот зачёт: разрешаем ровно так же, как GetClubSummaryAsync
    /// (привязка соревнования важнее подбора по дате), и схлопываем в уникальные.
    /// </summary>
    private async Task<List<OverviewPointsRuleDto>> LoadAppliedClubRulesAsync(IQueryable<ResultRecord> query)
    {
        var ruleKeys = await query
            .Select(r => new { r.Competition.PointRuleClubsId, r.Competition.IsMasters, r.CompetitionDate })
            .Distinct()
            .ToListAsync();

        var allClubRules = await _db.PointRulesClubs.AsNoTracking().Include(r => r.Entries).ToListAsync();

        var appliedRules = ruleKeys
            .Select(k => CompetitionRuleResolver.Resolve(
                allClubRules, k.PointRuleClubsId, k.IsMasters, DateOnly.FromDateTime(k.CompetitionDate)))
            .OfType<PointRuleClubs>()
            .DistinctBy(r => r.Id)
            .OrderBy(r => r.Version, StringComparer.Ordinal)
            .Select(r => new OverviewPointsRuleDto
            {
                Version = r.Version,
                Description = r.Description,
                Scope = r.Scope,
                EffectiveFrom = r.EffectiveFrom.ToString("yyyy-MM-dd"),
                DefaultPoints = r.DefaultPoints,
                MaxScoringPlace = r.MaxScoringPlace,
                RelayMultiplier = r.RelayMultiplier,
                OverallMultiplier = r.OverallMultiplier,
                MinEntrants = r.MinEntrants,
                Distances = r.Distances,
                AgeBands = r.AgeBands,
                PointsByPlace = r.Entries
                    .OrderBy(e => e.Place)
                    .Select(e => new OverviewPointsRuleEntryDto { Place = e.Place, Points = e.Points })
                    .ToList()
            })
            .ToList();
        return appliedRules;
    }

    /// <summary>
    /// Итог ручной проверки показываем, только если он одинаков у всех соревнований выборки:
    /// «сверено» на одном дне и «расхождение» на другом в один бейдж не складываются.
    /// </summary>
    private static async Task<string?> LoadClubPointsVerifiedAsync(IQueryable<ResultRecord> query)
    {
        var verifiedKinds = await query
            .Select(r => r.Competition.ClubPointsVerifiedKind)
            .Distinct()
            .ToListAsync();

        var clubPointsVerified = verifiedKinds.Count == 1 ? verifiedKinds[0] : null;
        return clubPointsVerified;
    }

    /// <summary>
    /// Официального клубного зачёта нет — наши очки остаются нашей оценкой по шкале правила
    /// (обычно автоподбор = шкала чемпионата Израиля), и витрина подписывает их «Unofficial».
    /// Признак — Competition.IsClubPointsUnofficial. Как и бейдж сверки — только если так у
    /// ВСЕХ соревнований выборки: в сезонном срезе с чемпионатом внутри сумма уже не
    /// «неофициальная целиком».
    /// </summary>
    private static async Task<bool> IsClubPointsUnofficialAsync(IQueryable<ResultRecord> query)
    {
        var officialFlags = await query
            .Select(r => new { r.Competition.HasOfficialClubStanding, r.Competition.ClubPointsDisabled, r.Competition.PointRuleClubsId })
            .Distinct()
            .ToListAsync();

        var clubPointsUnofficial = officialFlags.Count > 0 && officialFlags.All(f =>
            Competition.IsClubPointsUnofficial(f.HasOfficialClubStanding, f.ClubPointsDisabled, f.PointRuleClubsId));
        return clubPointsUnofficial;
    }

    /// <summary>
    /// Объяснение расхождения — только к бейджу расхождения и только если оно одно на всю
    /// выборку: два разных объяснения в одном попапе противоречили бы друг другу.
    /// У многодневки примечание записано каждому дню одинаковым, поэтому сравниваем
    /// содержимое, а не число строк.
    /// </summary>
    private async Task<CompetitionNoteDto?> LoadClubPointsMismatchNoteAsync(
        IQueryable<ResultRecord> query, string? clubPointsVerified)
    {
        CompetitionNoteDto? clubPointsMismatchNote = null;
        if (clubPointsVerified == PointsVerifiedKinds.Mismatch)
        {
            var competitionIds = await query.Select(r => r.CompetitionId).Distinct().ToListAsync();
            var notes = await _db.CompetitionNotes.AsNoTracking()
                .Include(n => n.Texts)
                .Where(n => competitionIds.Contains(n.CompetitionId)
                         && n.Kind == CompetitionNoteKinds.ClubPointsMismatch)
                .ToListAsync();

            var dtos = notes.Select(PointRulesAdminRepository.ToDto).ToList();
            if (dtos.Count > 0 && dtos.All(d => SameNote(d, dtos[0])))
                clubPointsMismatchNote = dtos[0];
        }
        return clubPointsMismatchNote;
    }

    /// <summary>Одинаковы ли примечания дней события: сравниваем то, что увидит читатель.</summary>
    private static bool SameNote(CompetitionNoteDto a, CompetitionNoteDto b) =>
        a.Texts.Count == b.Texts.Count
        && a.Texts.All(t => b.Texts.TryGetValue(t.Key, out var other) && other == t.Value)
        && a.ScaleDiffCaption == b.ScaleDiffCaption
        && a.ScaleDiff.Count == b.ScaleDiff.Count
        && a.ScaleDiff.Zip(b.ScaleDiff).All(p => p.First == p.Second);

    /// <summary>Копия фильтра-источника с Gender — для клубного зачёта по полу.
    /// Копируются только поля источника (как в /api/club-summary).</summary>
    private static ResultFilter CloneWithGender(ResultFilter f, string gender) => new()
    {
        CompetitionId = f.CompetitionId,
        Latest = f.Latest,
        EventId = f.EventId,
        Country = f.Country,
        PoolType = f.PoolType,
        DateFrom = f.DateFrom,
        DateTo = f.DateTo,
        Gender = gender,
        // Без этого зачёт по полу игнорировал бы тоггл и расходился с общим.
        Combined = f.Combined
    };

    // ── New records ────────────────────────────────────────────────────────────

    /// <summary>
    /// Новые рекорды: сравнение заплывов с Records (country/ISR) — серверный
    /// аналог клиентского isRecordTime. Records ~1.7К строк, кэш 10 мин; кандидаты —
    /// минимальная проекция уже отфильтрованного источника.
    /// </summary>
    private async Task<List<OverviewRecordDto>> DetectNewRecordsAsync(IQueryable<ResultRecord> query)
    {
        var records = await GetIsraelRecordsAsync();
        // SuspectReason != null — строка помечена как недостоверная (ошибка источника,
        // напр. 00:32.59 на 100 м баттерфляем в протоколе Маккабиады). Её рекорд показываем
        // с чипом «Under review», но в счётчики он не идёт и чистый рекорд не вытесняет —
        // детектор ищет такие строки отдельно (решение Влада 25.10.2026, Р76).
        var candidateRows = await query
            // Эстафеты — с Э4б (records-relays-plan): состав и команда едут вместе со строкой,
            // пол «none» детектор сам пропустит.
            .Where(r => !r.TimeFail && r.TimeMillisecond != null)
            .Select(r => new RecordCandidateRow(
                r.Id, r.SwimmerId, r.Swimmer.FirstName, r.Swimmer.LastName, r.Club.Name,
                r.Style.Name, r.Distance, r.Gender, r.Competition.PoolType,
                r.Swimmer.BirthYear, r.CompetitionDate, r.TimeMillisecond!.Value,
                r.TimeOriginal, r.Competition.DayNumber, r.Competition.IsMasters, r.AgeGroup,
                r.RelayId != null,
                r.RelayId != null ? r.Relay!.Members.Select(m => m.Swimmer.BirthYear).ToList() : null,
                r.RelayId != null ? r.Relay!.TeamName : null,
                r.SuspectReason))
            .ToListAsync();
        // Ось возраста для сверки со справочником — глобальная настройка RecordAgeAxis
        // (дефолт calendar = как ведёт справочник федерация), см. docs/data-integrity.md §13.
        var newRecords = CompetitionRecordsDetector.Detect(
            records, candidateRows, RecordAgeAxisSetting.From(_settings));
        return newRecords;
    }

    /// <summary>
    /// Рекорды Израиля (country/ISR, все категории) для детекции — кэш 10 мин.
    ///
    /// В кэше — не сущности <c>Record</c>, а их оси (<see cref="CachedRecordAxis"/>): сущность EF
    /// не контракт и в Redis не переносима (docs/plans/cache-tags-plan.md §4-2). Детектор
    /// принимает <c>Record</c>, поэтому на выходе собираем их обратно — он читает только оси.
    /// </summary>
    private async Task<IReadOnlyList<Domain.Entities.Record>> GetIsraelRecordsAsync()
    {
        var axes = await _cache.GetOrCreateAsync("records:country:ISR:all", () =>
            _db.Records.AsNoTracking()
                .Where(r => r.RegionType == "country" && r.RegionCode == "ISR")
                .Select(r => new CachedRecordAxis(
                    r.Category, r.AgeKey, r.Gender, r.PoolType, r.Style, r.Distance, r.Time))
                .ToListAsync(),
            StaticHintsTtl);

        return axes.Select(a => new Domain.Entities.Record
        {
            RegionType = "country",
            RegionCode = "ISR",
            Category = a.Category,
            AgeKey = a.AgeKey,
            Gender = a.Gender,
            PoolType = a.PoolType,
            Style = a.Style,
            Distance = a.Distance,
            Time = a.Time,
        }).ToList();
    }

    /// <summary>Оси рекорда, которые читают детектор новых рекордов и бонус High Point.</summary>
    private sealed record CachedRecordAxis(
        string Category, string AgeKey, string Gender, string PoolType, string Style, string Distance, string Time);
}
