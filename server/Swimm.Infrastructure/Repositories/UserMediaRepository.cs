using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// 2A: личное owner-only медиа (Sys_UserMedia). Уровень всегда "swimmer", visibility
/// всегда "private" — см. docs/tasks/user-media-2a-sonnet.md. Формат URL валидируется
/// в контроллере через MediaUrlValidator до вызова AddAsync.
/// </summary>
public class UserMediaRepository : IUserMediaRepository
{
    private readonly SwimmDbContext _db;

    public UserMediaRepository(SwimmDbContext db)
    {
        _db = db;
    }

    public async Task<List<UserMediaDto>> GetForUserAsync(int userId, int? swimmerId = null)
    {
        var query = _db.UserMedia
            .AsNoTracking()
            .Where(m => m.UserId == userId);

        if (swimmerId != null)
            // Эстафетное медиа принадлежит всей команде, а привязано к владельцу строки
            // (одна нога). Поэтому карточка пловца видит и своё медиа, и медиа на эстафетах,
            // где он — участник по RelayMembers (см. docs/relays.md). Owner-фильтр (UserId)
            // сохраняется — чужое медиа не подтягивается.
            query = query.Where(m =>
                m.SwimmerId == swimmerId.Value
                || (m.ResultRecord != null && m.ResultRecord.RelayId != null
                    && _db.RelayMembers.Any(rm =>
                        rm.RelayId == m.ResultRecord.RelayId && rm.SwimmerId == swimmerId.Value)));

        return await query
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
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
                // Денормализация для клиентских фильтров My media (сезон клиент выводит из даты).
                CompetitionName = m.Competition != null ? m.Competition.Name : null,
                CompetitionDate = m.Competition != null ? m.Competition.Date : null,
                ClubName = m.ResultRecord != null ? m.ResultRecord.Club.Name : null
            })
            .ToListAsync();
    }

    public async Task<List<SwimmerCompetitionBriefDto>> GetSwimmerCompetitionsBriefAsync(int swimmerId)
        => await _db.Results.AsNoTracking()
            .Where(r => r.SwimmerId == swimmerId && r.RelayId == null)
            .GroupBy(r => new { r.CompetitionId, r.Competition.Name, r.Competition.Date })
            .Select(g => new
            {
                g.Key.CompetitionId,
                g.Key.Name,
                g.Key.Date,
                MaxDate = g.Max(r => r.CompetitionDate),
            })
            .OrderByDescending(x => x.MaxDate)
            .Select(x => new SwimmerCompetitionBriefDto { Id = x.CompetitionId, Name = x.Name, Date = x.Date })
            .ToListAsync();

    public async Task<List<SwimmerResultBriefDto>> GetSwimmerResultsBriefAsync(int swimmerId, int competitionId)
        => await _db.Results.AsNoTracking()
            .Where(r => r.SwimmerId == swimmerId && r.CompetitionId == competitionId && r.RelayId == null)
            .OrderBy(r => r.CompetitionDate).ThenBy(r => r.Id)
            .Select(r => new SwimmerResultBriefDto
            {
                ResultId = r.Id,
                Distance = r.Distance,
                Style = r.Style.Name,
                Time = r.TimeOriginal,
                Date = r.Competition.Date,
            })
            .ToListAsync();

    public async Task<int> CountForUserAsync(int userId)
        => await _db.UserMedia.CountAsync(m => m.UserId == userId);

    public async Task<UserMediaDto?> AddAsync(int userId, AddUserMediaRequest request)
    {
        var swimmerExists = await _db.Swimmers.AnyAsync(s => s.Id == request.SwimmerId);
        if (!swimmerExists) return null;

        // Уровень выводим сами из привязок — клиенту не доверяем.
        string level;
        long? resultId = null;
        int? competitionId = null;

        if (request.ResultId != null)
        {
            // result_id задан → заплыв должен существовать и принадлежать этому пловцу;
            // CompetitionId берём из заплыва, клиентский competition_id игнорируем.
            var result = await _db.Results
                .Where(r => r.Id == request.ResultId.Value)
                .Select(r => new { r.SwimmerId, r.CompetitionId })
                .FirstOrDefaultAsync();

            if (result == null || result.SwimmerId != request.SwimmerId)
                return null;

            level = "result";
            resultId = request.ResultId.Value;
            competitionId = result.CompetitionId;
        }
        else if (request.CompetitionId != null)
        {
            var competitionExists = await _db.Competitions.AnyAsync(c => c.Id == request.CompetitionId.Value);
            if (!competitionExists) return null;

            level = "competition";
            competitionId = request.CompetitionId.Value;
        }
        else
        {
            level = "swimmer";
        }

        var media = new UserMedia
        {
            UserId = userId,
            SwimmerId = request.SwimmerId,
            Level = level,
            MediaType = request.MediaType,
            SourceType = request.SourceType,
            Url = request.Url,
            ResultId = resultId,
            CompetitionId = competitionId,
            Visibility = "private",
            CreatedAt = DateTime.UtcNow
        };

        _db.UserMedia.Add(media);
        await _db.SaveChangesAsync();

        return new UserMediaDto
        {
            Id = media.Id,
            SwimmerId = media.SwimmerId,
            Level = media.Level,
            MediaType = media.MediaType,
            SourceType = media.SourceType,
            Url = media.Url,
            ResultId = media.ResultId,
            CompetitionId = media.CompetitionId,
            CreatedAt = media.CreatedAt
        };
    }

    public async Task<bool> RemoveAsync(int userId, int mediaId)
    {
        // IDOR: фильтруем по userId — нельзя удалить чужое медиа.
        var media = await _db.UserMedia
            .FirstOrDefaultAsync(m => m.Id == mediaId && m.UserId == userId);

        if (media == null) return false;

        _db.UserMedia.Remove(media);
        await _db.SaveChangesAsync();
        return true;
    }
}
