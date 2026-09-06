using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Domain;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Агрегат «My media v3»: заплывы favorite-пловцов за сезон + медиа юзера + реакции.
/// Всё через RW-контекст (Sys_-таблицы). Фильтры только по Id/датам — по урокам перфа
/// никаких JOIN по Name. Объём одного сезона нескольких пловцов мал (сотни строк),
/// поэтому донасыщение (медиа, реакции, PB) — отдельными точечными запросами.
/// </summary>
public class MySwimsRepository : IMySwimsRepository
{
    private readonly SwimmDbContext _db;
    private readonly ISeasonBestRepository _seasonBest;
    private readonly IShowcaseSeasonProvider _showcase;

    public MySwimsRepository(
        SwimmDbContext db, ISeasonBestRepository seasonBest, IShowcaseSeasonProvider showcase)
    {
        _db = db;
        _seasonBest = seasonBest;
        _showcase = showcase;
    }

    /// <summary>Меньше двух сверстников на ступени — бейджа SB нет (общий порог продукта).</summary>
    private const int MinPeersForSeasonBest = 2;

    /// <summary>Стартовый год сезона (сентябрь–август) для даты. Общий календарь — <see cref="SeasonMath"/>.</summary>
    private static int SeasonOf(DateTime d) => SeasonMath.StartYearOf(d);

    /// <summary>Свод пола к ключу ступени — как в <c>SeasonBestRepository</c>, иначе ключи не сойдутся.</summary>
    private static string? NormalizeGender(string? gender) => gender?.Trim().ToLowerInvariant() switch
    {
        "male" or "m" => "male",
        "female" or "f" => "female",
        _ => null,
    };

