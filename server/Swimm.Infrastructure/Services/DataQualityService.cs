using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Read-only выборки «здоровье данных» (T3b, docs/tasks/dashboard-deeplinks-lists-sonnet.md) —
/// предикаты скопированы из <see cref="DashboardStatusService"/> (счётчики на дашборде), чтобы
/// число там и список тут совпадали. Все выборки капнуты топ-200 по Id, с общим Total.
/// </summary>
public class DataQualityService(SwimmDbContext db) : IDataQualityService
{
    private const string SyntheticOrgIdPrefix = "SYNTH-";
    private const int Cap = CappedListDto<object>.Cap;

    public async Task<CappedListDto<SwimmerQualityRowDto>> GetSwimmerQualityAsync(string filter, CancellationToken ct = default)
    {
        IQueryable<Swimmer> q = filter switch
        {
            "no-org-id" => db.Swimmers.AsNoTracking().Where(s => s.Origin == "isr" && s.SwimmerOrgId == null),
            "no-results" => db.Swimmers.AsNoTracking()
                .Where(s => !(s.SwimmerOrgId != null && s.SwimmerOrgId.StartsWith(SyntheticOrgIdPrefix)))
                .Where(s => !db.Results.Any(r => r.SwimmerId == s.Id))
                .Where(s => !db.RelayMembers.Any(rm => rm.SwimmerId == s.Id)),
            _ => db.Swimmers.AsNoTracking().Where(_ => false)
        };

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(s => s.Id).Take(Cap)
            .Select(s => new SwimmerQualityRowDto(s.Id, s.LastName, s.FirstName, s.BirthYear,
                s.Club != null ? s.Club.Name : null))
            .ToListAsync(ct);

        return new CappedListDto<SwimmerQualityRowDto>(total, items);
    }

    public async Task<CappedListDto<ClubQualityRowDto>> GetClubQualityAsync(string filter, CancellationToken ct = default)
    {
        IQueryable<Club> q = filter switch
        {
            // Склеенные клубы пусты по определению — в «дырах данных» им не место.
            "no-swimmers" => db.Clubs.AsNoTracking()
                .Where(c => !c.IsPseudo && !c.Name.StartsWith("SYNTH") && c.MergedIntoId == null)
                .Where(c => !db.Swimmers.Any(s => s.ClubId == c.Id))
                .Where(c => !db.Results.Any(r => r.ClubId == c.Id)),
            "no-country" => db.Clubs.AsNoTracking()
                .Where(c => c.CountryId == null && !c.IsPseudo && c.MergedIntoId == null),
            _ => db.Clubs.AsNoTracking().Where(_ => false)
        };

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(c => c.Id).Take(Cap)
            .Select(c => new ClubQualityRowDto(c.Id, c.Name, c.NameEn, c.CountryId))
            .ToListAsync(ct);

        return new CappedListDto<ClubQualityRowDto>(total, items);
    }

    public async Task<ResultAnomaliesDto> GetResultAnomaliesAsync(CancellationToken ct = default)
    {
        var swimmerIds = db.Swimmers.AsNoTracking().Select(s => s.Id);
        var clubIds = db.Clubs.AsNoTracking().Select(c => c.Id);
        var fkQuery = db.Results.AsNoTracking()
            .Where(r => !swimmerIds.Contains(r.SwimmerId) || !clubIds.Contains(r.ClubId));

        var fkTotal = await fkQuery.CountAsync(ct);
        var fkItems = await fkQuery.OrderBy(r => r.Id).Take(Cap)
            .Select(r => new ResultFkAnomalyRowDto(r.Id, r.SwimmerId, r.ClubId, r.CompetitionId))
            .ToListAsync(ct);

        var relayQuery = db.Relays.AsNoTracking()
            .Where(r => !db.RelayMembers.Any(rm => rm.RelayId == r.Id));

        var relayTotal = await relayQuery.CountAsync(ct);
        var relayItems = await relayQuery.OrderBy(r => r.Id).Take(Cap)
            .Select(r => new EmptyRelayRowDto(
                r.Id,
                db.Results.Where(res => res.RelayId == r.Id).Select(res => res.CompetitionId).FirstOrDefault()))
            .ToListAsync(ct);

        // Без пола: шапка протокола пола не давала (смешанный заплыв), и у пловца его тоже
        // нет. Эстафеты не в счёт — там пол команды и так не всегда определён.
        var noGenderQuery = db.Results.AsNoTracking()
            .Where(r => r.RelayId == null && (r.Gender == "" || r.Gender == "none"));

        var noGenderTotal = await noGenderQuery.CountAsync(ct);
        var noGenderItems = await noGenderQuery.OrderBy(r => r.Id).Take(Cap)
            .Select(r => new ResultNoGenderRowDto(
                r.Id, r.SwimmerId,
                r.Swimmer != null ? (r.Swimmer.LastName + " " + r.Swimmer.FirstName).Trim() : "",
                r.CompetitionId,
                r.Competition != null ? r.Competition.Name : "",
                r.Distance,
                r.Style != null ? r.Style.Name : ""))
            .ToListAsync(ct);

        return new ResultAnomaliesDto(
            new CappedListDto<ResultFkAnomalyRowDto>(fkTotal, fkItems),
            new CappedListDto<EmptyRelayRowDto>(relayTotal, relayItems),
            new CappedListDto<ResultNoGenderRowDto>(noGenderTotal, noGenderItems));
    }

    public async Task<CappedListDto<ModerationPendingRowDto>> GetModerationPendingAsync(CancellationToken ct = default)
    {
        var q = db.UserMediaPublications.AsNoTracking()
            .Where(p => p.Status == UserMediaPublicationStatus.Pending);

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(p => p.Id).Take(Cap)
            .Select(p => new ModerationPendingRowDto(
                p.Id, p.Media!.Url, p.Media.MediaType, p.HubGroup!.Name,
                p.Media.User != null ? p.Media.User.Email : null, p.CreatedAt))
            .ToListAsync(ct);

        return new CappedListDto<ModerationPendingRowDto>(total, items);
    }

    public async Task<CappedListDto<HubGroupJoinRequestRowDto>> GetPendingJoinRequestsAsync(CancellationToken ct = default)
    {
        var q = db.HubGroupUserMembers.AsNoTracking()
            .Where(m => m.Status == HubGroupUserMemberStatus.Pending);

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(m => m.Id).Take(Cap)
            .Select(m => new HubGroupJoinRequestRowDto(
                m.Id, m.HubGroupId, m.HubGroup!.Name, m.User != null ? m.User.Email : null, m.JoinedAt))
            .ToListAsync(ct);

        return new CappedListDto<HubGroupJoinRequestRowDto>(total, items);
    }
}
