using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Забор стартового протокола соревнования (docs/plans/start-list-plan.md, шаг С4).
///
/// Порядок: программа дня (<c>AthleticsDisciplines</c>) → стартовый протокол каждого заплыва
/// (<c>StartList/{disciplineId}</c>) → заявки в <c>CompetitionEntries</c>.
///
/// Три решения, которые нельзя переизобрести:
///
/// 1. <b>Справочник <c>Competitions</c> не трогаем.</b> У предстоящего старта строки там нет,
///    и заводить её заранее нельзя: <c>BulkPullService</c> отбирает к затягиванию по
///    <c>MatchedCompetitionId is null</c>, и проштампованное соревнование выпало бы из пачки
///    навсегда. Идентичность заявки — <c>OrgCompId</c> (И7), имя и дата денормализованы.
///
/// 2. <b>Пловец матчится по <c>LogligId</c>, а не по имени.</b> В стартовом протоколе имена
///    только на иврите, зато у каждой строки стоит ссылка на карточку. Имя — фоллбек через
///    <see cref="LogligSwimmerNameResolver"/>, тот же, что у пособытийного импорта.
///
/// 3. <b>Клубы НЕ заводятся.</b> Именно матчинг-по-имени с созданием плодил клубы-дубли
///    (инцидент И-13: 59 клубов на 5141 результат за один переимпорт). Не нашёлся — заявка
///    уходит на псевдоклуб «No club» и ждёт импорта протокола, а число таких строк попадает
///    в отчёт.
///
/// Повторный запуск идемпотентен: он же и есть штатный режим, потому что посев меняется
/// до последнего дня.
/// </summary>
public sealed class StartListPullService : IStartListPullService
{
    /// <summary>Псевдоклуб для строк без опознанного клуба — тот же, что у импорта результатов.</summary>
    private const string NoClubName = "No club";

    /// <summary>
    /// Источник печатает время старта БЕЗ часового пояса — это местное израильское время.
    /// Перевод в UTC делается здесь, один раз: дальше по системе ходит уже момент времени.
    /// </summary>
    private static readonly TimeZoneInfo IsraelTimeZone = ResolveIsraelTimeZone();

    private readonly SwimmDbContext _db;
    private readonly ICompetitionDiscoveryProvider _provider;
    private readonly ILogger<StartListPullService> _logger;

    public StartListPullService(
        SwimmDbContext db,
        ICompetitionDiscoveryProvider provider,
        ILogger<StartListPullService> logger)
    {
        _db = db;
        _provider = provider;
        _logger = logger;
    }

    public async Task<StartListPullReport> PullAsync(int orgCompId, CancellationToken ct = default)
    {
        var discovered = await _db.DiscoveredCompetitions
            .FirstOrDefaultAsync(d => d.OrgCompId == orgCompId, ct);

        if (discovered is null)
            return await FinishAsync(Fail(orgCompId, null,
                $"Соревнование {orgCompId} не значится во «Входящих» — сначала синхронизируйте список."), ct);

        // Риск №1 плана (§1.6): единственный известный путь к logligId — iframe на comp.asp,
        // и появляется ли он ДО старта, неизвестно. Это не сбой забора, а «тянуть пока нечего».
        if (discovered.LogligId is not int logligId)
            return await FinishAsync(Empty(orgCompId, null,
                "У соревнования нет loglig-id: детальная страница ещё не читалась либо на ней " +
                "нет iframe. Нажмите «Обновить» во «Входящих»."), ct);

        IReadOnlyList<LogligDisciplineGridRowDto> grid;
        try
        {
            grid = await _provider.FetchDisciplineGridAsync(logligId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Стартовый протокол {OrgCompId}: программа дня не прочиталась", orgCompId);
            return await FinishAsync(Fail(orgCompId, logligId, ex.Message), ct);
        }

        // ── Фаза A: читаем все заплывы, копим сырые строки ───────────────────
        var raw = new List<(LogligDisciplineGridRowDto Event, LogligStartListRowDto Row)>();
        var fetchedDisciplines = new List<int>();
        var failed = 0;

        foreach (var ev in grid)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var startList = await _provider.FetchStartListAsync(ev.DisciplineId, ct);
                fetchedDisciplines.Add(ev.DisciplineId);
                foreach (var row in startList.Rows) raw.Add((ev, row));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                _logger.LogWarning(ex,
                    "Стартовый протокол {OrgCompId}: заплыв {DisciplineId} не прочитался",
                    orgCompId, ev.DisciplineId);
            }
        }