    public async Task<MySwimsResponseDto> GetMySwimsAsync(int userId, int? season, bool allSeasons = false)
    {
        // 1. Favorite-пловцы (чипы). Primary первым — как в дизайне.
        var swimmers = await _db.UserFavorites
            .AsNoTracking()
            .Where(f => f.UserId == userId && f.TargetType == "swimmer" && f.SwimmerId != null)
            .OrderByDescending(f => f.IsPrimary)
            .ThenBy(f => f.SortOrder)
            .Select(f => new MySwimmerDto
            {
                Id = f.SwimmerId!.Value,
                Name = (f.Swimmer!.LastName + " " + f.Swimmer.FirstName).Trim(),
                IsPrimary = f.IsPrimary,
            })
            .ToListAsync();

        // Дефолт — ВИТРИННЫЙ сезон, не календарный: 1 сентября календарная граница уводит
        // страницу в сезон, где стартов ещё нет, и она показывает пустоту
        // (docs/season-boundary-rule.md, журнал 01.09.2026). Провайдер — общий на продукт.
        var currentSeason = await _showcase.CurrentStartYearAsync();
        var response = new MySwimsResponseDto
        {
            Swimmers = swimmers,
            Season = season ?? currentSeason,
            AllSeasons = allSeasons,
        };
        if (swimmers.Count == 0) return response;

        var swimmerIds = swimmers.Select(s => s.Id).ToList();

        // 2. Сезоны с результатами (для селекта Season) — год+месяц, свёртка в сезон в памяти.
        var yearMonths = await _db.Results
            .AsNoTracking()
            .Where(r => swimmerIds.Contains(r.SwimmerId)
                        || (r.RelayId != null && _db.RelayMembers.Any(m =>
                                m.RelayId == r.RelayId && swimmerIds.Contains(m.SwimmerId))))
            .Select(r => new { r.CompetitionDate.Year, r.CompetitionDate.Month })
            .Distinct()
            .ToListAsync();
        response.Seasons = yearMonths
            .Select(ym => SeasonMath.StartYearOf(new DateTime(ym.Year, ym.Month, 1)))
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();

        // CompetitionDate — timestamp WITHOUT time zone → Kind обязан быть Unspecified (см. SeasonMath).
        var (seasonStart, seasonEnd) = SeasonMath.RangeOf(response.Season);

        // 3. Заплывы сезона. Эстафеты приходят и по членству (RelayMembers), а не только
        //    по «владельцу» строки: строка привязана к одной ноге, но принадлежит всем.
        var swims = await _db.Results
            .AsNoTracking()
            .Where(r => (allSeasons || (r.CompetitionDate >= seasonStart && r.CompetitionDate < seasonEnd))
                        && (swimmerIds.Contains(r.SwimmerId)
                            || (r.RelayId != null && _db.RelayMembers.Any(m =>
                                    m.RelayId == r.RelayId && swimmerIds.Contains(m.SwimmerId)))))
            .OrderByDescending(r => r.CompetitionDate).ThenBy(r => r.Id)
            .Select(r => new MySwimDto
            {
                ResultId = r.Id,
                SwimmerId = r.SwimmerId,
                RelayId = r.RelayId,
                CompetitionId = r.CompetitionId,
                CompetitionName = r.Competition.Name,
                CompetitionDate = r.Competition.Date,
                PoolType = r.Competition.PoolType,
                Date = r.CompetitionDate.ToString("yyyy-MM-dd"),
                Distance = r.Distance,
                Style = r.Style.Name,
                StyleId = r.StyleId,
                IsRelay = r.RelayId != null,
                Place = r.Position,
                Points = r.InternationalPoints,
                Time = r.TimeOriginal,
                // Пол/год рождения/возраст события — ключи ступени рекорда и SB. Пол берём у
                // ПЛОВЦА (Results.Gender — фоллбек): кривая шапка протокола уводит в чужую ступень.
                Gender = r.Swimmer.Gender ?? r.Gender,
                BirthYear = r.Swimmer.BirthYear,
                EventStyleAge = r.EventStyleAge,
                SuspectReason = r.SuspectReason,
                TimeFail = r.TimeFail,
            })
            .ToListAsync();
        response.Swims = swims;

        // Донасыщаем членство эстафет: SwimmerId всех ног (для чип-фильтра/счётчиков на клиенте).
        var swimRelayIds = swims.Where(s => s.RelayId != null).Select(s => s.RelayId!.Value).Distinct().ToList();
        if (swimRelayIds.Count > 0)
        {
            var membersByRelay = (await _db.RelayMembers
                .AsNoTracking()
                .Where(m => swimRelayIds.Contains(m.RelayId))
                .Select(m => new { m.RelayId, m.SwimmerId })
                .ToListAsync())
                .GroupBy(m => m.RelayId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.SwimmerId).ToList());
            foreach (var s in swims)
                if (s.RelayId != null && membersByRelay.TryGetValue(s.RelayId.Value, out var ids))
                    s.MemberSwimmerIds = ids;
        }

        var resultIds = swims.Select(s => s.ResultId).ToList();

