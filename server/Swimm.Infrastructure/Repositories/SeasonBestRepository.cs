using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Domain;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Национальный season best (см. <see cref="ISeasonBestRepository"/>). Читает через
/// <see cref="SwimmReadDbContext"/> (роль swimm_ro), как остальные публичные репозитории.
/// </summary>
public class SeasonBestRepository : ISeasonBestRepository
{
    private readonly SwimmReadDbContext _read;

    /// <summary>
    /// Умолчание сезона — витринное (docs/season-boundary-rule.md), не календарное.
    /// </summary>
    private readonly IShowcaseSeasonProvider _showcase;

    public SeasonBestRepository(SwimmReadDbContext read, IShowcaseSeasonProvider showcase)
    {
        _read = read;
        _showcase = showcase;
    }

    /// <summary>Строка-кандидат: всё, что нужно и для отбора лидера, и для ответа.</summary>
    private sealed record Row(
        string Gender, int? BirthYear, int? TimeMs, string? TimeOriginal, int SwimmerId,
        string Name, string? NameEn, string? Club, string? PoolType, string? Competition,
        DateTime Date, int? Points);

    public async Task<SeasonBestNationalDto> GetNationalSeasonBestAsync(
        string style, string distance, string? poolType, int? season, CancellationToken ct = default)
    {
        // Сезон не задан — берём ВИТРИННЫЙ (после последнего зимнего чемпионата), а не
        // календарный: 1 сентября календарь уходит в сезон без единого старта.
        var seasonYear = season ?? await _showcase.CurrentStartYearAsync(ct);
        var (start, endExclusive) = SeasonMath.RangeOf(seasonYear);

        var query = _read.Results.AsNoTracking()
            .Where(r => r.TimeMillisecond != null
                        && !r.TimeFail
                        && r.RelayId == null
                        // Помеченные ошибки протокола не должны становиться «лучшим временем
                        // сезона» — тот же фильтр, что у клубного season best.
                        && r.SuspectReason == null
                        // Masters исключены целиком (решение Влада 2026-08-22): таб стоит рядом
                        // с детскими возрастными рекордами, и взрослые старты там не к месту.
                        && !r.Competition.IsMasters
                        // Открытая вода — не эта витрина (см. OpenWaterOverride).
                        && r.Competition.StandingKindOverride != OpenWaterOverride
                        && r.Style.Name == style
                        && r.Distance == distance
                        && r.CompetitionDate >= start
                        && r.CompetitionDate < endExclusive);

        // ⚠ 25m и 50m — разные времена. Без фильтра оба бассейна попадают в одну выборку
        // (витрина так и делает при pool_type=all), и бассейн виден у каждой записи.
        if (!string.IsNullOrWhiteSpace(poolType))
            query = query.Where(r => r.Competition.PoolType == poolType);

        // Возраст нужен на строку (он сезонный, а не хранимый) — лидера выбираем в памяти,
        // как в клубном season best. Ответ кэшируется на сутки, выборка узкая (одна дистанция).
        var rows = await query
            .Select(r => new Row(
                // Пол берём у ПЛОВЦА, а не из строки: пол человека живёт в карточке, а
                // Results.Gender — пол зачёта заплыва, и он бывает ошибочным (одна кривая
                // шапка протокола уводила пловца в чужую колонку витрины).
                r.Swimmer.Gender ?? r.Gender,
                r.Swimmer.BirthYear,
                r.TimeMillisecond,
                r.TimeOriginal,
                r.SwimmerId,
                (r.Swimmer.LastName + " " + r.Swimmer.FirstName).Trim(),
                (r.Swimmer.LastNameEn + " " + r.Swimmer.FirstNameEn).Trim(),
                r.Club.Name,
                r.Competition.PoolType,
                r.Competition.Name,
                r.CompetitionDate,
                r.InternationalPoints))
            .ToListAsync(ct);

        var meets = rows.Select(r => (r.Competition ?? "") + "|" + r.Date.ToString("yyyy-MM-dd")).Distinct().Count();

        var items = rows
            .Select(r => new
            {
                Row = r,
                Gender = NormalizeGender(r.Gender),
                Age = r.BirthYear is int by ? SeasonMath.AgeInSeason(seasonYear, by) : null,
            })
            // Без года рождения ступени нет — такие заплывы просто выпадают (в клубной
            // карточке для них есть корзина «n/a», здесь таблица строго по возрастам).
            .Where(x => x.Age != null && x.Gender != null)
            .GroupBy(x => new { x.Gender, Age = x.Age!.Value })
            .Select(g => g
                .OrderBy(x => x.Row.TimeMs)
                // При равенстве времени лидер — тот, кто проплыл раньше.
                .ThenBy(x => x.Row.Date)
                .First())
            .Select(x => new SeasonBestNationalItemDto
            {
                Gender = x.Gender!,
                Age = x.Age!.Value,
                Time = x.Row.TimeOriginal ?? "",
                TimeMs = x.Row.TimeMs,
                SwimmerId = x.Row.SwimmerId,
                Name = x.Row.Name,
                NameEn = string.IsNullOrWhiteSpace(x.Row.NameEn) ? null : x.Row.NameEn,
                Club = x.Row.Club,
                PoolType = x.Row.PoolType,
                Competition = x.Row.Competition,
                Date = x.Row.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                Points = x.Row.Points,
            })
            .OrderBy(i => i.Age)
            .ThenBy(i => i.Gender)
            .ToList();

        return new SeasonBestNationalDto
        {
            Season = seasonYear,
            SeasonLabel = SeasonMath.Label(seasonYear),
            // Пояснение «сезон откроется после зимнего чемпионата» — одно на все витрины SB.
            SeasonNotice = await _showcase.PendingNoticeAsync(ct),
            Style = style,
            Distance = distance,
            PoolType = string.IsNullOrWhiteSpace(poolType) ? null : poolType,
            Meets = meets,
            Data = items,
        };
    }

