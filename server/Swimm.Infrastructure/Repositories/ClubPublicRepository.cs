using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Domain;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Публичный read-путь клуба — ростер и клубные рекорды (см. <see cref="IClubPublicRepository"/>).
/// Читает через <see cref="SwimmReadDbContext"/> (роль swimm_ro), как остальные публичные
/// репозитории (HubGroups, Records).
/// </summary>
public class ClubPublicRepository : IClubPublicRepository
{
    private readonly SwimmReadDbContext _read;

    /// <summary>
    /// Витринный сезон для карточки Season best — только он, календарный
    /// <c>SeasonMath.CurrentStartYear()</c> здесь запрещён (docs/season-boundary-rule.md).
    /// </summary>
    private readonly IShowcaseSeasonProvider _showcase;

    public ClubPublicRepository(SwimmReadDbContext read, IShowcaseSeasonProvider showcase)
    {
        _read = read;
        _showcase = showcase;
    }

    public async Task<int?> ResolveClubIdAsync(int clubId)
    {
        var club = await _read.Clubs.AsNoTracking()
            .Where(c => c.Id == clubId)
            .Select(c => new { c.Id, c.IsPseudo, c.MergedIntoId })
            .FirstOrDefaultAsync();
        if (club == null) return null;

        if (club.MergedIntoId != null)
        {
            // Один переход по мягкому merge — редиректа нет (решение Влада), отдаём данные
            // приёмника. Дальше разматывать не нужно: merge второго уровня запрещён guard-ом.
            var target = await _read.Clubs.AsNoTracking()
                .Where(c => c.Id == club.MergedIntoId.Value)
                .Select(c => new { c.Id, c.IsPseudo })
                .FirstOrDefaultAsync();
            return target == null || target.IsPseudo ? null : target.Id;
        }

        return club.IsPseudo ? null : club.Id;
    }

    public async Task<ClubRosterPageDto> GetRosterAsync(
        int resolvedClubId, int page, int pageSize, string? gender, int? ageFrom, int? ageTo, int? season)
    {
        // Возраст в сезоне (НЕ зачётная группа Category) — сезон берём из параметра
        // либо из текущего календарного сезона (SeasonMath).
        var seasonYear = season ?? SeasonMath.CurrentStartYear();

        // ⚠ Возраст в сезоне = сколько исполняется в году ОКОНЧАНИЯ сезона
        // (SeasonMath.AgeInSeason): сезон 2025/26 → возраст по 2026 году. Здесь считается
        // тот же год явно, потому что фильтр по возрасту должен переводиться в SQL —
        // границы возраста разворачиваются в границы BirthYear (ниже).
        var ageYear = seasonYear + 1;

        var query = _read.Swimmers.AsNoTracking().Where(s => s.ClubId == resolvedClubId);

        if (!string.IsNullOrWhiteSpace(gender))
        {
            // ⚠ Swimmer.Gender хранится в ДВУХ форматах: подавляющее большинство строк —
            // "male"/"female" (2475/1484 на 2026-08-01), но есть и "M"/"F" (13/2) от старого
            // импорта; плюс "none" и null. XML-док у поля говорит «M / F» и врёт — сверяться
            // с данными, а не с ним. Фильтр обязан принимать оба написания, иначе роcтер по
            // полу вернул бы полтора десятка пловцов на всю базу.
            var isFemale = gender == "female";
            var word = isFemale ? "female" : "male";
            var letter = isFemale ? "F" : "M";
            query = query.Where(s => s.Gender == word || s.Gender == letter);
        }

        // age = ageYear - BirthYear ⇒ границы возраста переводим в границы BirthYear,
        // чтобы фильтр остался переводимым в SQL (без клиентской пост-фильтрации).
        if (ageFrom.HasValue) query = query.Where(s => s.BirthYear <= ageYear - ageFrom.Value);
        if (ageTo.HasValue) query = query.Where(s => s.BirthYear >= ageYear - ageTo.Value);

        var total = await query.CountAsync();

        var pageRows = await query
            .OrderBy(s => s.LastNameEn).ThenBy(s => s.LastName).ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new
            {
                s.Id,
                s.LastName,
                s.FirstName,
                s.LastNameEn,
                s.FirstNameEn,
                s.BirthYear,
                s.Gender
            })
            .ToListAsync();

        var ids = pageRows.Select(s => s.Id).ToList();