        // 3b. Плитка соревнования в шапке карточки (CompetitionTile) — те же данные, что у
        //     /api/competitions: канонический таб считает общий CompetitionCategories.Canonical,
        //     чемпионат — ручной флаг админки. Своей эвристики по названию тут нет и быть не должно.
        var competitionIds = swims.Select(s => s.CompetitionId).Distinct().ToList();
        var categoryKeysByComp = (await _db.CategoryCompetitions.AsNoTracking()
                .Where(cc => competitionIds.Contains(cc.CompetitionId))
                .Select(cc => new { cc.CompetitionId, cc.Category!.Key })
                .ToListAsync())
            .GroupBy(x => x.CompetitionId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Key).ToHashSet());
        var compFlags = await _db.Competitions.AsNoTracking()
            .Where(c => competitionIds.Contains(c.Id))
            .Select(c => new { c.Id, c.IsMasters, c.IsChampionship })
            .ToListAsync();
        var flagsById = compFlags.ToDictionary(c => c.Id);

        foreach (var s in swims)
        {
            if (!flagsById.TryGetValue(s.CompetitionId, out var flags)) continue;
            s.IsChampionship = flags.IsChampionship;
            s.Category = CompetitionCategories.Canonical(
                flags.IsMasters, categoryKeysByComp.GetValueOrDefault(s.CompetitionId));
        }

        // 4. PB — личный рекорд: лучшее время пловца за всё время на (стиль, дистанция),
        //    индивидуальные заплывы. Правило продукта, менять его тут нельзя.
        //    Индекс (SwimmerId, TimeMillisecond) держит выборку дешёвой.
        var bests = await _db.Results
            .AsNoTracking()
            .Where(r => swimmerIds.Contains(r.SwimmerId)
                        && r.RelayId == null && !r.TimeFail && r.TimeMillisecond != null)
            .GroupBy(r => new { r.SwimmerId, r.StyleId, r.Distance })
            .Select(g => new { g.Key.SwimmerId, g.Key.StyleId, g.Key.Distance, Best = g.Min(r => r.TimeMillisecond) })
            .ToListAsync();
        var bestByKey = bests.ToDictionary(b => (b.SwimmerId, b.StyleId, b.Distance), b => b.Best);

        // Времена сезонных заплывов + поля ступени SB (пол ПЛОВЦА, год рождения, отбраковки
        // сезонной таблицы) — в DTO их нет, добираем одним запросом.
        var seasonMeta = await _db.Results
            .AsNoTracking()
            .Where(r => resultIds.Contains(r.Id))
            .Select(r => new
            {
                r.Id,
                r.TimeMillisecond,
                Gender = r.Swimmer.Gender ?? r.Gender,
                r.Swimmer.BirthYear,
                r.SuspectReason,
                r.CompetitionDate,
                IsMasters = r.Competition.IsMasters,
                StandingKind = r.Competition.StandingKindOverride,
            })
            .ToListAsync();
        var timeById = seasonMeta.ToDictionary(t => t.Id, t => t.TimeMillisecond);
        var metaById = seasonMeta.ToDictionary(t => t.Id);

        foreach (var s in swims)
        {
            if (s.IsRelay || s.TimeFail) continue;
            if (!timeById.TryGetValue(s.ResultId, out var ms) || ms == null) continue;
            if (bestByKey.TryGetValue((s.SwimmerId, s.StyleId, s.Distance), out var best) && best == ms)
                s.IsPb = true;
        }

        // 4b. SB — ОБЩЕЕ правило продукта, а не своё: «быстрейший в стране в этом сезоне на своей
        //     ступени» (пол × возраст в сезоне × стиль × дистанция × бассейн) при peers >= 2.
        //     Эталон — ТА ЖЕ таблица, что отдаёт /api/season-best/table и по которой ставит бейдж
        //     протокол (`SeasonBestRepository.GetSeasonBestTableAsync`, `season-best-table.ts`).
        //     Личное лучшее время сезона тут ни при чём — за личное отвечает PB.
        //     Состав таблицы задан сервером: мастерские старты, открытая вода, эстафеты и
        //     помеченные SuspectReason в неё не входят, поэтому такие строки пропускаем сами —
        //     иначе сравнили бы с эталоном, в который они не попадали.
        //     В режиме «All» строки из разных сезонов, поэтому таблиц столько же: заплыв
        //     меряется ступенью СВОЕГО сезона, иначе прошлогоднее время сравнивалось бы с
        //     нынешним лидером.
        var stepsBySeason = new Dictionary<int, Dictionary<(string Style, string Distance, string Pool, string Gender, int Age), (int TimeMs, int Peers)>>();
        foreach (var year in seasonMeta.Select(m => SeasonOf(m.CompetitionDate)).Distinct())
        {
            var table = await _seasonBest.GetSeasonBestTableAsync(year);
            stepsBySeason[year] = table.Data.ToDictionary(
                i => (i.Style, i.Distance, i.PoolType, i.Gender, i.Age),
                i => (i.TimeMs, i.Peers));
        }

        foreach (var s in swims)
        {
            if (s.IsRelay || s.TimeFail) continue;
            if (!metaById.TryGetValue(s.ResultId, out var meta) || meta.TimeMillisecond == null) continue;
            if (meta.SuspectReason != null || meta.IsMasters || meta.StandingKind == StandingKinds.OpenWater) continue;
            var swimSeason = SeasonOf(meta.CompetitionDate);
            if (!stepsBySeason.TryGetValue(swimSeason, out var stepByKey)) continue;
            var gender = NormalizeGender(meta.Gender);
            var age = meta.BirthYear > 0 ? SeasonMath.AgeInSeason(swimSeason, meta.BirthYear) : null;
            if (gender == null || age == null || string.IsNullOrWhiteSpace(s.PoolType)) continue;
            if (stepByKey.TryGetValue((s.Style, s.Distance, s.PoolType, gender, age.Value), out var step)
                && step.Peers >= MinPeersForSeasonBest
                && step.TimeMs == meta.TimeMillisecond.Value)
                s.IsSb = true;
        }

        // 5. Всё медиа юзера одной выборкой; раскладка по уровням.
        var media = await _db.UserMedia
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Select(m => new UserMediaDto
            {
                Id = m.Id,
                SwimmerId = m.SwimmerId,
                Level = m.Level,
                MediaType = m.MediaType,
                SourceType = m.SourceType,
                Url = m.Url,
                ResultId = m.ResultId,
                CompetitionId = m.CompetitionId,
                CreatedAt = m.CreatedAt,
                SwimmerName = (m.Swimmer.LastName + " " + m.Swimmer.FirstName).Trim(),
                ResultLabel = m.ResultRecord != null
                    ? m.ResultRecord.Style.Name + " " + m.ResultRecord.Distance + " · "
                      + m.ResultRecord.Competition.Date
                    : null,
                CompetitionName = m.Competition != null ? m.Competition.Name : null,
                CompetitionDate = m.Competition != null ? m.Competition.Date : null,
            })
            .ToListAsync();

        // 6. Реакции: ❤ по медиа юзера и 🎉 по заплывам сезона (счётчик + мой флаг).
        var mediaIds = media.Select(m => m.Id).ToList();
        if (mediaIds.Count > 0)
        {
            var likes = await _db.UserReactions
                .AsNoTracking()
                .Where(x => x.Kind == "like" && x.MediaId != null && mediaIds.Contains(x.MediaId.Value))
                .GroupBy(x => x.MediaId!.Value)
                .Select(g => new { MediaId = g.Key, Count = g.Count(), Mine = g.Any(x => x.UserId == userId) })
                .ToListAsync();
            var likesById = likes.ToDictionary(l => l.MediaId);
            foreach (var m in media)
            {
                if (!likesById.TryGetValue(m.Id, out var l)) continue;
                m.LikesCount = l.Count;
                m.MyLike = l.Mine;
            }
        }

        if (resultIds.Count > 0)
        {
            var cheers = await _db.UserReactions
                .AsNoTracking()
                .Where(x => x.Kind == "congrats" && x.ResultId != null && resultIds.Contains(x.ResultId.Value))
                .GroupBy(x => x.ResultId!.Value)
                .Select(g => new { ResultId = g.Key, Count = g.Count(), Mine = g.Any(x => x.UserId == userId) })
                .ToListAsync();
            var cheersById = cheers.ToDictionary(c => c.ResultId);
            foreach (var s in swims)
            {
                if (!cheersById.TryGetValue(s.ResultId, out var c)) continue;
                s.CongratsCount = c.Count;
                s.MyCheer = c.Mine;
            }
        }

        // 7. Раскладка медиа: к заплыву (в строки), к соревнованию, свободные (Unlinked).
        var byResult = media.Where(m => m.Level == "result" && m.ResultId != null)
            .ToLookup(m => m.ResultId!.Value);
        foreach (var s in swims)
            s.Media = byResult[s.ResultId].ToList();

        response.CompetitionMedia = media.Where(m => m.Level == "competition").ToList();
        response.UnlinkedMedia = media.Where(m => m.Level == "swimmer").ToList();

        return response;
    }
}
