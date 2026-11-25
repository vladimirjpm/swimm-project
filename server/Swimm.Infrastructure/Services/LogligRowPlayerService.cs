using System.Diagnostics;
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
    public Task<LogligRowPlayerReport> StampAsync(
        int orgCompId, string trigger, CancellationToken ct = default, TimeSpan? readBudget = null) =>
        GuardedAsync(orgCompId, null, () => RunAsync(orgCompId, readBudget, ct), trigger, ct);

    /// <inheritdoc />
    public async Task<LogligRowPlayerReport> StampDayAsync(
        int competitionId, int logligId, string trigger, CancellationToken ct = default, TimeSpan? readBudget = null)
    {
        var day = await db.Competitions.AsNoTracking().Where(c => c.Id == competitionId)
            .Select(c => new { c.Id, c.Date, c.OrgCompId, EventOrg = c.Event != null ? c.Event.OrgCompId : null })
            .FirstOrDefaultAsync(ct);
        var org = day?.OrgCompId ?? day?.EventOrg ?? 0;
        return await GuardedAsync(org, competitionId, async () => day == null
            ? (new LogligRowPlayerReport(false, $"соревнования #{competitionId} нет"), (int?)null)
            : await RunCoreAsync(logligId, [day.Id], day.Date, $"переимпорт дня #{competitionId} (у --stamp-row-players по compID — номер другого дня, loglig {logligId} он не видит)", readBudget, ct),
            trigger, ct);
    }

    private async Task<LogligRowPlayerReport> GuardedAsync(int orgCompId, int? headDayHint,
        Func<Task<(LogligRowPlayerReport Report, int? HeadDay)>> run, string trigger, CancellationToken ct)
    {
        LogligRowPlayerReport report;
        int? headDay = headDayHint;
        try
        {
            (report, headDay) = await run();
        }
        catch (Exception ex)
        {
            // Сорвавшийся посреди записи прогон не должен оставить трекеру недописанное —
            // следующим SaveChanges (аудит) оно ушло бы в базу частично.
            db.ChangeTracker.Clear();
            logger.LogWarning(ex, "Игроки loglig по строкам: прогон compID {OrgCompId} сорвался", orgCompId);
            report = new LogligRowPlayerReport(false, SplitAutoAttachService.FailureText(ex, ct));
            await RecordAsync(orgCompId, headDay, trigger, report);
            if (ex is OperationCanceledException && ct.IsCancellationRequested) throw;
            return report;
        }

        await RecordAsync(orgCompId, headDay, trigger, report);
        return report;
    }

    private async Task<(LogligRowPlayerReport Report, int? HeadDay)> RunAsync(
        int orgCompId, TimeSpan? readBudget, CancellationToken ct)
    {
        var logligId = await db.DiscoveredCompetitions.AsNoTracking()
            .Where(d => d.OrgCompId == orgCompId).Select(d => d.LogligId).FirstOrDefaultAsync(ct);
        if (logligId is not int competitionLogligId)
            return (new(false, "у соревнования нет loglig-id — страниц дисциплин взять неоткуда"), null);

        // Дни этого compID. У окружных чемпионатов дни одного события — разные compID и разные
        // турниры loglig: день с ЧУЖИМ compID не берём, иначе его строки спорили бы за ключ без даты.
        // День БЕЗ compID берём: у обычной многодневки штамп стоит только на одном дне события (И-60).
        var days = (await CompetitionIdentity.ResolveDaysAsync(db, orgCompId, ct))
            .Where(d => d.OrgCompId is null || d.OrgCompId == orgCompId)
            .ToList();
        if (days.Count == 0)
            return (new(false, $"дней с compID {orgCompId} в базе нет — сверять не с чем"), null);
        return await RunCoreAsync(competitionLogligId, days.Select(d => d.Id).ToList(),
            days.Count == 1 ? days[0].Date : "", "--stamp-row-players " + orgCompId, readBudget, ct);
    }

    /// <summary>Прогон по дням <paramref name="dayIds"/> со страницами соревнования loglig <paramref name="competitionLogligId"/>.</summary>
    private async Task<(LogligRowPlayerReport Report, int? HeadDay)> RunCoreAsync(int competitionLogligId,
        List<int> dayIds, string singleDayDate, string retryHint, TimeSpan? readBudget, CancellationToken ct)
    {
        var headDay = dayIds.Min();

        var read = await ReadPagesAsync(competitionLogligId, singleDayDate, readBudget, ct);

        var rows = await db.Results.AsNoTracking()
            .Where(r => dayIds.Contains(r.CompetitionId) && r.RelayId == null)
            .Select(r => new
            {
                r.Id, r.SwimmerId, r.CompetitionDate, Style = r.Style!.Name, r.Distance, r.Gender,
                r.Heat, r.Lane, r.TimeMillisecond, r.TimeFail, r.LogligPlayerId,
            })
            .ToListAsync(ct);

        // Прочитано не всё — строки без времени не сводим: их двойник мог остаться на
        // непрочитанной странице (LogligRowPlayerMatcher.Build, allowUntimed).
        var complete = read.Unreadable.Count == 0 && read.NotReached == 0;
        var plan = LogligRowPlayerMatcher.Build(read.Source, rows.Select(r => new RowPlayerInDb(
                r.Id, r.CompetitionDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                r.Style, r.Distance, r.Gender, r.Heat, r.Lane,
                r.TimeFail || r.TimeMillisecond is not > 0 ? null : r.TimeMillisecond))
            .ToList(), allowUntimed: complete);

        // Пишем только доказанные пары. Строку, которой пары сейчас нет, не обнуляем: прежний id
        // записан по доказательству, а сегодня страница могла просто не прочитаться (устаревший id
        // снимает переимпорт, когда у строки меняется время или статус).
        var toWrite = rows.Where(r => plan.Matches.TryGetValue(r.Id, out var p) && r.LogligPlayerId != p)
            .Select(r => r.Id).ToList();
        foreach (var chunk in toWrite.Chunk(500))
        {
            var tracked = await db.Results.Where(r => chunk.Contains(r.Id)).ToListAsync(ct);
            foreach (var r in tracked) r.LogligPlayerId = plan.Matches[r.Id];
        }
        await db.SaveChangesAsync(ct);
        await SavePlayerNamesAsync(read.Players, ct);

        var twoPlayerCards = rows
            .GroupBy(r => r.SwimmerId)
            .Count(g => g.Select(r => plan.Matches.TryGetValue(r.Id, out var p) ? p : r.LogligPlayerId)
                .Where(p => p is not null).Distinct().Count() > 1);

        var message =
            $"дисциплин {read.Events}, строк на loglig {read.SourceRows}, у нас {rows.Count}: игрок записан {toWrite.Count}, " +
            $"уже стоял {plan.Matches.Count - toWrite.Count}" +
            (plan.Ambiguous > 0 ? $", неоднозначных {plan.Ambiguous}" : "") +
            (plan.SourceUnmatched > 0 ? $", без пары {plan.SourceUnmatched}" : "") +
            (read.NoLink > 0 ? $", без ссылки на игрока {read.NoLink}" : "") +
            (read.Unreadable.Count > 0 ? $", не прочитано дисциплин {read.Unreadable.Count}" : "") +
            (read.NotReached > 0 ? $", не дошли до {read.NotReached} дисциплин (истекло время чтения)" : "") +
            (complete ? "" : " — строки без времени не сопоставлялись, повторите: " + retryHint) +
            $"; {read.Requests} запросов за {read.Elapsed.TotalSeconds:0} с" +
            (twoPlayerCards > 0 ? $". ⚠ Карточек с двумя игроками loglig: {twoPlayerCards} — см. /Admin/Health" : "");

        return (new LogligRowPlayerReport(true, message, read.Events, read.Requests, read.SourceRows, read.NoLink,
            rows.Count, plan.Matches.Count, toWrite.Count, plan.Ambiguous, plan.SourceUnmatched, twoPlayerCards,
            read.Unreadable, read.NotReached), headDay);
    }

    private sealed record PagesRead(
        List<RowPlayerFromSource> Source, Dictionary<int, (string Name, int? Year)> Players,
        int Events, int Requests, int SourceRows, int NoLink, List<int> Unreadable, int NotReached, TimeSpan Elapsed);

    /// <summary>
    /// Читает страницы дисциплин, пока есть время. Сбой или таймаут ОДНОЙ страницы — «дисциплина
    /// не прочитана», прогон идёт дальше; отмена нашего токена — наружу.
    /// </summary>
    private async Task<PagesRead> ReadPagesAsync(
        int competitionLogligId, string singleDayDate, TimeSpan? readBudget, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var eventIds = await provider.FetchEventIdsAsync(competitionLogligId, ct);
        var requests = 1;
        var source = new List<RowPlayerFromSource>();
        var players = new Dictionary<int, (string Name, int? Year)>();
        var unreadable = new List<int>();
        int events = 0, sourceRows = 0, noLink = 0, notReached = 0;
        // Полная страница — запасной источник ссылок. Если и она в первый раз вернула ноль
        // ссылок, вёрстка сменилась у обеих — больше в этом прогоне её не дёргаем.
        var fullPageHelps = true;

        for (var i = 0; i < eventIds.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (readBudget is { } budget && clock.Elapsed > budget)
            {
                notReached = eventIds.Count - i;
                break;
            }

            var eventId = eventIds[i];
            LogligEventResultsDto ev;
            try
            {
                requests++;
                ev = await provider.FetchEventResultsAsync(eventId, ct);
                if (fullPageHelps && !ev.IsRelay && ev.Rows.Count > 0 && ev.Rows.All(r => r.PlayerId is null))
                {
                    requests++;
                    var full = await provider.FetchEventResultsFullPageAsync(eventId, ct);
                    if (full.Rows.Any(r => r.PlayerId is not null)) ev = full;
                    else fullPageHelps = false;
                }
            }
            // Таймаут HttpClient — TaskCanceledException при НЕотменённом нашем токене.
            catch (Exception ex) when (ex is HttpRequestException or TimeoutException
                                       || (ex is OperationCanceledException && !ct.IsCancellationRequested))
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

        return new PagesRead(source, players, events, requests, sourceRows, noLink, unreadable, notReached, clock.Elapsed);
    }

    /// <summary>
    /// Имена игроков — для находки Health: последнее увиденное написание. ОТДЕЛЬНЫМ сохранением,
    /// после id строк: конфликт ключа при параллельном прогоне (CLI рядом с шагом после импорта
    /// вставил того же игрока) не должен откатывать запись в Results. Не сохранилось — не беда:
    /// находка покажет id без имени, имя допишет следующий прогон.
    /// </summary>
    private async Task SavePlayerNamesAsync(Dictionary<int, (string Name, int? Year)> players, CancellationToken ct)
    {
        try
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
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            logger.LogWarning(ex, "Игроки loglig: имена не сохранены (параллельный прогон?) — id строк записаны");
        }
    }

    /// <summary>Строка аудита прогона — всегда, и при сбое. Без токена: прогон мог кончиться отменой.</summary>
    private Task RecordAsync(int orgCompId, int? headDay, string trigger, LogligRowPlayerReport r) =>
        audit.LogAsync(LogligRowPlayerRules.AuditAction, "Competition", headDay?.ToString(CultureInfo.InvariantCulture),
            $"compID {orgCompId}: игроки loglig по строкам — {(r.Ok ? r.Message : "не удалось: " + r.Message)}",
            new
            {
                OrgCompId = orgCompId, Trigger = trigger, r.Ok, r.Incomplete, r.Events, r.Requests, r.SourceRows, r.NoLink,
                r.OurRows, r.Matched, r.Written, r.Ambiguous, r.SourceUnmatched, r.TwoPlayerCards, r.Unreadable, r.NotReached,
            }, CancellationToken.None);
}