        // Счётчики competitions/swims — по Results.ClubId ЭТОГО клуба (не по всем клубам
        // пловца за карьеру), в границах сезона только если season задан явно.
        DateTime? rangeStart = null, rangeEndExclusive = null;
        if (season.HasValue)
        {
            var range = SeasonMath.RangeOf(season.Value);
            rangeStart = range.Start;
            rangeEndExclusive = range.EndExclusive;
        }

        var countsById = new Dictionary<int, (int Competitions, int Swims)>();
        if (ids.Count > 0)
        {
            var countsQuery = _read.Results.AsNoTracking()
                .Where(r => r.ClubId == resolvedClubId && ids.Contains(r.SwimmerId));
            if (rangeStart.HasValue)
                countsQuery = countsQuery.Where(r => r.CompetitionDate >= rangeStart.Value && r.CompetitionDate < rangeEndExclusive!.Value);

            var counts = await countsQuery
                .GroupBy(r => r.SwimmerId)
                .Select(g => new
                {
                    SwimmerId = g.Key,
                    Competitions = g.Select(r => r.CompetitionId).Distinct().Count(),
                    Swims = g.Count()
                })
                .ToListAsync();

            foreach (var c in counts)
                countsById[c.SwimmerId] = (c.Competitions, c.Swims);
        }

        var data = pageRows.Select(s =>
        {
            countsById.TryGetValue(s.Id, out var c);
            return new ClubRosterItemDto
            {
                SwimmerId = s.Id,
                LastName = s.LastName,
                FirstName = s.FirstName,
                LastNameEn = s.LastNameEn,
                FirstNameEn = s.FirstNameEn,
                BirthYear = s.BirthYear,
                // Год рождения бывает не заполнен (BirthYear = 0) — тогда возраста нет,
                // иначе получалось «age 2025» (сезон минус ноль).
                Age = SeasonMath.AgeInSeason(seasonYear, s.BirthYear),
                Gender = s.Gender == "F" ? "female" : s.Gender == "M" ? "male" : s.Gender,
                Competitions = c.Competitions,
                Swims = c.Swims
            };
        }).ToList();

