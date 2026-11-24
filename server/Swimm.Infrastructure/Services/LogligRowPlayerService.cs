using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="ILogligRowPlayerService"/>
public sealed class LogligRowPlayerService(
    SwimmDbContext db,
    ICompetitionDiscoveryProvider provider,
    IAdminAuditService audit,
    ILogger<LogligRowPlayerService> logger) : ILogligRowPlayerService
{
    public async Task<LogligRowPlayerReport> StampAsync(int orgCompId, string trigger, CancellationToken ct = default)
    {
        LogligRowPlayerReport report;
        int? headDay = null;
        try
        {
            (report, headDay) = await RunAsync(orgCompId, ct);
        }
        catch (Exception ex)
        {
            // Сорвавшийся посреди записи прогон не должен оставить трекеру недописанное —
            // следующим SaveChanges (аудит) оно ушло бы в базу частично.
            db.ChangeTracker.Clear();
            logger.LogWarning(ex, "Игроки loglig по строкам: прогон compID {OrgCompId} сорвался", orgCompId);
            report = new LogligRowPlayerReport(false, SplitAutoAttachService.FailureText(ex, ct));
            if (ct.IsCancellationRequested && ex is OperationCanceledException)
            {
                await RecordAsync(orgCompId, headDay, trigger, report);
                throw;
            }
        }

        await RecordAsync(orgCompId, headDay, trigger, report);
        return report;
    }

    private async Task<(LogligRowPlayerReport Report, int? HeadDay)> RunAsync(int orgCompId, CancellationToken ct)
    {
        var logligId = await db.DiscoveredCompetitions.AsNoTracking()
            .Where(d => d.OrgCompId == orgCompId).Select(d => d.LogligId).FirstOrDefaultAsync(ct);
        if (logligId is not int competitionLogligId)
            return (new(false, "у соревнования нет loglig-id — страниц дисциплин взять неоткуда"), null);

        // Дни этого compID. У окружных чемпионатов дни одного события — разные compID и разные
        // турниры loglig: чужие дни не берём, иначе их строки спорили бы за ключ без даты.
        var days = (await CompetitionIdentity.ResolveDaysAsync(db, orgCompId, ct))
            .Where(d => d.OrgCompId is null || d.OrgCompId == orgCompId)
            .ToList();
        if (days.Count == 0)
            return (new(false, $"дней с compID {orgCompId} в базе нет — сверять не с чем"), null);
        var dayIds = days.Select(d => d.Id).ToList();
        var headDay = dayIds.Min();
        var singleDayDate = days.Count == 1 ? days[0].Date : "";

        var eventIds = await provider.FetchEventIdsAsync(competitionLogligId, ct);
        var requests = 1;
        var source = new List<RowPlayerFromSource>();
        var players = new Dictionary<int, (string Name, int? Year)>();
        var unreadable = new List<int>();
        int events = 0, sourceRows = 0, noLink = 0;

        foreach (var eventId in eventIds)
        {
            ct.ThrowIfCancellationRequested();
            LogligEventResultsDto ev;
            try
            {
                requests++;
                ev = await provider.FetchEventResultsAsync(eventId, ct);
                // Печатная версия без ссылок на игроков (так было раньше) — берём полную страницу.
                if (!ev.IsRelay && ev.Rows.Count > 0 && ev.Rows.All(r => r.PlayerId is null))
                {
                    requests++;
                    ev = await provider.FetchEventResultsFullPageAsync(eventId, ct);
                }
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("loglig дисциплина {EventId} не прочитана: {Error}", eventId, ex.Message);
                unreadable.Add(eventId);
                continue;
            }
            if (ev.IsRelay) continue;

            events++;
            var date = ev.Date.Length > 0 ? ev.Date : singleDayDate;
            foreach (var r in ev.Rows)
            {
                sourceRows++;
                if (r.PlayerId is not int playerId) { noLink++; continue; }
                players[playerId] = (r.FullName, r.BirthYear);
                // «00:00.00» страница печатает, когда времени нет.
                int? timeMs = SwimTime.ParseToMs(r.Time) is int ms and > 0 ? ms : null;
                source.Add(new RowPlayerFromSource(
                    date, ev.StyleName, ev.Distance, LogligPlaceMatcher.SectionGender(r.Category, ev.Gender),
                    r.Heat, r.Lane, timeMs, playerId));
            }
        }

        var rows = await db.Results.AsNoTracking()
            .Where(r => dayIds.Contains(r.CompetitionId) && r.RelayId == null)
            .Select(r => new
            {
                r.Id, r.SwimmerId, r.CompetitionDate, Style = r.Style!.Name, r.Distance, r.Gender,
                r.Heat, r.Lane, r.TimeMillisecond, r.TimeFail, r.LogligPlayerId,
            })
            .ToListAsync(ct);

        var plan = LogligRowPlayerMatcher.Build(source, rows.Select(r => new RowPlayerInDb(
                r.Id, r.CompetitionDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                r.Style, r.Distance, r.Gender, r.Heat, r.Lane,
                r.TimeFail || r.TimeMillisecond is not > 0 ? null : r.TimeMillisecond))
            .ToList());

        // Пишем только доказанные пары. Строку, которой пары сейчас нет, не обнуляем: прежний id
        // записан по доказательству, а сегодня страница могла просто не прочитаться.
        var toWrite = rows.Where(r => plan.Matches.TryGetValue(r.Id, out var p) && r.LogligPlayerId != p)
            .Select(r => r.Id).ToList();
        foreach (var chunk in toWrite.Chunk(500))
        {
            var tracked = await db.Results.Where(r => chunk.Contains(r.Id)).ToListAsync(ct);
            foreach (var r in tracked) r.LogligPlayerId = plan.Matches[r.Id];
        }
        await UpsertPlayersAsync(players, ct);
        await db.SaveChangesAsync(ct);

        var twoPlayerCards = rows
            .GroupBy(r => r.SwimmerId)
            .Count(g => g.Select(r => plan.Matches.TryGetValue(r.Id, out var p) ? p : r.LogligPlayerId)
                .Where(p => p is not null).Distinct().Count() > 1);

        var message =
            $"дисциплин {events}, строк на loglig {sourceRows}, у нас {rows.Count}: игрок записан {toWrite.Count}, " +
            $"уже стоял {plan.Matches.Count - toWrite.Count}" +
            (plan.Ambiguous > 0 ? $", неоднозначных {plan.Ambiguous}" : "") +
            (plan.SourceUnmatched > 0 ? $", без пары {plan.SourceUnmatched}" : "") +
            (noLink > 0 ? $", без ссылки на игрока {noLink}" : "") +
            (unreadable.Count > 0 ? $", не прочитано дисциплин {unreadable.Count}" : "") +
            (twoPlayerCards > 0 ? $". ⚠ Карточек с двумя игроками loglig: {twoPlayerCards} — см. /Admin/Health" : "");

        return (new LogligRowPlayerReport(true, message, events, requests, sourceRows, noLink, rows.Count,
            plan.Matches.Count, toWrite.Count, plan.Ambiguous, plan.SourceUnmatched, twoPlayerCards, unreadable), headDay);
    }

    /// <summary>Имена игроков — для находки Health: последнее увиденное написание.</summary>
    private async Task UpsertPlayersAsync(Dictionary<int, (string Name, int? Year)> players, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        foreach (var chunk in players.Keys.Chunk(500))
        {
            var known = await db.LogligPlayers.Where(p => chunk.Contains(p.LogligId))
                .ToDictionaryAsync(p => p.LogligId, ct);
            foreach (var id in chunk)
            {
                var (name, year) = players[id];
                if (name.Length > 200) name = name[..200];
                if (!known.TryGetValue(id, out var player))
                    db.LogligPlayers.Add(player = new LogligPlayer { LogligId = id });
                player.FullName = name;
                player.BirthYear = year ?? player.BirthYear;
                player.SeenAt = now;
            }
        }
    }

    /// <summary>Строка аудита прогона — всегда, и при сбое. Без токена: прогон мог кончиться отменой.</summary>
    private Task RecordAsync(int orgCompId, int? headDay, string trigger, LogligRowPlayerReport r) =>
        audit.LogAsync(LogligRowPlayerRules.AuditAction, "Competition", headDay?.ToString(CultureInfo.InvariantCulture),
            $"compID {orgCompId}: игроки loglig по строкам — {(r.Ok ? r.Message : "не удалось: " + r.Message)}",
            new
            {
                OrgCompId = orgCompId, Trigger = trigger, r.Ok, r.Events, r.Requests, r.SourceRows, r.NoLink,
                r.OurRows, r.Matched, r.Written, r.Ambiguous, r.SourceUnmatched, r.TwoPlayerCards, r.Unreadable,
            }, CancellationToken.None);
}
