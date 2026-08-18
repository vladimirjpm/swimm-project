using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Шов страницы спортсмена (<see cref="ISwimmerPageRepository"/>, этап A1).
/// Только I/O: вся арифметика сезона живёт в <see cref="SeasonAggregator"/>.
///
/// Выборка идёт ДВУМЯ запросами по индексам, а не одним с <c>OR</c>: личные заплывы читаются
/// по <c>IX_Results_SwimmerId</c>, эстафетные — по <c>IX_Results_RelayId</c>. Условие
/// <c>SwimmerId = x OR RelayId IN (…)</c> в одном запросе планировщик разворачивает в скан
/// всей таблицы — тот самый класс ошибки, что уже ловили на фильтре по имени стиля.
/// </summary>
public class SwimmerPageRepository : ISwimmerPageRepository
{
    private readonly SwimmReadDbContext _read;
    private readonly ICacheService _cache;

    /// <summary>Совпадает с TTL остальных публичных полезных нагрузок (CachedJsonExtensions).</summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    public SwimmerPageRepository(SwimmReadDbContext read, ICacheService cache)
    {
        _read = read;
        _cache = cache;
    }

    public async Task<IReadOnlyList<SeasonSwimRow>> GetSwimsAsync(int swimmerId)
    {
        if (swimmerId <= 0) return [];

        var key = $"swimmer-swims:{swimmerId}";
        var cached = await _cache.GetAsync<List<SeasonSwimRow>>(key);
        if (cached is not null) return cached;

        var personal = await Project(
            _read.Results.AsNoTracking().Where(r => r.SwimmerId == swimmerId && r.RelayId == null));

        // Эстафеты, где пловец значится ногой (docs/relays.md): строка результата привязана
        // к первой ноге, поэтому «свои» эстафеты обычным матчем по SwimmerId не находятся.
        var relayIds = await _read.RelayMembers.AsNoTracking()
            .Where(m => m.SwimmerId == swimmerId)
            .Select(m => m.RelayId)
            .Distinct()
            .ToListAsync();

        // Плюс строка, где он же владелец: у старых импортов RelayMembers может быть пуст.
        var relay = relayIds.Count > 0
            ? await Project(_read.Results.AsNoTracking()
                .Where(r => r.RelayId != null && relayIds.Contains(r.RelayId.Value)))
            : [];

        var ownRelay = await Project(
            _read.Results.AsNoTracking().Where(r => r.SwimmerId == swimmerId && r.RelayId != null));

        var rows = personal
            .Concat(relay)
            .Concat(ownRelay)
            .GroupBy(r => r.ResultId)
            .Select(g => g.First())
            .OrderBy(r => r.CompetitionDate)
            .ThenBy(r => r.ResultId)
            // SwimmerId у эстафеты в базе — первая нога; для страницы это ВСЕГДА запрошенный
            // пловец, иначе PB-детекция ключевалась бы по чужому id.
            .Select(r => r.SwimmerId == swimmerId ? r : r with { SwimmerId = swimmerId })
            .ToList();

        await _cache.SetAsync(key, rows, Ttl);
        return rows;
    }

    public async Task<IReadOnlyList<DateTime>> GetWinterChampionshipDatesAsync()
    {
        var key = "winter-championship-dates";
        var cached = await _cache.GetAsync<List<DateTime>>(key);
        if (cached is not null) return cached;

        var raw = await _read.Competitions.AsNoTracking()
            .Where(c => c.IsChampionship)
            .Select(c => new { c.Date, c.PoolType, c.StandingKindOverride })
            .ToListAsync();

        var dates = raw
            .Where(c => StandingKinds.Resolve(true, c.PoolType, c.StandingKindOverride) == StandingKinds.Winter)
            .Select(c => ParseDate(c.Date))
            .Where(d => d != DateTime.MinValue)
            .ToList();

        await _cache.SetAsync(key, dates, Ttl);
        return dates;
    }

