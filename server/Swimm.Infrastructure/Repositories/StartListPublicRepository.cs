using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Публичный read-путь стартового протокола (docs/plans/start-list-plan.md, шаг С6).
/// Читает через <see cref="SwimmReadDbContext"/> (роль swimm_ro), как остальные публичные
/// репозитории.
///
/// Три выдачи — один и тот же набор заявок под разным приближением, поэтому проекция строки
/// (<see cref="ToSwim"/>) одна на всех: разъехавшиеся копии «строки заплыва» — та самая
/// болезнь, ради которой в проекте появился общий `SwimRow`.
/// </summary>
public class StartListPublicRepository : IStartListPublicRepository
{
    /// <summary>
    /// Класс качества посевного времени. Третий после «протокола» и «справочника рекордов»:
    /// это личный рекорд С ДРУГОГО старта, и витрина обязана отличать его от результата (И11).
    /// </summary>
    private const string SeedQuality = "seed";

    /// <summary>Разумный потолок окна «ближайших стартов», чтобы запрос не выродился в скан.</summary>
    private const int MaxUpcomingDays = 120;

    private readonly SwimmReadDbContext _read;

    public StartListPublicRepository(SwimmReadDbContext read)
    {
        _read = read;
    }

    public Task<bool> ExistsAsync(
        int orgCompId, int? orgDisciplineId = null, int? swimmerId = null, CancellationToken ct = default)
    {
        var q = _read.CompetitionEntries.AsNoTracking().Where(e => e.OrgCompId == orgCompId);
        if (orgDisciplineId is int d) q = q.Where(e => e.OrgDisciplineId == d);
        if (swimmerId is int s) q = q.Where(e => e.SwimmerId == s);
        return q.AnyAsync(ct);
    }

    public async Task<IReadOnlyList<UpcomingCompetitionDto>> GetUpcomingCompetitionsAsync(
        DateTime from, int days, CancellationToken ct = default)
    {
        // Kind=Unspecified обязателен: CompDate лежит в `timestamp without time zone`,
        // и Npgsql отказывается сравнивать её со значением Kind=Utc (та же ловушка, что
        // в GetUpcomingAsync). In-memory провайдер этого не воспроизводит.
        var fromDate = DateTime.SpecifyKind(from.Date, DateTimeKind.Unspecified);
        var toDate = fromDate.AddDays(Math.Clamp(days, 1, MaxUpcomingDays));

        // Группировка в памяти, а не в SQL, СОЗНАТЕЛЬНО: считать «дней» и «пловцов» надо
        // через Distinct().Count() внутри группы, а такую проекцию EF транслирует не
        // всегда — и падает уже на живой БД, потому что in-memory провайдер выполняет
        // её как обычный LINQ (тот же класс ловушки, что Kind=Utc выше).
        // Объём ограничен окном: это заявки на ближайшие недели, тысячи строк, не миллионы.
        var rows = await _read.CompetitionEntries.AsNoTracking()
            .Where(e => e.CompDate >= fromDate && e.CompDate <= toDate)
            .Select(e => new { e.OrgCompId, e.CompName, e.CompDate, e.SwimmerId, e.PulledAt })
            .ToListAsync(ct);

        return rows
            .GroupBy(e => e.OrgCompId)
            .Select(g => new UpcomingCompetitionDto(
                g.Key,
                g.First().CompName,
                g.Min(e => e.CompDate),
                g.Max(e => e.CompDate),
                g.Select(e => e.CompDate.Date).Distinct().Count(),
                g.Count(),
                g.Select(e => e.SwimmerId).Distinct().Count(),
                g.Max(e => e.PulledAt)))
            .OrderBy(r => r.DateStart)
            .ThenBy(r => r.CompName)
            .ToList();
    }

    public async Task<StartListProgrammeDto?> GetProgrammeAsync(int orgCompId, CancellationToken ct = default)
    {
        var rows = await BaseQuery().Where(e => e.OrgCompId == orgCompId).ToListAsync(ct);
        if (rows.Count == 0) return null;

        var days = rows
            .GroupBy(e => e.CompDate.Date)
            .OrderBy(g => g.Key)
            .Select(day => new StartListDayDto(
                day.Key,
                day.GroupBy(e => e.OrgDisciplineId)
                    .Select(BuildEvent)
                    // Порядок ленты — по времени; у заплывов без времени остаётся номер
                    // программы, иначе они молча всплыли бы в начало дня.
                    .OrderBy(e => e.StartAt ?? DateTime.MaxValue)
                    .ThenBy(e => e.EventNumber ?? int.MaxValue)
                    .ToList())
            )
            .ToList();

        return new StartListProgrammeDto(
            orgCompId, rows[0].CompName, days, rows.Count, rows.Max(e => e.PulledAt));
    }

    public async Task<StartListEventHeatsDto?> GetEventAsync(
        int orgCompId, int orgDisciplineId, CancellationToken ct = default)
    {
        var rows = await BaseQuery()
            .Where(e => e.OrgCompId == orgCompId && e.OrgDisciplineId == orgDisciplineId)
            .ToListAsync(ct);
        if (rows.Count == 0) return null;

        var heats = rows
            .GroupBy(e => e.Heat)
            .OrderBy(g => g.Key)
            .Select(h => new StartListHeatDto(
                h.Key,
                h.Min(e => e.HeatStartAt),
                h.Select(e => e.Round).FirstOrDefault(r => r is not null),
                h.OrderBy(e => e.Lane).Select(ToSwim).ToList()))
            .ToList();

        return new StartListEventHeatsDto(
            orgCompId, rows[0].CompName,
            BuildEvent(rows.GroupBy(e => e.OrgDisciplineId).First()),
            heats,
            rows.Max(e => e.PulledAt));
    }

