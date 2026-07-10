using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Чтение приватных тренировок группы из Sys_TrainingSessions/Sys_TrainingResults и маппинг в
/// форму клиентского ResultWrap. Приватность обеспечивает контроллер (Authorize + права);
/// физически таблицы всё равно в Sys_-контуре (swimm_ro их не видит).
/// </summary>
public class HubGroupTrainingRepository : IHubGroupTrainingRepository
{
    private readonly SwimmDbContext _db;

    public HubGroupTrainingRepository(SwimmDbContext db) => _db = db;

    public async Task<int?> ResolveGroupIdBySlugAsync(string slug)
    {
        var s = slug.Trim().ToLowerInvariant();
        var group = await _db.HubGroups
            .Where(g => g.Slug.ToLower() == s)
            .Select(g => new { g.Id })
            .FirstOrDefaultAsync();
        return group?.Id;
    }

    public async Task<bool> IsActiveAccountMemberAsync(int hubGroupId, int userId)
        => await _db.HubGroupUserMembers.AnyAsync(m =>
            m.HubGroupId == hubGroupId && m.UserId == userId && m.Status == "active");

    public async Task<TrainingSourceDto> GetTrainingsAsync(int hubGroupId)
    {
        var group = await _db.HubGroups
            .Where(g => g.Id == hubGroupId)
            .Select(g => new { g.Name, g.NameEn })
            .FirstOrDefaultAsync();

        var rows = await _db.TrainingResults
            .Where(r => r.Session!.HubGroupId == hubGroupId)
            .Include(r => r.Session)
            .Include(r => r.Swimmer!).ThenInclude(s => s.Club)
            .Include(r => r.Style)
            // Порядок как в источнике: тренировка → сет → повтор.
            .OrderBy(r => r.Session!.Date)
            .ThenBy(r => r.SetNo).ThenBy(r => r.OrderNo).ThenBy(r => r.Id)
            .ToListAsync();

        // Медиа тренировок группы (HubGroupMedia.TrainingId != null) — привязка по TrainingSession.Id,
        // видна той же аудитории, что и сама тренировка (контроллер уже проверил права до вызова).
        var mediaBySession = await _db.HubGroupMedia.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId && m.TrainingId != null)
            .OrderBy(m => m.Id)
            .Select(m => new { m.TrainingId, Dto = new HubGroupMediaDto
            {
                Id = m.Id, MediaType = m.MediaType, SourceType = m.SourceType, Url = m.Url, Caption = m.Caption,
            } })
            .ToListAsync();
        var mediaLookup = mediaBySession
            .GroupBy(m => m.TrainingId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Dto).ToList());

        var results = rows.Select(r => Map(r, mediaLookup)).ToList();

        return new TrainingSourceDto
        {
            Title = group?.NameEn ?? group?.Name ?? "Trainings",
            IsMasters = true,
            Results = results,
        };
    }

    private static TrainingRowDto Map(
        Swimm.Domain.Entities.TrainingResult r, Dictionary<int, List<HubGroupMediaDto>> mediaLookup)
    {
        var session = r.Session!;
        var sw = r.Swimmer!;
        var styleName = r.Style?.Name ?? string.Empty;
        // Возраст на тренировке (masters-норматив) — год сессии минус год рождения.
        var eventAge = sw.BirthYear > 0 ? (session.Date.Year - sw.BirthYear) : 0;

        return new TrainingRowDto
        {
            Competition = session.Name ?? session.ExternalTrainingId,
            IsMasters = true,
            AgeGroup = sw.BirthYear > 0 ? sw.BirthYear.ToString() : string.Empty,
            Date = session.Date.ToString("dd/MM/yyyy"),

            Event = $"{styleName} {r.Distance}m",
            EventStyleName = styleName,
            EventStyleLen = r.Distance,
            EventStyleGender = r.Gender,
            EventStyleAge = eventAge > 0 ? eventAge.ToString() : string.Empty,
            PoolType = session.PoolType,

            SwimmerId = sw.Id,
            LastName = sw.LastName,
            FirstName = sw.FirstName,
            LastNameEn = sw.LastNameEn,
            FirstNameEn = sw.FirstNameEn,
            BirthYear = sw.BirthYear,
            Club = sw.Club?.Name ?? string.Empty,
            ClubEn = sw.Club?.NameEn ?? string.Empty,

            Time = r.TimeOriginal,
            TimeSplit = string.Empty,
            TimeFail = false,
            InternationalPoints = 0,

            Training = new TrainingInfoDto
            {
                TrainingId = long.TryParse(session.ExternalTrainingId, out var tid) ? tid : 0,
                SessionId = session.Id,
                TrainingName = session.Name ?? string.Empty,
                Set = r.SetNo,
                Order = r.OrderNo,
                Interval = r.IntervalSec,
                Intensity = r.Intensity,
                ExpectedTime = FormatMs(r.ExpectedTimeMs),
                IsPaddles = r.IsPaddles,
                IsBuoy = r.IsBuoy,
                Media = mediaLookup.TryGetValue(session.Id, out var media) ? media : [],
            },
        };
    }

    /// <summary>Мс → строка «M:SS» / «M:SS.ff» (обратно к формату источника). null/0 → пусто.</summary>
    private static string? FormatMs(int? ms)
    {
        if (ms is null or <= 0) return null;
        var totalSec = ms.Value / 1000.0;
        var min = (int)(totalSec / 60);
        var sec = totalSec - min * 60;
        // сотые показываем только если они есть
        return sec % 1 == 0
            ? $"{min}:{(int)sec:00}"
            : $"{min}:{sec:00.00}";
    }
}