    public async Task<IReadOnlyDictionary<int, string?>> GetStandingKindsAsync(
        IEnumerable<int> competitionIds)
    {
        var ids = competitionIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, string?>();

        var raw = await _read.Competitions.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.IsChampionship, c.PoolType, c.StandingKindOverride })
            .ToListAsync();

        return raw.ToDictionary(
            c => c.Id,
            c => StandingKinds.Resolve(c.IsChampionship, c.PoolType, c.StandingKindOverride));
    }

    public async Task<SwimmerAgeGroupDto?> GetLadderGroupAsync(IEnumerable<int> competitionIds)
    {
        var ids = competitionIds.Distinct().ToList();
        if (ids.Count == 0) return null;

        var ladder = Category.ReservedKeys;
        var rows = await _read.CategoryCompetitions.AsNoTracking()
            .Where(cc => ids.Contains(cc.CompetitionId))
            .Select(cc => new
            {
                cc.Category.Key,
                cc.Category.Name,
                cc.Category.Badge,
                cc.Category.DisplayOrder,
            })
            .ToListAsync();

        var top = rows
            .Where(c => ladder.Contains(c.Key))
            .GroupBy(c => c.Key)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.First().DisplayOrder)
            .FirstOrDefault();

        if (top is null) return null;

        var category = top.First();
        return new SwimmerAgeGroupDto
        {
            Code = category.Key,
            Label = category.Name,
            Badge = category.Badge,
        };
    }

    public async Task<int> CountRecordsHeldAsync(int swimmerId)
    {
        if (swimmerId <= 0) return 0;

        var s = await _read.Swimmers.AsNoTracking()
            .Where(x => x.Id == swimmerId)
            .Select(x => new { x.FirstName, x.LastName, x.FirstNameEn, x.LastNameEn })
            .FirstOrDefaultAsync();
        if (s is null) return 0;

        // Оба порядка имени и EN-вариант — как в именном пути карьеры: в справочнике рекордов
        // держатель записан строкой и порядок частей не гарантирован.
        var names = new[]
            {
                $"{s.FirstName} {s.LastName}", $"{s.LastName} {s.FirstName}",
                $"{s.FirstNameEn} {s.LastNameEn}", $"{s.LastNameEn} {s.FirstNameEn}",
            }
            .Select(n => n.Trim())
            .Where(n => n.Length > 1)
            .Distinct()
            .ToList();
        if (names.Count == 0) return 0;

        return await _read.Records.AsNoTracking()
            .CountAsync(r => r.HolderName != null && names.Contains(r.HolderName));
    }

    public async Task<IReadOnlyDictionary<string, int>> GetClubBestMsAsync(int clubId)
    {
        if (clubId <= 0) return new Dictionary<string, int>();

        var key = $"club-best-ms:{clubId}";
        var cached = await _cache.GetAsync<Dictionary<string, int>>(key);
        if (cached is not null) return cached;

        // Группировка в SQL: у крупного клуба полторы тысячи строк, тянуть их ради минимума
        // незачем. Отбор строк — те же правила, что у SeasonAggregator.IsCountable.
        var grouped = await _read.Results.AsNoTracking()
            .Where(r => r.ClubId == clubId
                        && r.RelayId == null
                        && !r.TimeFail
                        && r.SuspectReason == null
                        && r.TimeMillisecond > 0)
            .GroupBy(r => new { r.StyleId, r.Distance, r.Competition.PoolType, r.Gender })
            .Select(g => new
            {
                g.Key.StyleId,
                g.Key.Distance,
                g.Key.PoolType,
                g.Key.Gender,
                Ms = g.Min(x => x.TimeMillisecond),
            })
            .ToListAsync();

        var best = grouped
            .Where(g => g.Ms is > 0)
            .ToDictionary(
                g => SeasonAggregator.DisciplineKey(g.StyleId, g.Distance, g.PoolType, g.Gender),
                g => g.Ms!.Value);

        await _cache.SetAsync(key, best, Ttl);
        return best;
    }

    public async Task<IReadOnlyDictionary<string, NationalAgeRecordRow>> GetNationalAgeRecordsAsync(
        string? regionCode, string? gender, int age)
    {
        var region = string.IsNullOrWhiteSpace(regionCode) ? "ISR" : regionCode.Trim().ToUpperInvariant();
        var sex = NormalizeGender(gender);
        if (sex is null || age <= 0) return new Dictionary<string, NationalAgeRecordRow>();

        var ageKey = age.ToString();
        var key = $"national-age-records:{region}:{sex}:{ageKey}";
        var cached = await _cache.GetAsync<Dictionary<string, NationalAgeRecordRow>>(key);
        if (cached is not null) return cached;

        var records = await _read.Records.AsNoTracking()
            .Where(r => r.RegionType == "country"
                        && r.RegionCode == region
                        && r.Category == "age"
                        && r.AgeKey == ageKey
                        && r.Gender == sex)
            .Select(r => new { r.Style, r.Distance, r.PoolType, r.Time, r.HolderName })
            .ToListAsync();

        // Справочник хранит стиль СТРОКОЙ, а ключ дисциплины — по StyleId: без карты
        // «имя → id» сравнение молча не нашло бы ни одной пары.
        var styleIds = await _read.Styles.AsNoTracking()
            .Select(s => new { s.Id, s.Name })
            .ToListAsync();
        var byName = styleIds
            .GroupBy(s => s.Name.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Id);

        // Метка «запись оспаривается» — тот же реестр, что у публичного API рекордов и стены
        // клуба, иначе значок был бы на одной странице и пропадал на другой (инвариант И11).
        // Ступень в ключе НЕ учитываем: лестница федерации кумулятивная, одно достижение
        // растянуто на 2–4 возраста, а претензия заводится на одну ступень (баг RQ-1).
        var issues = (await _read.RecordIssues.AsNoTracking()
                .Where(i => i.RegionType == "country"
                            && i.RegionCode == region
                            && i.Category == "age"
                            && i.Gender == sex
                            && (i.Status == RecordIssueStatuses.Open
                                || i.Status == RecordIssueStatuses.Reported
                                || i.Status == RecordIssueStatuses.Accepted))
                .Select(i => new { i.PoolType, i.Style, i.Distance, i.FlaggedTime, i.Reason })
                .ToListAsync())
            .GroupBy(i => IssueKey(i.PoolType, i.Style, i.Distance, i.FlaggedTime))
            .ToDictionary(g => g.Key, g => g.First().Reason);

        var map = new Dictionary<string, NationalAgeRecordRow>();
        foreach (var r in records)
        {
            if (!byName.TryGetValue((r.Style ?? "").Trim().ToLowerInvariant(), out var styleId)) continue;

            var discipline = SeasonAggregator.DisciplineKey(styleId, r.Distance, r.PoolType, sex);
            issues.TryGetValue(IssueKey(r.PoolType, r.Style, r.Distance, r.Time), out var issue);
            var row = new NationalAgeRecordRow(
                r.Time, SwimTime.ParseToMs(r.Time), r.HolderName, ageKey, issue);

            // Дубли в справочнике возможны — держим самый быстрый.
            if (!map.TryGetValue(discipline, out var cur)
                || (row.TimeMs is int ms && (cur.TimeMs is null || ms < cur.TimeMs)))
                map[discipline] = row;
        }

        await _cache.SetAsync(key, map, Ttl);
        return map;
    }

    /// <summary>Ключ претензии без возрастной ступени — одно достижение живёт на нескольких.</summary>
    private static string IssueKey(string? poolType, string? style, string? distance, string? time) =>
        string.Join('|',
            (poolType ?? "").Trim().ToLowerInvariant(),
            (style ?? "").Trim().ToLowerInvariant(),
            (distance ?? "").Trim().ToLowerInvariant().TrimEnd('m'),
            (time ?? "").Trim());

    /// <summary>
    /// Пол к виду справочника. ⚠ В базе он живёт в ДВУХ форматах: «male»/«female» у
    /// подавляющего большинства и «M»/«F» у горстки строк (урок страницы клуба: фильтр по
    /// «M»/«F» вернул полтора десятка пловцов на всю базу).
    /// </summary>
    private static string? NormalizeGender(string? gender) => gender?.Trim().ToLowerInvariant() switch
    {
        "male" or "m" => "male",
        "female" or "f" => "female",
        _ => null,
    };

    /// <summary>
    /// Проекция в <see cref="SeasonSwimRow"/>. Поля соревнования (бассейн, название, флаги)
    /// тянутся здесь же — без них строку результата не нарисовать, а второй проекции быть
    /// не должно.
    /// </summary>
    private static async Task<List<SeasonSwimRow>> Project(IQueryable<ResultRecord> q) =>
        await q.Select(r => new SeasonSwimRow(
                r.Id,
                r.SwimmerId,
                r.CompetitionId,
                r.CompetitionDate,
                r.StyleId,
                r.Distance,
                r.Gender,
                r.Competition.PoolType,
                r.EventCategory,
                r.TimeMillisecond,
                r.TimeFail,
                r.SuspectReason,
                r.RelayId != null)
        {
            Position = r.Position,
            PositionAgeGroup = r.PositionAgeGroup,
            HeatType = r.HeatType,
            InternationalPoints = r.InternationalPoints,
            TimeOriginal = r.TimeOriginal,
            TimeSplit = r.TimeSplit,
            EventId = r.Competition.EventId,
            CompetitionName = r.Competition.Name,
            IsChampionship = r.Competition.IsChampionship,
            IsAward = r.Competition.IsAward,
            IsMasters = r.Competition.IsMasters,
            StyleName = r.Style.Name,
            ClubId = r.ClubId,
            AgeGroup = r.AgeGroup,
            EventStyleAge = r.EventStyleAge,
        })
        .ToListAsync();

    /// <summary>Competition.Date хранится строкой dd/MM/yyyy (историческое).</summary>
    private static DateTime ParseDate(string date) =>
        DateTime.TryParseExact(date, "dd/MM/yyyy",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var dt)
            ? dt
            : DateTime.MinValue;
}