    public async Task<StartListSwimmerDto?> GetSwimmerAsync(
        int orgCompId, int swimmerId, CancellationToken ct = default)
    {
        var rows = await BaseQuery()
            .Where(e => e.OrgCompId == orgCompId && e.SwimmerId == swimmerId)
            .ToListAsync(ct);
        if (rows.Count == 0) return null;

        var swims = rows
            .OrderBy(e => e.HeatStartAt ?? DateTime.MaxValue)
            .ThenBy(e => e.OrgEventNumber ?? int.MaxValue)
            .Select(ToSwim)
            .ToList();

        var first = rows[0].Swimmer;
        return new StartListSwimmerDto(
            orgCompId, rows[0].CompName, swimmerId,
            SwimmerName(first), first.BirthYear, rows[0].Club.Name,
            swims.Min(s => s.HeatStartAt),
            swims,
            rows.Max(e => e.PulledAt));
    }

    public async Task<IReadOnlyList<StartListSwimDto>> GetClubSwimsAsync(
        int orgCompId, int clubId, CancellationToken ct = default)
    {
        var rows = await BaseQuery()
            .Where(e => e.OrgCompId == orgCompId && e.ClubId == clubId)
            .ToListAsync(ct);

        return rows
            .OrderBy(e => e.HeatStartAt ?? DateTime.MaxValue)
            .ThenBy(e => e.OrgEventNumber ?? int.MaxValue)
            .ThenBy(e => e.Lane)
            .Select(ToSwim)
            .ToList();
    }

    public async Task<IReadOnlyList<StartListSwimDto>> GetUpcomingAsync(
        IReadOnlyCollection<int> swimmerIds, DateTime from, int days, CancellationToken ct = default)
    {
        if (swimmerIds.Count == 0) return [];

        // ⚠ Kind=Unspecified обязателен: CompDate — календарная дата, она лежит в
        // `timestamp without time zone` (как CompetitionDate у результата), и Npgsql
        // отказывается сравнивать её со значением Kind=Utc. Дефолтный `from` — это
        // DateTime.UtcNow, поэтому без приведения запрос падает 500 на живой БД.
        // In-memory провайдер этого не воспроизводит — ловится только на Postgres.
        var fromDate = DateTime.SpecifyKind(from.Date, DateTimeKind.Unspecified);
        var toDate = fromDate.AddDays(Math.Clamp(days, 1, MaxUpcomingDays));

        // Отбор по CompDate, а не по HeatStartAt: у заплыва время может быть ещё не назначено,
        // и такие старты обязаны попасть в «ближайшие» — родителю важно, что старт вообще есть.
        var rows = await BaseQuery()
            .Where(e => swimmerIds.Contains(e.SwimmerId)
                        && e.CompDate >= fromDate
                        && e.CompDate <= toDate)
            .ToListAsync(ct);

        return rows
            .OrderBy(e => e.CompDate)
            .ThenBy(e => e.HeatStartAt ?? DateTime.MaxValue)
            .ThenBy(e => e.OrgEventNumber ?? int.MaxValue)
            .Select(ToSwim)
            .ToList();
    }

    // ── Общее ────────────────────────────────────────────────────────────────

    private IQueryable<CompetitionEntry> BaseQuery() =>
        _read.CompetitionEntries.AsNoTracking()
            .Include(e => e.Swimmer)
            .Include(e => e.Club)
            .Include(e => e.Style);

    private static StartListEventDto BuildEvent(IGrouping<int, CompetitionEntry> g)
    {
        var first = g.First();
        return new StartListEventDto(
            g.Key,
            first.OrgEventNumber,
            first.Distance,
            first.Style.Name,
            first.Gender,
            first.EventCategory,
            first.AgeBand,
            IsRelay(first.Distance),
            g.Min(e => e.HeatStartAt),
            g.Count(),
            g.Select(e => e.Heat).Distinct().Count());
    }

    private static StartListSwimDto ToSwim(CompetitionEntry e) => new(
        e.Id,
        e.OrgDisciplineId,
        e.OrgEventNumber,
        e.Distance,
        e.Style.Name,
        e.Gender,
        e.EventCategory,
        e.AgeBand,
        IsRelay(e.Distance),
        e.Heat,
        e.Lane,
        e.HeatStartAt,
        e.Round,
        e.SeedTimeOriginal.Length > 0 ? e.SeedTimeOriginal : null,
        SeedQuality,
        e.SwimmerId,
        SwimmerName(e.Swimmer),
        e.Swimmer.BirthYear > 0 ? e.Swimmer.BirthYear : null,
        e.ClubId,
        e.Club.Name,
        e.ResultId,
        e.Status);

    /// <summary>
    /// Эстафета выводится из дистанции («4X50»), отдельного флага у заявки нет: у результатов
    /// признак — <c>RelayId</c>, а команды у заявки нет вовсе (источник её не печатает,
    /// ноги склеивает пара заплыв+дорожка).
    /// </summary>
    private static bool IsRelay(string distance) =>
        distance.Contains('X', StringComparison.OrdinalIgnoreCase);

    /// <summary>Имя одной строкой. Английское — если заполнено: витрина проекта англоязычная.</summary>
    private static string SwimmerName(Swimmer s) =>
        (s.FirstNameEn.Length > 0 || s.LastNameEn.Length > 0)
            ? $"{s.FirstNameEn} {s.LastNameEn}".Trim()
            : $"{s.FirstName} {s.LastName}".Trim();
}