        // Программа есть, а стартовых протоколов нет — посев ещё не сделан. За неделю до
        // старта это норма, и красить её в ошибку значит приучить не смотреть на статус.
        if (raw.Count == 0 && failed == 0)
            return await FinishAsync(
                Empty(orgCompId, logligId, "Посев ещё не сделан: в заплывах нет ни одной строки.")
                    with { Events = grid.Count, EventsFetched = fetchedDisciplines.Count }, ct);

        // ── Фаза B: справочники. Пловцы и стили могут создаваться, клубы — нет ──
        var resolution = await ResolveReferencesAsync(raw, ct);

        // ── Фаза C: сверяем с тем, что уже лежит, и применяем ────────────────
        var drafts = BuildDrafts(raw, resolution, discovered);

        // Скоуп сверки — ТОЛЬКО успешно прочитанные заплывы: иначе оборванная сеть на
        // середине выглядела бы как «все снялись» и вычистила бы половину протокола.
        var existing = await _db.CompetitionEntries
            .Where(e => e.OrgCompId == orgCompId && fetchedDisciplines.Contains(e.OrgDisciplineId))
            .ToListAsync(ct);

        var match = StartListMatcher.Match(
            existing, drafts,
            e => new StartListKey(e.OrgDisciplineId, e.Heat, e.Lane, e.SwimmerId),
            d => new StartListKey(d.OrgDisciplineId, d.Heat, d.Lane, d.SwimmerId));

        var pulledAt = DateTime.UtcNow;
        foreach (var (old, fresh) in match.Matched) Apply(old, fresh, pulledAt);
        foreach (var (old, fresh) in match.Moved) Apply(old, fresh, pulledAt);
        foreach (var fresh in match.Added) _db.CompetitionEntries.Add(Apply(new CompetitionEntry(), fresh, pulledAt));
        _db.CompetitionEntries.RemoveRange(match.Removed);

        await _db.SaveChangesAsync(ct);