        return new ClubRosterPageDto
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            HasMore = (long)page * pageSize < total,
            Data = data
        };
    }

    public async Task<ClubSeasonBestDto> GetSeasonBestAsync(int resolvedClubId, string? poolType, int? season)
    {
        // Карточка всегда про ОДИН сезон (решение Влада 2026-08-01): «Season 2025/26 Best».
        // Глобальный фильтр сезона страницы она не слушает — иначе при «all seasons»
        // название врало бы, а плитка возраста теряла бы смысл (в 2019-м пловцу было 8,
        // сейчас 15 — «лучшее в 10 лет за всю историю» это уже рекорд клуба, не сезон).
        //
        // ⚠ Умолчание — ВИТРИННЫЙ сезон, а не календарный (docs/season-boundary-rule.md):
        // до зимнего чемпионата новый сезон показывать нечем. С 01.08 по 31.08.2026 здесь
        // стоял SeasonMath.CurrentStartYear(), и 1 сентября карточка опустела — календарь
        // шагнул в сезон, в котором ещё нет ни одного старта.
        var seasonYear = season ?? await _showcase.CurrentStartYearAsync();
        var range = SeasonMath.RangeOf(seasonYear);

        var (leaders, meets) = await LeadersAsync(
            resolvedClubId, poolType, range.Start, range.EndExclusive, seasonYear);

        var groups = leaders
            .GroupBy(x => new { x.Row.StyleName, x.Row.Distance, x.Row.PoolType, x.Row.Gender })
            .Select(g => new ClubSeasonBestGroupDto
            {
                StyleName = g.Key.StyleName,
                Distance = g.Key.Distance,
                PoolType = g.Key.PoolType,
                Gender = g.Key.Gender,
                // Слот уже единственный на ступень — лидер отобран выше.
                Items = g
                    .Select(x => new ClubBestDto
                    {
                        StyleName = g.Key.StyleName,
                        Distance = g.Key.Distance,
                        PoolType = g.Key.PoolType,
                        Gender = g.Key.Gender,
                        TimeOriginal = x.Row.TimeOriginal,
                        SuspectReason = x.Row.SuspectReason,
                        TimeMs = x.Row.TimeMillisecond,
                        SwimmerId = x.Row.SwimmerId,
                        SwimmerName = x.Row.SwimmerName,
                        SwimmerNameEn = x.Row.SwimmerNameEn,
                        CompetitionName = x.Row.CompetitionName,
                        Date = x.Row.Date,
                        Points = x.Row.InternationalPoints,
                        AgeKey = x.Age.Key,
                        AgeLabel = x.Age.Label,
                        AgeOrder = x.Age.Order
                    })
                    // От старших к младшим (решение Влада): «adults»/мастерс впереди, дети
                    // дальше. «n/a» (нет года рождения, Order = 999) держим последним —
                    // при обычном убывании он бы вылез в начало.
                    .OrderBy(b => b.AgeKey == "n/a" ? 1 : 0)
                    .ThenByDescending(b => b.AgeOrder)
                    .ToList()
            })
            .OrderBy(g => g.StyleName)
            .ThenBy(g => g.Distance.Length)
            .ThenBy(g => g.Distance)
            .ThenBy(g => g.PoolType)
            .ThenBy(g => g.Gender)
            .ToList();

        return new ClubSeasonBestDto
        {
            Season = seasonYear,
            SeasonLabel = SeasonMath.Label(seasonYear),
            // Сентябрь–февраль: новый сезон идёт, а карточка показывает прошлый. Без этой
            // заметки пустая (или «прошлогодняя») карточка читается как поломка.
            SeasonNotice = await _showcase.PendingNoticeAsync(),
            Total = groups.Sum(g => g.Items.Count),
            Meets = meets,
            Data = groups
        };
    }

    /// <summary>
    /// Сколько слотов season best у клуба в ПРОИЗВОЛЬНОМ окне дат — для витрины Hero
    /// (витринный сезон режется последним зимним чемпионатом, см. ShowcaseSeason).
    /// Возраст всё равно считается по КАЛЕНДАРНОМУ сезону: возрастные ступени федерации
    /// живут по нему, и подменять их витринной границей нельзя.
    /// </summary>
    public async Task<int> GetSeasonBestCountAsync(
        int resolvedClubId, DateTime start, DateTime endExclusive)
    {
        var seasonYear = SeasonMath.StartYearOf(start);
        var (leaders, _) = await LeadersAsync(resolvedClubId, null, start, endExclusive, seasonYear);
        return leaders.Count;
    }

    /// <summary>
    /// Слоты, где лидер страны — пловец этого клуба, плюс сколько стартов вошло в расчёт.
    ///
    /// ⚠ Выборка НЕ по клубу: лидер определяется по ВСЕЙ базе, и только потом остаются
    /// слоты, где первый — наш (решение Влада 2026-08-01). Исключение SuspectReason
    /// обязательно: помеченная ошибка протокола не должна делать клуб «первым в стране».
    /// Эстафеты исключены — слот про личное время.
    ///
    /// Открытая вода тоже исключена (решение Влада 2026-08-26): море — настоящий чемпионат
    /// (поэтому в медалях и «Championships» оно осталось, Р21), но слот «первый в Израиле»
    /// сравнивает ВРЕМЯ, а морское время несравнимо с бассейновым — течение, трасса, старт.
    /// Признак площадки пока временный, см. <see cref="StandingKinds.OpenWater"/>.
    /// </summary>
    private async Task<(List<LeaderRow> Leaders, int Meets)> LeadersAsync(
        int resolvedClubId, string? poolType, DateTime start, DateTime endExclusive, int seasonYear)
    {
        var query = _read.Results.AsNoTracking()
            .Where(r => r.TimeMillisecond != null
                        && !r.TimeFail
                        && r.RelayId == null
                        && r.SuspectReason == null
                        && r.Competition.StandingKindOverride != StandingKinds.OpenWater
                        && r.CompetitionDate >= start
                        && r.CompetitionDate < endExclusive);

        // ⚠ 25m и 50m — разные времена: PoolType всегда часть ключа слота,
        // фильтр тут лишь сужает выборку под конкретный бассейн.
        if (!string.IsNullOrWhiteSpace(poolType))
            query = query.Where(r => r.Competition.PoolType == poolType);

        // Возраст нужен на строку, поэтому лидеров считаем в памяти: за сезон это ~16 тысяч
        // заплывов по всем клубам — приемлемо, ответ кэшируется.
        var rows = await query
            .Select(r => new SeasonBestRow(
                r.Style.Name,
                r.Distance,
                r.Competition.PoolType,
                r.Gender,
                r.TimeOriginal,
                r.SuspectReason,
                r.TimeMillisecond,
                r.SwimmerId,
                r.ClubId,
                (r.Swimmer.LastName + " " + r.Swimmer.FirstName).Trim(),
                (r.Swimmer.LastNameEn + " " + r.Swimmer.FirstNameEn).Trim(),
                r.Competition.Name,
                r.Competition.Date,
                r.CompetitionDate,
                r.InternationalPoints,
                r.Swimmer.BirthYear))
            .ToListAsync();

        // Сколько соревнований вошло в расчёт — эту цифру карточка обязана показать:
        // «первый в Израиле» у нас значит «первый среди того, что импортировано», а
        // юниорских и взрослых чемпионатов в базе может не быть вовсе.
        var meets = rows.Select(r => r.CompetitionName + "|" + r.Date).Distinct().Count();

        // Слот = дисциплина × возрастная ступень. В каждом слоте оставляем ОДНОГО лидера
        // страны; при равенстве времени — более ранний заплыв.
        // Возраст берётся ПО СЕЗОНУ (SeasonMath.AgeInSeason), а не по дате заплыва.
        var leaders = rows
            .Select(r => new LeaderRow(r, AgeBucket(r.BirthYear is int by ? SeasonMath.AgeInSeason(seasonYear, by) : null)))
            .GroupBy(x => new
            {
                x.Row.StyleName, x.Row.Distance, x.Row.PoolType, x.Row.Gender, AgeKey = x.Age.Key
            })
            .Select(g => g
                .OrderBy(x => x.Row.TimeMillisecond)
                .ThenBy(x => x.Row.CompetitionDate)
                .First())
            // …и только те слоты, где лидер — пловец этого клуба.
            .Where(x => x.Row.ClubId == resolvedClubId)
            .ToList();

        return (leaders, meets);
    }

    private sealed record SeasonBestRow(
        string StyleName, string Distance, string? PoolType, string? Gender, string TimeOriginal,
        string? SuspectReason, int? TimeMillisecond, int SwimmerId, int ClubId, string SwimmerName,
        string SwimmerNameEn, string CompetitionName, string Date, DateTime CompetitionDate,
        int InternationalPoints, int? BirthYear);

    private sealed record LeaderRow(SeasonBestRow Row, (string Key, string Label, int Order) Age);

    /// <summary>
    /// Возрастная ступень — по устройству таблиц федерации: до 18 включительно каждый год
    /// отдельно, 19–24 «adults», 25+ мастерс пятилетками (25-29, 30-34, …).
    ///
    /// На вход идёт возраст В СЕЗОНЕ (<see cref="SeasonMath.AgeInSeason"/>), одинаковый для
    /// всех стартов сезона, а не возраст на день заплыва. null — год рождения не заполнен;
    /// такие заплывы уходят в ступень «n/a» и стоят последними, а не выкидываются: иначе
    /// лучшее время могло бы молча исчезнуть из карточки.
    /// </summary>
    private static (string Key, string Label, int Order) AgeBucket(int? seasonAge)
    {
        if (seasonAge is not int age) return ("n/a", "age n/a", 999);

        if (age <= 18) return (age.ToString(), $"age {age}", age);
        if (age <= 24) return ("adults", "adults", 100);

        var from = (age - 25) / 5 * 5 + 25;
        return ($"{from}-{from + 4}", $"masters {from}-{from + 4}", 100 + from);
    }

    /// <summary>Порядок категорий в стене: сперва абсолютные, потом возрастные, потом мастерс.</summary>
    private static int CategoryOrder(string category) => category switch
    {
        "open" => 0,
        "age" => 1,
        "junior" => 2,
        "masters" => 3,
        _ => 4
    };

    public async Task<ClubRecordWallDto> GetRecordWallAsync(int resolvedClubId, string? poolType)
    {
        // Имена, под которыми клуб мог попасть в справочник рекордов: своё + названия всех
        // склеенных в него дублей (merge физически переносит результаты, но СТРОКИ в Records
        // остаются с историческим написанием).
        var nameRows = await _read.Clubs.AsNoTracking()
            .Where(c => c.Id == resolvedClubId || c.MergedIntoId == resolvedClubId)
            .Select(c => new { c.Name, c.NameEn })
            .ToListAsync();

        var names = nameRows
            .SelectMany(c => new[] { c.Name, c.NameEn })
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (names.Count == 0)
            return new ClubRecordWallDto();

        // ⚠ Связь рекорда с клубом — ТОЛЬКО по названию: у Record нет ни ClubId, ни SwimmerId
        // (см. Record.cs, «3 оси»). Точное совпадение + вариант с суффиксом («… לאומית»,
        // «… אולימפית» — в источнике к названию клуба дописывают площадку/статус старта).
        // Префикс безопаснее «содержит»: «הפועל דולפין נתניה» не подтянет нахарийский
        // «הפועל דולפין נהריה», а вот подстрока подтянула бы чужие клубы с этим же словом.
        //
        // Фильтрация в памяти сознательно: строк в Records ~1.9к на всю базу, а перевести
        // «любое из N названий как префикс» в SQL без склейки OR-ов нельзя. Ответ кэшируется.
        var candidates = await _read.Records.AsNoTracking()
            .Where(r => r.Club != null && r.Club != "")
            .Select(r => new
            {
                r.RegionType, r.RegionCode, r.Category, r.AgeKey, r.Gender,
                r.PoolType, r.Style, r.Distance, r.Time, r.HolderName, r.Club, r.RecordDate
            })
            .ToListAsync();

        var matched = candidates
            .Where(r => string.IsNullOrWhiteSpace(poolType) || r.PoolType == poolType)
            .Where(r => names.Any(n =>
                string.Equals(r.Club, n, StringComparison.OrdinalIgnoreCase) ||
                r.Club!.StartsWith(n + " ", StringComparison.OrdinalIgnoreCase)))
            .Select(r => new ClubOfficialRecordDto
            {
                RegionType = r.RegionType,
                RegionCode = r.RegionCode,
                Category = r.Category,
                AgeKey = r.AgeKey,
                Gender = r.Gender,
                PoolType = r.PoolType,
                Style = r.Style,
                Distance = r.Distance,
                Time = r.Time,
                HolderName = r.HolderName ?? "",
                Club = r.Club!,
                RecordDate = r.RecordDate ?? "",
                IssueReason = null,   // проставляется ниже по реестру претензий
            })
            .OrderBy(r => CategoryOrder(r.Category))
            .ThenBy(r => r.AgeKey.Length)
            .ThenBy(r => r.AgeKey)
            .ThenBy(r => r.Style)
            .ThenBy(r => r.Distance.Length)
            .ThenBy(r => r.Distance)
            .ThenBy(r => r.Gender)
            .ToList();

        // Метка «запись оспаривается» — тот же реестр и тот же ключ, что у публичного API
        // рекордов (RecordIssueKey): иначе значок был бы на одной странице и пропадал на другой.
        var open = await _read.RecordIssues.AsNoTracking()
            .Where(i => i.Status == RecordIssueStatuses.Open
                     || i.Status == RecordIssueStatuses.Reported
                     || i.Status == RecordIssueStatuses.Accepted)
            .Select(i => new
            {
                i.RegionType, i.RegionCode, i.Category, i.AgeKey, i.Gender,
                i.PoolType, i.Style, i.Distance, i.FlaggedTime, i.Reason
            })
            .ToListAsync();

        if (open.Count > 0)
        {
            var byKey = new Dictionary<string, string>();
            foreach (var i in open)
                byKey[RecordIssueKey.Of(i.RegionType, i.RegionCode, i.Category, i.AgeKey,
                    i.Gender, i.PoolType, i.Style, i.Distance, i.FlaggedTime)] = i.Reason;

            // Разнос по кумулятивной лестнице: претензия заведена на одну ступень, а то же
            // достижение живёт и на соседних — иначе карточка ступени 11 без значка (RQ-1).
            var axes = matched.Select((r, idx) => new RecordAxes(
                idx, r.RegionType, r.RegionCode, r.Category, r.AgeKey,
                r.Gender, r.PoolType, r.Style, r.Distance, r.Time, r.HolderName, r.RecordDate)).ToList();
            foreach (var (index, reason) in RecordIssueSpreader.Resolve(axes, byKey))
                matched[index].IssueReason = reason;
        }

        return new ClubRecordWallDto { MatchedNames = names, Data = matched };
    }
}