    // ── Список одной дисциплины (страница /season-best) ──────────────────────────────────

    /// <summary>Строка-кандидат списка: всё, что нужно и для ранжирования, и для ответа.</summary>
    private sealed record ListRow(
        long ResultId, string? Gender, int BirthYear, int? TimeMs, string? TimeOriginal,
        int SwimmerId, string Name, string? NameEn, int ClubId, string? Club, string? ClubEn,
        int CompetitionId, string? Competition, string? PoolType, DateTime Date, int Points,
        string? AgeGroup);

    /// <summary>
    /// Открытая вода в эту витрину не входит: «лучший в сезоне» здесь — про бассейн, а море
    /// с его 1600/5000/10000 — другой вид старта, где время несравнимо (течение, трасса).
    /// Признак площадки пока временный — см. <see cref="StandingKinds.OpenWater"/>.
    /// </summary>
    private const string OpenWaterOverride = StandingKinds.OpenWater;

    /// <summary>
    /// Стили, которые витрина считает дисциплинами — общий список
    /// <see cref="Strokes.All"/>. Белый список нужен потому, что в <c>Styles</c> оседали ключи
    /// из кривых протоколов («מטר_חופשי»): в справочнике они безобидны, а в селекторе
    /// дисциплины выглядели бы как шестой вид плавания.
    /// </summary>
    private static readonly string[] CanonicalStyles = Strokes.All;

