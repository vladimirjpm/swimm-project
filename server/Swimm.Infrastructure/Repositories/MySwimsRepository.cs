using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
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

    public MySwimsRepository(SwimmDbContext db)
    {
        _db = db;
    }

    /// <summary>Стартовый год сезона (сентябрь–август) для даты.</summary>
    private static int SeasonOf(DateTime d) => d.Month >= 9 ? d.Year : d.Year - 1;

    public async Task<MySwimsResponseDto> GetMySwimsAsync(int userId, int? season)
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

        var currentSeason = SeasonOf(DateTime.UtcNow);
        var response = new MySwimsResponseDto
        {
            Swimmers = swimmers,
            Season = season ?? currentSeason,
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
            .Select(ym => ym.Month >= 9 ? ym.Year : ym.Year - 1)
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();

        // CompetitionDate — timestamp WITHOUT time zone → Kind обязан быть Unspecified.
        var seasonStart = new DateTime(response.Season, 9, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var seasonEnd = seasonStart.AddYears(1);

        // 3. Заплывы сезона. Эстафеты приходят и по членству (RelayMembers), а не только
        //    по «владельцу» строки: строка привязана к одной ноге, но принадлежит всем.
        var swims = await _db.Results
            .AsNoTracking()
            .Where(r => r.CompetitionDate >= seasonStart && r.CompetitionDate < seasonEnd
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

        // 4. PB: лучшее время пловца за всё время на (стиль, дистанция), индивидуальные заплывы.
        //    Индекс (SwimmerId, TimeMillisecond) держит выборку дешёвой.
        var bests = await _db.Results
            .AsNoTracking()
            .Where(r => swimmerIds.Contains(r.SwimmerId)
                        && r.RelayId == null && !r.TimeFail && r.TimeMillisecond != null)
            .GroupBy(r => new { r.SwimmerId, r.StyleId, r.Distance })
            .Select(g => new { g.Key.SwimmerId, g.Key.StyleId, g.Key.Distance, Best = g.Min(r => r.TimeMillisecond) })
            .ToListAsync();
        var bestByKey = bests.ToDictionary(b => (b.SwimmerId, b.StyleId, b.Distance), b => b.Best);

        // Времена сезонных заплывов для сравнения с PB — TimeMillisecond не в DTO, добираем.
        var seasonTimes = await _db.Results
            .AsNoTracking()
            .Where(r => resultIds.Contains(r.Id))
            .Select(r => new { r.Id, r.TimeMillisecond })
            .ToListAsync();
        var timeById = seasonTimes.ToDictionary(t => t.Id, t => t.TimeMillisecond);

        foreach (var s in swims)
        {
            if (s.IsRelay || s.TimeFail) continue;
            if (!timeById.TryGetValue(s.ResultId, out var ms) || ms == null) continue;
            if (bestByKey.TryGetValue((s.SwimmerId, s.StyleId, s.Distance), out var best) && best == ms)
                s.IsPb = true;
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
