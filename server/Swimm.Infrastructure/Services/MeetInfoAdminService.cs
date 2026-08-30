using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Редактор справки о старте (шаг Т1). Пишет <see cref="CompetitionMeetInfo"/> и
/// <see cref="CompetitionWarmUp"/> — обе таблицы публичные, их читает таб Start list.
///
/// Ручное и автоматическое разведено по разным полям СОЗНАТЕЛЬНО: забор стартового протокола
/// идемпотентен и запускается до последнего дня, поэтому он пишет только
/// <c>IsChampionship</c>, а здесь правится <c>IsChampionshipOverride</c>. Иначе решение
/// админа жило бы до следующего прогона забора.
/// </summary>
public class MeetInfoAdminService : IMeetInfoAdminService
{
    private readonly SwimmDbContext _db;

    public MeetInfoAdminService(SwimmDbContext db)
    {
        _db = db;
    }

    public async Task<MeetInfoAdminDto?> GetAsync(int orgCompId, CancellationToken ct = default)
    {
        var info = await _db.CompetitionMeetInfos
            .Include(m => m.WarmUps)
            .FirstOrDefaultAsync(m => m.OrgCompId == orgCompId, ct);

        var (name, days) = await ResolveDaysAsync(orgCompId, ct);
        if (name is null) return null;

        return Build(orgCompId, name, info, days);
    }

    public async Task<MeetInfoAdminDto?> SaveAsync(
        int orgCompId, MeetInfoSaveRequest request, CancellationToken ct = default)
    {
        var (name, days) = await ResolveDaysAsync(orgCompId, ct);
        if (name is null) return null;

        var info = await _db.CompetitionMeetInfos
            .Include(m => m.WarmUps)
            .FirstOrDefaultAsync(m => m.OrgCompId == orgCompId, ct);

        if (info is null)
        {
            info = new CompetitionMeetInfo { OrgCompId = orgCompId };
            _db.CompetitionMeetInfos.Add(info);
        }

        info.IsChampionshipOverride = request.IsChampionshipOverride;
        info.UpdatedAt = DateTime.UtcNow;

        foreach (var day in request.Days)
        {
            // Календарный день, как CompDate у заявки: колонка `timestamp without time zone`,
            // и Kind=Utc Npgsql на ней не примет.
            var date = DateTime.SpecifyKind(day.Date.Date, DateTimeKind.Unspecified);
            var existing = info.WarmUps.FirstOrDefault(w => w.Date == date);
            var at = ParseWarmUp(date, day.WarmUpLocal);

            if (at is null)
            {
                // Пустое поле — «стереть»: иначе однажды введённое время нечем убрать.
                if (existing is not null) _db.CompetitionWarmUps.Remove(existing);
                continue;
            }

            if (existing is null)
                info.WarmUps.Add(new CompetitionWarmUp { OrgCompId = orgCompId, Date = date, WarmUpAt = at.Value });
            else
                existing.WarmUpAt = at.Value;
        }

        await _db.SaveChangesAsync(ct);

        return Build(orgCompId, name, info, days);
    }

    /// <summary>
    /// Имя соревнования и его дни. Сперва заявки (они и есть программа), затем «Входящие» —
    /// у будущего старта заявок может ещё не быть, а разминку админ вводит заранее, читая
    /// регламент. Ни там, ни там — соревнование нам неизвестно.
    /// </summary>
    private async Task<(string? Name, IReadOnlyList<(DateTime Date, int Entries)> Days)> ResolveDaysAsync(
        int orgCompId, CancellationToken ct)
    {
        var entries = await _db.CompetitionEntries.AsNoTracking()
            .Where(e => e.OrgCompId == orgCompId)
            .Select(e => new { e.CompDate, e.CompName })
            .ToListAsync(ct);

        if (entries.Count > 0)
        {
            var days = entries
                .GroupBy(e => e.CompDate.Date)
                .OrderBy(g => g.Key)
                .Select(g => (Date: g.Key, Entries: g.Count()))
                .ToList();
            return (entries[0].CompName, days);
        }

        var discovered = await _db.DiscoveredCompetitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.OrgCompId == orgCompId, ct);
        if (discovered is null) return (null, []);

        var from = discovered.DateStart.Date;
        var to = discovered.DateEnd.Date < from ? from : discovered.DateEnd.Date;
        var range = new List<(DateTime, int)>();
        for (var d = from; d <= to; d = d.AddDays(1)) range.Add((d, 0));
        return (discovered.Name, range);
    }

    private static MeetInfoAdminDto Build(
        int orgCompId, string name, CompetitionMeetInfo? info,
        IReadOnlyList<(DateTime Date, int Entries)> days)
    {
        var warmUps = info?.WarmUps.ToDictionary(w => w.Date.Date, w => w.WarmUpAt)
            ?? new Dictionary<DateTime, DateTime>();

        return new MeetInfoAdminDto(
            orgCompId,
            name,
            info?.IsChampionship ?? false,
            info?.IsChampionshipOverride,
            info?.ChampionshipEffective ?? false,
            info?.RegulationUrl,
            info?.RegulationCheckedAt,
            info?.UpdatedAt,
            days.Select(d => new MeetInfoDayDto(
                d.Date,
                warmUps.TryGetValue(d.Date, out var at) ? IsraelTime.ToLocal(at).ToString("HH:mm") : null,
                d.Entries)).ToList());
    }

    /// <summary>«HH:mm» местного времени + день → момент UTC. Пусто или мусор — null.</summary>
    private static DateTime? ParseWarmUp(DateTime date, string? local)
    {
        if (string.IsNullOrWhiteSpace(local)) return null;
        if (!TimeOnly.TryParse(local.Trim(), out var time)) return null;
        return IsraelTime.ToUtc(date.Date + time.ToTimeSpan());
    }
}