    public async Task<SeasonBestListDto> GetSeasonBestListAsync(
        SeasonBestListQuery query, CancellationToken ct = default)
    {
        // Умолчание — витринный сезон, как и в GetNationalSeasonBestAsync выше.
        var seasonYear = query.Season ?? await _showcase.CurrentStartYearAsync(ct);
        var (start, endExclusive) = SeasonMath.RangeOf(seasonYear);

        var style = (query.Style ?? "").Trim();
        // «50m» от витрины и «50» из БД — одна дистанция.
        var distance = (query.Distance ?? "").Trim().TrimEnd('m', 'M');

        var db = _read.Results.AsNoTracking()
            .Where(r => r.TimeMillisecond != null
                        && !r.TimeFail
                        && r.RelayId == null
                        && r.SuspectReason == null
                        // Мастерские и обычные старты — ДВЕ непересекающиеся выборки, а не
                        // один список с фильтром: у мастерсов свои соревнования и свой круг
                        // ровесников (группа-пятилетка), и смешивать 12-летних с 47-летними
                        // в одном рейтинге нельзя (решение Влада 2026-08-26; до этого мастерсы
                        // не показывались вовсе).
                        && r.Competition.IsMasters == query.Masters
                        // Морскую трёшку нельзя ставить в один рейтинг с бассейновой:
                        // дистанция та же, а старт другой (см. OpenWaterOverride).
                        && r.Competition.StandingKindOverride != OpenWaterOverride
                        && r.Style.Name == style
                        && r.Distance == distance
                        && r.CompetitionDate >= start
                        && r.CompetitionDate < endExclusive);

        if (!string.IsNullOrWhiteSpace(query.PoolType))
            db = db.Where(r => r.Competition.PoolType == query.PoolType);

        var ageGroup = query.Masters ? query.AgeGroup?.Trim() : null;

        if (query.Masters)
        {
            // У мастерсов ось возраста — группа из протокола, а не год рождения: в ней они
            // плывут и в ней же считаются места.
            if (!string.IsNullOrEmpty(ageGroup))
                db = db.Where(r => r.AgeGroup == ageGroup);
        }
        else if (query.Age is int ageFrom)
        {
            // Возраст сезонный, но в SQL он выражается через год рождения:
            // age = seasonYear + 1 − BirthYear. Считаем границы заранее — так фильтр уходит
            // в индекс, а не в память.
            var ageTo = query.AgeTo ?? ageFrom;
            var birthYearMax = seasonYear + 1 - ageFrom;   // младший возраст → более поздний год
            var birthYearMin = seasonYear + 1 - ageTo;
            db = db.Where(r => r.Swimmer.BirthYear >= birthYearMin && r.Swimmer.BirthYear <= birthYearMax);
        }

        var rows = await db
            .Select(r => new ListRow(
                r.Id,
                // Пол берём у ПЛОВЦА: Results.Gender — пол зачёта заплыва, и он бывает ошибочным.
                r.Swimmer.Gender ?? r.Gender,
                r.Swimmer.BirthYear,
                r.TimeMillisecond,
                r.TimeOriginal,
                r.SwimmerId,
                (r.Swimmer.LastName + " " + r.Swimmer.FirstName).Trim(),
                (r.Swimmer.LastNameEn + " " + r.Swimmer.FirstNameEn).Trim(),
                r.ClubId,
                r.Club.Name,
                r.Club.NameEn,
                r.CompetitionId,
                r.Competition.Name,
                r.Competition.PoolType,
                r.CompetitionDate,
                r.InternationalPoints,
                r.AgeGroup))
            .ToListAsync(ct);

        var gender = NormalizeGender(query.Gender);

        // Пол нормализуем в памяти: в базе он живёт в двух написаниях, и SQL-фильтр по одному
        // из них тихо терял бы половину людей.
        var ordered = rows
            .Select(r => new { Row = r, Gender = NormalizeGender(r.Gender) })
            .Where(x => x.Gender != null && x.Row.BirthYear > 0)
            .Where(x => gender == null || x.Gender == gender)
            .OrderBy(x => x.Row.TimeMs)
            // При равенстве времени раньше идёт тот, кто проплыл раньше; Id — чтобы порядок
            // был устойчивым между запросами (иначе пагинация «дрожит»).
            .ThenBy(x => x.Row.Date)
            .ThenBy(x => x.Row.ResultId)
            .ToList();

        // Какой это по счёту заплыв пловца. Считается по ПОЛНОМУ списку — до фильтра по клубу
        // и до схлопывания в «лучший на пловца», иначе номер попытки менялся бы от фильтра.
        var attemptOf = new Dictionary<int, int>();
        var withAttempt = ordered
            .Select(x =>
            {
                var attempt = attemptOf.TryGetValue(x.Row.SwimmerId, out var seen) ? seen + 1 : 1;
                attemptOf[x.Row.SwimmerId] = attempt;
                return new { x.Row, x.Gender, Attempt = attempt };
            })
            .ToList();

        if (query.BestPerSwimmer)
            withAttempt = withAttempt.Where(x => x.Attempt == 1).ToList();

        var leaderMs = withAttempt.Count > 0 ? withAttempt[0].Row.TimeMs ?? 0 : 0;

        // Места: равные времена делят место, следующий получает свой порядковый номер
        // (1, 2, 2, 4) — как в протоколе.
        var placed = new List<(ListRow Row, string Gender, int Attempt, int Place)>(withAttempt.Count);
        var place = 0;
        int? previousMs = null;
        for (var i = 0; i < withAttempt.Count; i++)
        {
            var x = withAttempt[i];
            if (previousMs == null || x.Row.TimeMs != previousMs) place = i + 1;
            previousMs = x.Row.TimeMs;
            placed.Add((x.Row, x.Gender!, x.Attempt, place));
        }

        // Опции фильтра «Club» — по срезу ДО фильтра по клубу: иначе, выбрав клуб,
        // пользователь терял бы возможность выбрать любой другой.
        var clubs = placed
            .GroupBy(x => x.Row.ClubId)
            .Select(g => new SeasonBestClubOptionDto
            {
                ClubId = g.Key,
                Name = g.First().Row.Club ?? "",
                NameEn = ClubEnOrNull(g.First().Row.Club, g.First().Row.ClubEn),
                Swims = g.Count(),
            })
            .OrderByDescending(c => c.Swims)
            .ThenBy(c => c.NameEn ?? c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Клуб фильтрует ПОКАЗ, но не пересчитывает места: пользователь видит своих пловцов
        // с их местами в общем рейтинге (#1, #7, #23). Пересчёт мест внутри клуба — это уже
        // другой продукт («лучшие в клубе»), он живёт в /api/clubs/{id}/season-best.
        var visible = query.ClubId is int clubId
            ? placed.Where(x => x.Row.ClubId == clubId).ToList()
            : placed;

        var limit = Math.Clamp(query.Limit, 1, 200);
        var offset = Math.Max(query.Offset, 0);

        var data = visible
            .Skip(offset)
            .Take(limit)
            .Select(x => new SeasonBestListItemDto
            {
                Place = x.Place,
                Attempt = x.Attempt,
                ResultId = x.Row.ResultId,
                Time = x.Row.TimeOriginal ?? "",
                TimeMs = x.Row.TimeMs,
                Points = x.Row.Points,
                GapMs = Math.Max((x.Row.TimeMs ?? 0) - leaderMs, 0),
                SwimmerId = x.Row.SwimmerId,
                Name = x.Row.Name,
                NameEn = string.IsNullOrWhiteSpace(x.Row.NameEn) ? null : x.Row.NameEn,
                Gender = x.Gender,
                // BirthYear > 0 гарантирован фильтром выше, поэтому ступень тут всегда есть.
                Age = SeasonMath.AgeInSeason(seasonYear, x.Row.BirthYear) ?? 0,
                AgeGroup = x.Row.AgeGroup,
                ClubId = x.Row.ClubId,
                Club = x.Row.Club,
                ClubEn = ClubEnOrNull(x.Row.Club, x.Row.ClubEn),
                CompetitionId = x.Row.CompetitionId,
                Competition = x.Row.Competition,
                PoolType = x.Row.PoolType,
                Date = x.Row.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            })
            .ToList();

        return new SeasonBestListDto
        {
            Season = seasonYear,
            SeasonLabel = SeasonMath.Label(seasonYear),
            // Пояснение «сезон откроется после зимнего чемпионата» — одно на все витрины SB.
            SeasonNotice = await _showcase.PendingNoticeAsync(ct),
            Style = style,
            Distance = distance,
            PoolType = string.IsNullOrWhiteSpace(query.PoolType) ? null : query.PoolType,
            Gender = gender,
            Age = query.Masters ? null : query.Age,
            AgeTo = query.Masters ? null : query.AgeTo,
            Masters = query.Masters,
            AgeGroup = ageGroup,
            ClubId = query.ClubId,
            BestPerSwimmer = query.BestPerSwimmer,
            Total = visible.Count,
            Offset = offset,
            Limit = limit,
            Swimmers = visible.Select(x => x.Row.SwimmerId).Distinct().Count(),
            Meets = visible.Select(x => x.Row.CompetitionId).Distinct().Count(),
            Clubs = clubs,
            Data = data,
        };
    }

    public async Task<SeasonBestOptionsDto> GetSeasonBestOptionsAsync(CancellationToken ct = default)
    {
        // Сезоны считаем по соревнованиям, а не по результатам: соревнований сотни, строк —
        // десятки тысяч, а ответ один и тот же.
        var meets = await _read.Results.AsNoTracking()
            .Where(r => r.TimeMillisecond != null
                        && !r.Competition.IsMasters
                        && r.Competition.StandingKindOverride != OpenWaterOverride)
            .Select(r => new { r.CompetitionId, r.CompetitionDate })
            .Distinct()
            .ToListAsync(ct);

        var seasons = meets
            .GroupBy(m => SeasonMath.StartYearOf(m.CompetitionDate))
            .Select(g => new SeasonBestSeasonOptionDto
            {
                Season = g.Key,
                Label = SeasonMath.Label(g.Key),
                Meets = g.Select(m => m.CompetitionId).Distinct().Count(),
            })
            .OrderByDescending(s => s.Season)
            .ToList();

        // Умолчание карусели — ВИТРИННЫЙ сезон (docs/season-boundary-rule.md), а не просто
        // самый свежий с данными: иначе первый же импортированный октябрьский старт
        // перебрасывал бы страницу в едва начавшийся сезон. Витринного сезона может не
        // оказаться в списке (данных за него ещё нет) — тогда самый свежий, что есть.
        var showcase = await _showcase.CurrentStartYearAsync(ct);
        var notice = await _showcase.PendingNoticeAsync(ct);
        var preferred = seasons.FirstOrDefault(s => s.Season == showcase) ?? seasons.FirstOrDefault();
        if (preferred is not null) preferred.IsDisplayDefault = true;

        var pairs = await _read.Results.AsNoTracking()
            .Where(r => r.TimeMillisecond != null
                        && r.RelayId == null
                        && !r.Competition.IsMasters
                        // Без этого в селектор дисциплины приезжали 1600/5000/10000 —
                        // дистанции чемпионата в открытой воде (#1547, Эйлат).
                        && r.Competition.StandingKindOverride != OpenWaterOverride
                        && CanonicalStyles.Contains(r.Style.Name))
            .Select(r => new { Style = r.Style.Name, r.Distance })
            .Distinct()
            .ToListAsync(ct);

        var events = pairs
            .GroupBy(p => p.Style)
            .Select(g => new SeasonBestEventOptionDto
            {
                Style = g.Key,
                Distances = g
                    .Select(p => p.Distance)
                    // Эстафетных дистанций («4X50») в неэстафетных строках быть не должно,
                    // но протоколы иногда приносят их и без RelayId — отсекаем явно.
                    .Where(d => !d.Contains('X', StringComparison.OrdinalIgnoreCase))
                    .Distinct()
                    .OrderBy(d => int.TryParse(d, out var n) ? n : int.MaxValue)
                    .ToList(),
            })
            .Where(e => e.Distances.Count > 0)
            .OrderBy(e => Array.IndexOf(CanonicalStyles, e.Style))
            .ToList();

        var pools = await _read.Competitions.AsNoTracking()
            .Where(c => c.PoolType != "")
            .Select(c => c.PoolType)
            .Distinct()
            .ToListAsync(ct);

        // Возрастные группы мастерских протоколов — вторая шкала возраста на витрине.
        // Берём их из данных, а не списком в коде: федерация меняет сетку, и захардкоженная
        // лестница молча разошлась бы с протоколами.
        var groupRows = await _read.Results.AsNoTracking()
            .Where(r => r.TimeMillisecond != null
                        && r.RelayId == null
                        && r.Competition.IsMasters
                        && r.AgeGroup != null
                        && r.AgeGroup != ""
                        && r.Swimmer.BirthYear > 0)
            .Select(r => new { Group = r.AgeGroup!, r.Swimmer.BirthYear, r.CompetitionDate })
            .ToListAsync(ct);

        // ⚠ Группу берём только ту, что сходится с возрастами людей в ней. Причина не
        // теоретическая: у соревнования «ליגה מאסטרס - וייסגל רחובות» ВСЕ 123 строки помечены
        // группой «9-11», а плывут там от 12 до 71 года — это мусор протокола, и на витрине
        // он выглядел бы как настоящая мастерская ступень с бессмысленным списком.
        // Проверяем по медиане (устойчива к одиночным выбросам), с запасом в год на края.
        var ageGroups = groupRows
            .GroupBy(x => x.Group)
            .Where(g => GroupMatchesAges(
                g.Key,
                g.Select(x => SeasonMath.AgeInSeason(SeasonMath.StartYearOf(x.CompetitionDate), x.BirthYear) ?? 0)))
            .Select(g => g.Key)
            .ToList();

        return new SeasonBestOptionsDto
        {
            Seasons = seasons,
            // Карусель стоит на прошлом сезоне — заметка объясняет, почему нового ещё нет.
            SeasonNotice = notice,
            Events = events,
            Pools = pools.OrderBy(p => p).ToList(),
            // Сортировка по НИЖНЕЙ границе: строковая ставила бы «100+» между «19-24» и «25-29».
            AgeGroups = ageGroups
                .OrderBy(g => int.TryParse(g.Split('-')[0].TrimEnd('+'), out var n) ? n : int.MaxValue)
                .ThenBy(g => g, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    /// <summary>
    /// Сходится ли подпись возрастной группы («25-29», «90+») с возрастами тех, кто в ней
    /// плыл. Нужна, потому что в <c>Results.AgeGroup</c> попадает и мусор из кривых шапок
    /// протокола; такую «группу» витрина показывать не должна. Мы её не чиним и не удаляем —
    /// это данные федерации (docs/records-quality-plan.md), а только не пускаем в селектор.
    /// </summary>
    private static bool GroupMatchesAges(string group, IEnumerable<int> ages)
    {
        var parts = group.Split('-', StringSplitOptions.TrimEntries);
        var openEnded = group.TrimEnd().EndsWith('+');
        if (!int.TryParse(parts[0].TrimEnd('+'), out var from)) return false;

        var to = int.MaxValue;
        if (!openEnded && parts.Length > 1 && int.TryParse(parts[1], out var parsedTo)) to = parsedTo;
        else if (!openEnded && parts.Length == 1) to = from;

        var sorted = ages.Where(a => a > 0).OrderBy(a => a).ToList();
        if (sorted.Count == 0) return false;

        var median = sorted[sorted.Count / 2];
        // Запас в год: возраст сезонный, а протокольная сетка кое-где считается по дате старта.
        return median >= from - 1 && (to == int.MaxValue || median <= to + 1);
    }

    /// <summary>
    /// Английское имя клуба — только если оно и правда английское: у 30 клубов из 235
    /// <c>NameEn</c> просто копия ивритского, и отдавать её как «EN» значит врать витрине,
    /// которая на этом поле решает, какое имя показать.
    /// </summary>
    private static string? ClubEnOrNull(string? name, string? nameEn) =>
        string.IsNullOrWhiteSpace(nameEn) || nameEn == name ? null : nameEn;

    /// <summary>
    /// Results.Gender живёт в двух написаниях («male»/«female» и «M»/«F», как и у пловцов) —
    /// наружу отдаём одно, иначе витрина делит один и тот же пол на две колонки.
    /// </summary>
    private static string? NormalizeGender(string? gender)
    {
        var g = gender?.Trim().ToLowerInvariant();
        return g switch
        {
            "male" or "m" => "male",
            "female" or "f" => "female",
            _ => null,
        };
    }
}