        var status = failed > 0 ? StartListPullStatus.Partial : StartListPullStatus.Ok;
        return await FinishAsync(new StartListPullReport(
            orgCompId, logligId, status,
            failed > 0 ? $"Не прочиталось заплывов: {failed} из {grid.Count}." : null,
            grid.Count, fetchedDisciplines.Count, drafts.Count,
            match.Added.Count, match.Moved.Count, match.Removed.Count, match.Matched.Count,
            resolution.SwimmersCreated, resolution.SwimmersStamped, resolution.ClubsUnmatched,
            pulledAt), ct);
    }

    // ── Справочники ──────────────────────────────────────────────────────────

    private sealed class Resolution
    {
        /// <summary>Ключ строки источника → пловец. Ключ — loglig-id либо «имя#год».</summary>
        public Dictionary<string, Swimmer> Swimmers { get; } = [];
        public Dictionary<string, Club> Clubs { get; } = [];
        public Dictionary<string, Style> Styles { get; } = [];
        public Club NoClub { get; set; } = null!;
        public int SwimmersCreated { get; set; }
        public int SwimmersStamped { get; set; }
        public int ClubsUnmatched { get; set; }
    }

    private async Task<Resolution> ResolveReferencesAsync(
        List<(LogligDisciplineGridRowDto Event, LogligStartListRowDto Row)> raw, CancellationToken ct)
    {
        var result = new Resolution();

        var swimmers = await _db.Swimmers
            .Include(s => s.Club)
            .ToListAsync(ct);
        var byLogligId = swimmers
            .Where(s => s.LogligId is not null)
            .ToDictionary(s => s.LogligId!.Value);
        var byNameKey = new Dictionary<string, Swimmer>();
        foreach (var s in swimmers)
        {
            var key = NameKey(s.LastName, s.FirstName, s.BirthYear);
            byNameKey.TryAdd(key, s);
        }

        // Имя источника — одной ячейкой в порядке «имя фамилия»; резать вслепую нельзя.
        var resolver = new LogligSwimmerNameResolver(swimmers.Select(s =>
            new KnownSwimmerName(s.LastName, s.FirstName, s.BirthYear, s.Club?.Name ?? string.Empty)));

        var clubs = await _db.Clubs.Where(c => c.MergedIntoId == null).ToListAsync(ct);
        foreach (var c in clubs) result.Clubs.TryAdd(c.Name.Trim(), c);

        result.NoClub = result.Clubs.GetValueOrDefault(NoClubName)
                        ?? await FindOrCreateNoClubAsync(result, ct);

        foreach (var s in await _db.Styles.ToListAsync(ct)) result.Styles.TryAdd(s.Name, s);

        // Новички этого забора: один ребёнок записан в несколько заплывов, и без общей
        // корзины он заводился бы по разу на каждый.
        var createdThisPull = new Dictionary<string, Swimmer>();

        foreach (var (ev, row) in raw)
        {
            // ── пловец ──
            var rowKey = RowSwimmerKey(row);
            if (!result.Swimmers.ContainsKey(rowKey))
            {
                Swimmer? swimmer = null;

                if (row.LogligId is int logligId && byLogligId.TryGetValue(logligId, out var byId))
                    swimmer = byId;

                if (swimmer is null)
                {
                    var resolved = resolver.Resolve(row.FullName, row.BirthYear, row.Club);
                    var nameKey = NameKey(resolved.LastName, resolved.FirstName, row.BirthYear ?? 0);

                    // Доверяем ключу имени, только когда резолвер СОПОСТАВИЛ его с базой.
                    // При Matched=false фамилия угадана эвристикой «последний токен», и
                    // склеивать по ней с существующим пловцом — это заводить тёзку-двойника.
                    if (resolved.Matched && byNameKey.TryGetValue(nameKey, out var byName))
                    {
                        swimmer = byName;
                    }
                    else if (createdThisPull.TryGetValue(nameKey, out var justCreated))
                    {
                        // Тот же новичок во втором своём заплыве. Без этой ветки один
                        // ребёнок заводился бы столько раз, во скольких заплывах записан.
                        swimmer = justCreated;
                    }
                    else
                    {
                        // Решение В5: новичка заводим сразу. Иначе родитель ребёнка, который
                        // ещё ни разу не плыл, не получит ссылку — а это ровно тот, кому
                        // стартовый протокол нужнее всего.
                        swimmer = new Swimmer
                        {
                            LastName = resolved.LastName,
                            FirstName = resolved.FirstName,
                            BirthYear = row.BirthYear ?? 0,
                            Gender = PersonGender(ev.Gender),
                            Origin = "isr"
                        };
                        _db.Swimmers.Add(swimmer);
                        createdThisPull[nameKey] = swimmer;
                        result.SwimmersCreated++;
                    }
                }

                // Штамп loglig-id: источник печатает его ССЫЛКОЙ на карточку самого пловца —
                // доказательство того же класса, что и протокол (ср. LogligStampService).
                if (row.LogligId is int id && swimmer.LogligId is null)
                {
                    swimmer.LogligId = id;
                    swimmer.LogligIdStatus = "Verified";
                    swimmer.LogligIdSource = "startlist";
                    swimmer.LogligIdVerifiedAt = DateTime.UtcNow;
                    byLogligId[id] = swimmer;
                    if (swimmer.Id != 0) result.SwimmersStamped++;
                }

                result.Swimmers[rowKey] = swimmer;
            }

            // ── клуб: только матчинг, без создания (см. решение 3 в шапке класса) ──
            var clubName = row.Club.Trim();
            if (clubName.Length == 0 || !result.Clubs.ContainsKey(clubName))
                result.ClubsUnmatched++;

            // ── стиль: канонический ключ, создаём при отсутствии ──
            if (ev.StyleName.Length > 0 && !result.Styles.ContainsKey(ev.StyleName))
            {
                var style = new Style { Name = ev.StyleName };
                _db.Styles.Add(style);
                result.Styles[ev.StyleName] = style;
            }
        }

        // Один SaveChanges: дальше нужны Id новых пловцов и стилей.
        await _db.SaveChangesAsync(ct);
        return result;
    }

    private async Task<Club> FindOrCreateNoClubAsync(Resolution result, CancellationToken ct)
    {
        var noClub = await _db.Clubs.FirstOrDefaultAsync(c => c.MergedIntoId == null && c.Name == NoClubName, ct);
        if (noClub is null)
        {
            noClub = new Club { Name = NoClubName, NameEn = NoClubName };
            _db.Clubs.Add(noClub);
            await _db.SaveChangesAsync(ct);
        }

        result.Clubs[NoClubName] = noClub;
        return noClub;
    }

    // ── Черновики заявок ─────────────────────────────────────────────────────

    private sealed record EntryDraft(
        int OrgCompId, DateTime CompDate, string CompName,
        int OrgDisciplineId, int? OrgEventNumber, int Heat, int Lane,
        int SwimmerId, int ClubId, int StyleId,
        string Distance, string Gender, string? EventCategory, string? AgeBand,
        DateTime? HeatStartAtUtc, string? Round, int? SeedTimeMs, string SeedTimeOriginal);

    private static List<EntryDraft> BuildDrafts(
        List<(LogligDisciplineGridRowDto Event, LogligStartListRowDto Row)> raw,
        Resolution resolution,
        DiscoveredCompetition discovered)
    {
        var drafts = new List<EntryDraft>(raw.Count);
        var seen = new HashSet<StartListKey>();

        foreach (var (ev, row) in raw)
        {
            var swimmer = resolution.Swimmers[RowSwimmerKey(row)];
            var club = resolution.Clubs.GetValueOrDefault(row.Club.Trim()) ?? resolution.NoClub;
            var style = resolution.Styles.GetValueOrDefault(ev.StyleName);
            if (style is null) continue;   // дисциплина без опознанного стиля — не заявка

            var key = new StartListKey(ev.DisciplineId, row.Heat, row.Lane, swimmer.Id);
            // Источник изредка печатает строку дважды; уникальный индекс это отобьёт
            // исключением, а нам дубль в одном заборе просто не нужен.
            if (!seen.Add(key)) continue;

            // Дата дня — из САМОГО заплыва, если источник её назначил: у многодневки день
            // зашит в дату старта, и это точнее, чем DateStart всего события.
            var compDate = (ev.StartAtLocal?.Date ?? discovered.DateStart.Date);

            drafts.Add(new EntryDraft(
                discovered.OrgCompId, compDate, discovered.Name,
                ev.DisciplineId, ev.EventNumber, row.Heat, row.Lane,
                swimmer.Id, club.Id, style.Id,
                ev.Distance, ev.Gender, ev.Category, ev.AgeBand,
                ToUtc(ev.StartAtLocal, row.HeatStartAt), row.Round,
                ParseSeedMs(row.SeedTime), row.SeedTime ?? string.Empty));
        }

        return drafts;
    }

    private static CompetitionEntry Apply(CompetitionEntry entry, EntryDraft d, DateTime pulledAt)
    {
        entry.OrgCompId = d.OrgCompId;
        entry.CompDate = d.CompDate;
        entry.CompName = d.CompName;
        entry.OrgDisciplineId = d.OrgDisciplineId;
        entry.OrgEventNumber = d.OrgEventNumber;
        entry.Heat = d.Heat;
        entry.Lane = d.Lane;
        entry.SwimmerId = d.SwimmerId;
        entry.ClubId = d.ClubId;
        entry.StyleId = d.StyleId;
        entry.Distance = d.Distance;
        entry.Gender = d.Gender;
        entry.EventCategory = d.EventCategory;
        entry.AgeBand = d.AgeBand;
        entry.HeatStartAt = d.HeatStartAtUtc;
        entry.Round = d.Round;
        entry.SeedTimeMs = d.SeedTimeMs;
        entry.SeedTimeOriginal = d.SeedTimeOriginal;
        entry.PulledAt = pulledAt;
        return entry;
    }

    // ── Мелочи ───────────────────────────────────────────────────────────────

    /// <summary>Ключ строки источника: loglig-id, а если его нет — имя с годом.</summary>
    private static string RowSwimmerKey(LogligStartListRowDto row) =>
        row.LogligId is int id ? $"L{id}" : $"N{row.FullName.Trim()}#{row.BirthYear}";

    private static string NameKey(string last, string first, int year) =>
        $"{SwimmerDedupService.Normalize(last)}|{SwimmerDedupService.Normalize(first)}|{year}";

    /// <summary>
    /// Пол ЧЕЛОВЕКА из категории заплыва. «none» (микст) не пишем: человек не бывает
    /// «none», и записать это в карточку значило бы нарушить И14 — пол живёт в карточке.
    /// </summary>
    private static string? PersonGender(string eventGender) =>
        eventGender is "male" or "female" ? eventGender : null;

    /// <summary>
    /// Дата события + время заплыва → момент в UTC. Времени заплыва нет — берём время события.
    /// Перевод не удался (несуществующий час при переводе часов) — null: приблизительное
    /// время на витрине и так помечено «≈», а врать точным моментом нельзя.
    /// </summary>
    private static DateTime? ToUtc(DateTime? eventStartLocal, string? heatStartHhmm)
    {
        if (eventStartLocal is not DateTime eventStart) return null;

        var local = eventStart;
        if (heatStartHhmm is { Length: > 0 } && TimeOnly.TryParse(heatStartHhmm, out var heatTime))
            local = eventStart.Date + heatTime.ToTimeSpan();

        try
        {
            return TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(local, DateTimeKind.Unspecified), IsraelTimeZone);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static int? ParseSeedMs(string? seed) =>
        seed is null ? null : LogligClient.ParseTimeToMilliseconds(seed);

    private static TimeZoneInfo ResolveIsraelTimeZone()
    {
        // IANA-идентификатор работает и на Windows (.NET 6+ ходит в ICU), но у машин с
        // отключённым ICU остаётся только windows-имя — поэтому фоллбек, а не одно имя.
        foreach (var id in new[] { "Asia/Jerusalem", "Israel Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.Utc;
    }

    // ── Журнал заборов ───────────────────────────────────────────────────────

    private static StartListPullReport Fail(int orgCompId, int? logligId, string error) =>
        new(orgCompId, logligId, StartListPullStatus.Error, error, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, DateTime.UtcNow);

    private static StartListPullReport Empty(int orgCompId, int? logligId, string note) =>
        new(orgCompId, logligId, StartListPullStatus.Empty, note, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, DateTime.UtcNow);

    /// <summary>Пишет строку журнала — и при неудаче тоже: «почему вчера ничего не приехало»
    /// иначе не разобрать. Та же роль, что у <c>ImportReconciliation</c> для импорта.</summary>
    private async Task<StartListPullReport> FinishAsync(StartListPullReport report, CancellationToken ct)
    {
        _db.StartListPulls.Add(new StartListPull
        {
            OrgCompId = report.OrgCompId,
            PulledAt = report.PulledAt,
            Events = report.Events,
            Entries = report.Entries,
            Added = report.Added,
            Removed = report.Removed,
            Moved = report.Moved,
            Status = report.Status,
            Error = report.Error
        });
        await _db.SaveChangesAsync(ct);
        return report;
    }
}
