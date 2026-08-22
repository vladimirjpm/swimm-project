using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Сводка «Здоровье данных» для дашборда /Admin (docs/plans/admin-dashboard-health-2-plan.md):
/// пловцы/клубы/соревнования/результаты/рекорды/медиа/юзеры-группы/система одним запросом.
/// Весь результат кэшируется целиком в IMemoryCache на 2 минуты (ключ "dashboard:status") —
/// инвалидация не нужна, свежесть 2 минуты достаточна для сводных карточек.
/// </summary>
public class DashboardStatusService(
    SwimmDbContext db,
    ISwimmerDedupService swimmerDedup,
    IClubDedupService clubDedup,
    IRecordQualityService recordQuality,
    IDataCheckRunner dataChecks,
    IMemoryCache cache) : IDashboardStatusService
{
    private const string CacheKey = "dashboard:status";

    /// <summary>
    /// Метрики, у которых в реестре проверок (Д3) есть проверка один-в-один. Дашборд берёт
    /// число ОТТУДА, а не считает второй раз: две страницы, считающие одно и то же разным
    /// кодом, рано или поздно разойдутся, и тогда непонятно, какой верить.
    ///
    /// Цена решения: это картина последнего прогона, а не «прямо сейчас» — поэтому дашборд
    /// показывает рядом время прогона. Если проверка ещё ни разу не гонялась, метрика
    /// считается по-старому вживую (иначе новая база показывала бы обнадёживающие нули).
    /// </summary>
    private Dictionary<string, DataCheckStateDto> _states = [];

    private async Task<int> FromRegistryAsync(string checkId, Func<Task<int>> live) =>
        _states.TryGetValue(checkId, out var s) && !s.Failed ? s.Open : await live();

    private int FromRegistry(string checkId, Func<int> live) =>
        _states.TryGetValue(checkId, out var s) && !s.Failed ? s.Open : live();

    /// <summary>Префикс синтетических SwimmerOrgId, проставляемых при импорте без реального ID
    /// федерации (см. SwimmerDedupService/dedup-report.sql) — исключаются из «живых» счётчиков.</summary>
    private const string SyntheticOrgIdPrefix = "SYNTH-";

    public async Task<DashboardStatusSummary> GetStatusAsync(CancellationToken ct = default)
    {
        var cached = await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            return await BuildAsync(ct);
        });

        return cached!;
    }

    private async Task<DashboardStatusSummary> BuildAsync(CancellationToken ct)
    {
        // Реестр читается ПЕРВЫМ: его числа подставляются в блоки ниже.
        var (lastRun, states) = await dataChecks.GetStateAsync(ct);
        _states = states.ToDictionary(s => s.CheckId);
        var checks = BuildChecks(lastRun, states);

        var swimmers = await BuildSwimmersAsync(ct);
        var clubs = await BuildClubsAsync(ct);
        var competitions = await BuildCompetitionsAsync(ct);
        var results = await BuildResultsAsync(ct);
        var recordSets = await BuildRecordSetsAsync(ct);
        // Качество рекордов: сверка с протоколами + реестр спорных записей
        // (docs/plans/records-quality-plan.md).
        var recordQualitySummary = await recordQuality.GetSummaryAsync(ct: ct);
        var media = await BuildMediaAsync(ct);
        var usersGroups = await BuildUsersGroupsAsync(ct);
        var system = await BuildSystemAsync(ct);
        var clubPoints = await BuildClubPointsAsync(ct);

        return new DashboardStatusSummary(
            swimmers, clubs, competitions, results, recordSets, recordQualitySummary,
            media, usersGroups, system, checks, clubPoints);
    }

    /// <summary>
    /// Блок «Клубные очки»: итоги ручной сверки зачёта с официальным (docs/admin-pages/pointsrules.md).
    ///
    /// Считаем по СОБЫТИЯМ: отметка ставится всем дням многодневки, и по строкам-дням
    /// один чемпионат выглядел бы четырьмя расхождениями. Ключ события — <c>EventId</c>,
    /// а у однодневки его нет, поэтому она сама себе событие (тот же приём, что в
    /// <c>PointRulesAdminRepository</c> и <c>CompetitionRecalculationService</c>).
    /// </summary>
    private async Task<DashboardClubPointsStatus> BuildClubPointsAsync(CancellationToken ct)
    {
        var days = await db.Competitions.AsNoTracking()
            .Select(c => new
            {
                c.Id, c.EventId, c.Name, c.Date,
                c.PointRuleClubsId, Kind = c.ClubPointsVerifiedKind
            })
            .ToListAsync(ct);

        // Событие: EventId, а у однодневки — её собственный Id с минусом, чтобы ключи не
        // столкнулись. Представитель события — день с наименьшим Id, как везде.
        var events = days
            .GroupBy(d => d.EventId ?? -d.Id)
            .Select(g => new
            {
                Head = g.OrderBy(d => d.Id).First(),
                Kind = g.Select(d => d.Kind).FirstOrDefault(k => k != null),
                HasRule = g.Any(d => d.PointRuleClubsId != null)
            })
            .ToList();

        var mismatchEvents = events.Where(e => e.Kind == PointsVerifiedKinds.Mismatch).ToList();
        var mismatchIds = mismatchEvents.Select(e => e.Head.Id).ToList();

        // Дни каждого события с расхождением — очки и эталон суммируются по ним всем.
        var dayIdsByEvent = mismatchEvents.ToDictionary(
            e => e.Head.Id,
            e => days.Where(d => (d.EventId ?? -d.Id) == (e.Head.EventId ?? -e.Head.Id))
                     .Select(d => d.Id).ToList());
        var allDayIds = dayIdsByEvent.Values.SelectMany(x => x).Distinct().ToList();

        var pointsByDay = await db.ClubCompetitionStandings.AsNoTracking()
            .Where(s => allDayIds.Contains(s.CompetitionId))
            .GroupBy(s => s.CompetitionId)
            .Select(g => new { CompetitionId = g.Key, Points = g.Sum(x => x.Points) })
            .ToDictionaryAsync(x => x.CompetitionId, x => x.Points, ct);

        // Эталон официальных очков есть только у соревнований из пособытийного источника.
        var officialByDay = await db.Results.AsNoTracking()
            .Where(r => allDayIds.Contains(r.CompetitionId) && r.OfficialClubPoints != null)
            .GroupBy(r => r.CompetitionId)
            .Select(g => new { CompetitionId = g.Key, Official = g.Sum(x => x.OfficialClubPoints!.Value) })
            .ToDictionaryAsync(x => x.CompetitionId, x => x.Official, ct);

        var withNote = await db.CompetitionNotes.AsNoTracking()
            .Where(n => allDayIds.Contains(n.CompetitionId)
                     && n.Kind == CompetitionNoteKinds.ClubPointsMismatch)
            .Select(n => n.CompetitionId)
            .ToListAsync(ct);

        var mismatchedRows = await MismatchedRowsAsync(allDayIds, ct);

        var lines = mismatchEvents
            .Select(e =>
            {
                var ids = dayIdsByEvent[e.Head.Id];
                var official = ids.Where(officialByDay.ContainsKey).Sum(i => officialByDay[i]);
                var rows = ids.Sum(i => mismatchedRows.GetValueOrDefault(i));
                var hasOfficial = ids.Any(officialByDay.ContainsKey);

                return new DashboardClubPointsLine(
                    e.Head.Id, e.Head.Name, e.Head.Date,
                    OurPoints: ids.Sum(i => pointsByDay.GetValueOrDefault(i)),
                    OfficialPoints: hasOfficial ? official : null,
                    MismatchedRows: hasOfficial ? rows : null,
                    HasNote: ids.Any(withNote.Contains));
            })
            .OrderByDescending(l => l.MismatchedRows ?? 0)
            .ThenBy(l => l.CompetitionId)
            .ToList();

        return new DashboardClubPointsStatus(
            MismatchEvents: mismatchEvents.Count,
            MismatchWithoutNote: lines.Count(l => !l.HasNote),
            VerifiedEvents: events.Count(e => e.Kind == PointsVerifiedKinds.Official),
            AcceptedEvents: events.Count(e => e.Kind == PointsVerifiedKinds.Accepted),
            UncheckedEvents: events.Count(e => e.Kind is null && e.HasRule),
            NoRuleEvents: events.Count(e => !e.HasRule),
            Mismatches: lines);
    }

    /// <summary>
    /// Сколько строк каждого дня расходится с эталоном. Считается тем же движком, что и
    /// витрина, — иначе дашборд обещал бы одно, а проверка реестра показывала другое
    /// (правило то же, что в <c>OfficialClubPointsMismatchCheck</c>).
    /// </summary>
    private async Task<Dictionary<int, int>> MismatchedRowsAsync(
        IReadOnlyList<int> dayIds, CancellationToken ct)
    {
        var rows = await db.Results.AsNoTracking()
            .Where(r => dayIds.Contains(r.CompetitionId) && r.OfficialClubPoints != null)
            .Select(r => new
            {
                r.CompetitionId,
                r.Competition!.Date,
                r.Competition.IsMasters,
                RuleId = r.Competition.PointRuleClubsId,
                r.Position, r.HeatType, r.Round, r.TimeFail,
                IsRelay = r.RelayId != null,
                Official = r.OfficialClubPoints!.Value
            })
            .ToListAsync(ct);

        if (rows.Count == 0) return [];

        var rules = await db.PointRulesClubs.AsNoTracking().Include(r => r.Entries).ToListAsync(ct);
        var result = new Dictionary<int, int>();

        foreach (var group in rows.GroupBy(r => r.CompetitionId))
        {
            var head = group.First();
            var rule = CompetitionRuleResolver.Resolve(
                rules, head.RuleId, head.IsMasters,
                DateOnly.TryParseExact(head.Date, "dd/MM/yyyy",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var d) ? d : DateOnly.MinValue);

            result[group.Key] = group.Count(r =>
                PointRulesClubsScoring.RelayPointsFor(
                    rule,
                    r.HeatType == "prelim" || r.HeatType == "extra" || r.Round == ResultRounds.FinalOpen
                        ? null : r.Position,
                    r.TimeFail, r.IsRelay) != r.Official);
        }

        return result;
    }

    /// <summary>
    /// Сводка реестра проверок. Считается ПО СОСТОЯНИЯМ проверок, а не по счётчикам прогона:
    /// в прогоне лежат числа записанных находок (список капнут на 50), а здесь нужны полные.
    /// </summary>
    private static DashboardChecksStatus BuildChecks(
        DataCheckRunDto? lastRun, IReadOnlyList<DataCheckStateDto> states)
    {
        // Прогоны в истории есть, а состояний нет — значит все они прошли ДО появления
        // Sys_DataCheckStates (миграция AddDataCheckStates). Показать их дату с нулями значило
        // бы соврать «всё чисто»: считать нам пока просто нечего.
        if (states.Count == 0) lastRun = null;

        // Везде Open, а не Total: принятые как есть — это закрытое решение, а не работа.
        // Иначе дашборд показывал бы 58 предупреждений там, где /Admin/Health говорит 16.
        int Sum(DataCheckSeverity s) => states
            .Where(x => x.Severity == s && !x.Failed)
            .Sum(x => x.Open);

        var lines = states
            .Where(s => s.Open > 0 || s.Failed)
            .OrderByDescending(s => s.Failed)
            .ThenByDescending(s => s.Severity)
            .ThenByDescending(s => s.Open)
            .Select(s => new DashboardCheckLine(s.CheckId, s.Severity, s.Open, s.Failed))
            .ToList();

        return new DashboardChecksStatus(
            LastRunAt: lastRun?.StartedAt,
            LastRunTrigger: lastRun?.Trigger,
            Errors: Sum(DataCheckSeverity.Error),
            Warnings: Sum(DataCheckSeverity.Warning),
            Infos: Sum(DataCheckSeverity.Info),
            FailedChecks: states.Count(s => s.Failed),
            Lines: lines);
    }

    private async Task<DashboardSwimmerStatus> BuildSwimmersAsync(CancellationToken ct)
    {
        var swimmerReport = await swimmerDedup.FindCandidatesAsync(ct);

        var total = await db.Swimmers.AsNoTracking().CountAsync(ct);
        var originIsr = await db.Swimmers.AsNoTracking().CountAsync(s => s.Origin == "isr", ct);
        var originLocal = await db.Swimmers.AsNoTracking().CountAsync(s => s.Origin == "local", ct);
        var synthetic = await db.Swimmers.AsNoTracking()
            .CountAsync(s => s.SwimmerOrgId != null && s.SwimmerOrgId.StartsWith(SyntheticOrgIdPrefix), ct);
        var noOrgId = await db.Swimmers.AsNoTracking()
            .CountAsync(s => s.Origin == "isr" && s.SwimmerOrgId == null, ct);

        // Пловец без результатов: нет строк в Results (по SwimmerId) и нет строк в RelayMembers.
        // Синтетика (SYNTH-%) исключена — это заведомо технические записи.
        var noResults = await db.Swimmers.AsNoTracking()
            .Where(s => !(s.SwimmerOrgId != null && s.SwimmerOrgId.StartsWith(SyntheticOrgIdPrefix)))
            .Where(s => !db.Results.Any(r => r.SwimmerId == s.Id))
            .Where(s => !db.RelayMembers.Any(rm => rm.SwimmerId == s.Id))
            .CountAsync(ct);

        var logligCounts = await db.Swimmers.AsNoTracking()
            .GroupBy(s => s.LogligIdStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var loglig = new DashboardLogligStatus(
            Verified: logligCounts.FirstOrDefault(g => g.Status == "Verified")?.Count ?? 0,
            Suggested: logligCounts.FirstOrDefault(g => g.Status == "Suggested")?.Count ?? 0,
            Rejected: logligCounts.FirstOrDefault(g => g.Status == "Rejected")?.Count ?? 0,
            Unlinked: logligCounts.FirstOrDefault(g => g.Status == null)?.Count ?? 0);

        return new DashboardSwimmerStatus(
            Total: total,
            OriginIsr: originIsr,
            OriginLocal: originLocal,
            Synthetic: synthetic,
            // Уверенные дубли и сироты живут в реестре (swimmers.dedup-sure / swimmers.orphans);
            // «неуверенных» проверки нет — они сознательно не заведены как находка, поэтому
            // считаются вживую по тому же отчёту.
            SureCandidates: FromRegistry("swimmers.dedup-sure", () => swimmerReport.Candidates.Count(c => c.Sure)),
            UnsureCandidates: swimmerReport.Candidates.Count(c => !c.Sure),
            Orphans: FromRegistry("swimmers.orphans", () => swimmerReport.Orphans.Count),
            NoOrgId: noOrgId,
            NoResults: noResults,
            Loglig: loglig);
    }

    private async Task<DashboardClubStatus> BuildClubsAsync(CancellationToken ct)
    {
        var clubReport = await clubDedup.FindCandidatesAsync(ct);

        // Склеенные клубы (MergedIntoId) в статистику не входят: строка осталась только
        // ради живых ссылок на старый Id, как клуб она больше не существует.
        var total = await db.Clubs.AsNoTracking().CountAsync(c => c.MergedIntoId == null, ct);
        var pseudo = await db.Clubs.AsNoTracking().CountAsync(c => c.IsPseudo && c.MergedIntoId == null, ct);

        // Клуб без пловцов: нет Swimmer.ClubId == club.Id и нет ResultRecord.ClubId == club.Id.
        // Псевдоклубы и SYNTH-клубы исключены (не «настоящие» клубы, не считаем дырой).
        // Из реестра (clubs.empty), пока он не гонялся — вживую.
        var noSwimmers = await FromRegistryAsync("clubs.empty", () => db.Clubs.AsNoTracking()
            .Where(c => !c.IsPseudo && !c.Name.StartsWith("SYNTH") && c.MergedIntoId == null)
            .Where(c => !db.Swimmers.Any(s => s.ClubId == c.Id))
            .Where(c => !db.Results.Any(r => r.ClubId == c.Id))
            .CountAsync(ct));

        var noCountry = await db.Clubs.AsNoTracking()
            .CountAsync(c => c.CountryId == null && !c.IsPseudo && c.MergedIntoId == null, ct);

        var clubRequestsPending = await db.HubGroupClubRequests.AsNoTracking()
            .CountAsync(r => r.Status == HubGroupClubRequestStatus.Pending, ct);

        return new DashboardClubStatus(
            Total: total,
            Pseudo: pseudo,
            SureCandidates: FromRegistry("clubs.dedup-sure", () => clubReport.Candidates.Count(c => c.Sure)),
            UnsureCandidates: clubReport.Candidates.Count(c => !c.Sure),
            NoSwimmers: noSwimmers,
            NoCountry: noCountry,
            ClubRequestsPending: clubRequestsPending);
    }

    private async Task<DashboardCompetitionStatus> BuildCompetitionsAsync(CancellationToken ct)
    {
        var total = await db.Competitions.AsNoTracking().CountAsync(ct);
        var withResults = await db.Competitions.AsNoTracking()
            .CountAsync(c => db.Results.Any(r => r.CompetitionId == c.Id), ct);
        var noOrgCompId = await db.Competitions.AsNoTracking().CountAsync(c => c.OrgCompId == null, ct);

        // Совпадает с /Admin/Discovery: строка считается импортированной, если она матчится с
        // Competitions по имени+дате (см. DiscoveryCompetitionMatcher) ИЛИ помечена вручную через
        // SetStatusAsync — ничего в пайплайне импорта Status=Imported само не выставляет.
        var discoveryRows = await db.DiscoveredCompetitions.AsNoTracking().ToListAsync(ct);
        var matches = await new DiscoveryCompetitionMatcher(db).MatchAsync(discoveryRows, ct);

        var imported = 0;
        var newCount = 0;
        var ignored = 0;
        var errors = 0;
        foreach (var row in discoveryRows)
        {
            var isMatched = matches.GetValueOrDefault(row.Id) is not null;
            if (isMatched || row.Status == DiscoveredCompetitionStatus.Imported)
                imported++;
            else if (row.Status == DiscoveredCompetitionStatus.Ignored)
                ignored++;
            else
                newCount++;

            if (row.LastError != null)
                errors++;
        }

        return new DashboardCompetitionStatus(
            Total: total,
            WithResults: withResults,
            DiscoveryImported: imported,
            DiscoveryNew: newCount,
            DiscoveryIgnored: ignored,
            DiscoveryErrors: errors,
            NoOrgCompId: noOrgCompId,
            DuplicateStandings: await CountDuplicateStandingsAsync(ct));
    }

    /// <summary>
    /// Дубли клубного зачёта: (сезон × зачётная группа × ❄/☀) должен давать НЕ БОЛЬШЕ одного
    /// соревнования — у возрастной группы за сезон один зимний чемпионат и один летний.
    /// Уникальным индексом это не выразить (сезон производен от даты, группа лежит в M:N),
    /// поэтому проверяем данными. Ненулевой счётчик значит, что у какого-то соревнования
    /// неверно стоит IsChampionship или PoolType, — чинить надо там.
    /// Пропуск (чемпионат отменили) — норма и здесь не считается.
    /// </summary>
    private async Task<int> CountDuplicateStandingsAsync(CancellationToken ct)
    {
        var rows = await db.Competitions.AsNoTracking()
            .Where(c => c.IsChampionship || c.StandingKindOverride != null)
            .Select(c => new
            {
                c.Id,
                c.Date,
                c.EventId,
                c.IsChampionship,
                c.PoolType,
                c.StandingKindOverride,
                Categories = db.CategoryCompetitions
                    .Where(cc => cc.CompetitionId == c.Id)
                    .Select(cc => cc.Category.Key)
                    .ToList(),
            })
            .ToListAsync(ct);

        var seen = new HashSet<(int Season, string Kind, string Group)>();
        var duplicates = 0;
        // Зачётная единица — событие целиком, поэтому дни одного события не считаются дублями.
        var countedUnits = new HashSet<string>();

        foreach (var c in rows)
        {
            var kind = StandingKinds.Resolve(c.IsChampionship, c.PoolType, c.StandingKindOverride);
            if (kind is null) continue;
            if (!DateTime.TryParseExact(c.Date, "dd/MM/yyyy",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var date)) continue;

            var season = SeasonMath.StartYearOf(date);
            foreach (var key in c.Categories.Where(Category.ReservedKeys.Contains))
            {
                var unit = $"{c.EventId?.ToString() ?? "c" + c.Id}|{season}|{kind}|{key}";
                if (!countedUnits.Add(unit)) continue;   // другой день того же события
                if (!seen.Add((season, kind, key))) duplicates++;
            }
        }
        return duplicates;
    }

    private async Task<DashboardResultStatus> BuildResultsAsync(CancellationToken ct)
    {
        var total = await db.Results.AsNoTracking().CountAsync(ct);
        var timeFail = await db.Results.AsNoTracking().CountAsync(r => r.TimeFail, ct);

        // FK-аномалии — сторож: в проде БД держит FK, ожидаемо 0. InMemory-провайдер их не
        // проверяет, поэтому в тестах можно завести «битую» строку напрямую.
        // Обе метрики есть в реестре (results.fk-anomaly / relays.empty) — берём оттуда.
        var swimmerIds = db.Swimmers.AsNoTracking().Select(s => s.Id);
        var clubIds = db.Clubs.AsNoTracking().Select(c => c.Id);
        var fkAnomalies = await FromRegistryAsync("results.fk-anomaly", () => db.Results.AsNoTracking()
            .Where(r => !swimmerIds.Contains(r.SwimmerId) || !clubIds.Contains(r.ClubId))
            .CountAsync(ct));

        var emptyRelays = await FromRegistryAsync("relays.empty", () => db.Relays.AsNoTracking()
            .CountAsync(r => !db.RelayMembers.Any(rm => rm.RelayId == r.Id), ct));

        return new DashboardResultStatus(
            Total: total,
            TimeFail: timeFail,
            FkAnomalies: fkAnomalies,
            EmptyRelays: emptyRelays);
    }

    private async Task<IReadOnlyList<DashboardRecordSetStatus>> BuildRecordSetsAsync(CancellationToken ct)
    {
        var groups = await db.Records.AsNoTracking()
            .GroupBy(r => new { r.RegionType, r.RegionCode })
            .Select(g => new DashboardRecordSetStatus(g.Key.RegionType, g.Key.RegionCode, g.Count(), g.Max(r => r.UpdatedAt)))
            .ToListAsync(ct);

        return groups;
    }

    private async Task<DashboardMediaStatus> BuildMediaAsync(CancellationToken ct)
    {
        var total = await db.UserMedia.AsNoTracking().CountAsync(ct);
        var video = await db.UserMedia.AsNoTracking().CountAsync(m => m.MediaType == "video", ct);
        var photo = await db.UserMedia.AsNoTracking().CountAsync(m => m.MediaType == "image", ct);
        var broken = await db.UserMedia.AsNoTracking().CountAsync(m => m.LinkOk == false, ct);
        var unchecked_ = await db.UserMedia.AsNoTracking().CountAsync(m => m.LinkCheckedAt == null, ct);
        var moderationPending = await db.UserMediaPublications.AsNoTracking()
            .CountAsync(p => p.Status == UserMediaPublicationStatus.Pending, ct);

        return new DashboardMediaStatus(
            Total: total,
            Video: video,
            Photo: photo,
            Broken: broken,
            Unchecked: unchecked_,
            ModerationPending: moderationPending);
    }

    private async Task<DashboardUsersGroupsStatus> BuildUsersGroupsAsync(CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-7);

        var usersTotal = await db.AppUsers.AsNoTracking().CountAsync(ct);
        var active7d = await db.AppUsers.AsNoTracking().CountAsync(u => u.LastSeenAt != null && u.LastSeenAt >= since, ct);
        var deactivated = await db.AppUsers.AsNoTracking().CountAsync(u => !u.IsActive, ct);

        var groupsTotal = await db.HubGroups.AsNoTracking().CountAsync(ct);
        var groupsOfficial = await db.HubGroups.AsNoTracking().CountAsync(g => g.IsOfficial, ct);

        var joinRequestsPending = await db.HubGroupUserMembers.AsNoTracking()
            .CountAsync(m => m.Status == HubGroupUserMemberStatus.Pending, ct);

        return new DashboardUsersGroupsStatus(
            UsersTotal: usersTotal,
            Active7d: active7d,
            Deactivated: deactivated,
            GroupsTotal: groupsTotal,
            GroupsOfficial: groupsOfficial,
            JoinRequestsPending: joinRequestsPending);
    }

    private async Task<DashboardSystemStatus> BuildSystemAsync(CancellationToken ct)
    {
        var lastImport = await db.ImportHistory.AsNoTracking()
            .OrderByDescending(h => h.ImportDate)
            .FirstOrDefaultAsync(ct);

        var lastMediaCheckAt = await db.UserMedia.AsNoTracking()
            .Where(m => m.LinkCheckedAt != null)
            .OrderByDescending(m => m.LinkCheckedAt)
            .Select(m => (DateTime?)m.LinkCheckedAt)
            .FirstOrDefaultAsync(ct);

        var lastDiscoverySeenAt = await db.DiscoveredCompetitions.AsNoTracking()
            .OrderByDescending(d => d.LastSeenAt)
            .Select(d => (DateTime?)d.LastSeenAt)
            .FirstOrDefaultAsync(ct);

        var since = DateTime.UtcNow.AddDays(-7);
        var auditActions7d = await db.AdminAudits.AsNoTracking().CountAsync(a => a.CreatedAt >= since, ct);

        return new DashboardSystemStatus(
            LastImportAt: lastImport?.ImportDate,
            LastImportApproved: lastImport?.Approved,
            LastMediaCheckAt: lastMediaCheckAt,
            LastDiscoverySeenAt: lastDiscoverySeenAt,
            AuditActions7d: auditActions7d);
    }
}
