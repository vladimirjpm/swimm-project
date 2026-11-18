using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Domain;

namespace Swimm.Application.Mapping;

/// <summary>
/// Клуб из справочника — как лежит в базе. <paramref name="MergedIntoId"/> — клуб склеен в
/// другой (приёмник); <paramref name="IsPseudo"/> — в графе клуба стояла страна/сборная.
/// </summary>
public sealed record SwimmerClubRef(int Id, string Name, string NameEn, bool IsPseudo, int? MergedIntoId = null)
{
    /// <summary>
    /// Прежние названия клуба (<c>ClubNames</c>, план swimmer-club-change §5, Н8) — по ним строка
    /// сезона пишет «then: …». Нужны только живому клубу: у надгробия их нет, они у приёмника.
    /// </summary>
    public IReadOnlyList<ClubFormerNameRow> FormerNames { get; init; } = [];
}

/// <summary>
/// История клубов пловца по сезонам (docs/plans/swimmer-club-change-plan.md, Ф2–Ф3).
/// Чистая функция: заплывы и справочник клубов приносит вызывающий.
///
/// Правила одни на все экраны, которые спрашивают «за какой клуб он плыл»:
/// <list type="bullet">
/// <item>клуб строки — <c>Results.ClubId</c>, а не карточка пловца;</item>
/// <item>только индивидуальные заплывы: у эстафеты в SwimmerId бывает «пловец-тень»
/// (docs/relays.md) — то же правило, что у подписки группы на клуб (<c>HubGroupClubRoster</c>);</item>
/// <item>псевдоклуб (страна, сборная) — не клуб, строка пропускается;</item>
/// <item>склеенный клуб сводится к приёмнику: два имени одного клуба — не смена клуба (К4).</item>
/// </list>
/// </summary>
public static class SwimmerClubHistory
{
    /// <summary>Сколько звеньев склейки проходить: цепочки длиннее — ошибка данных (или петля).</summary>
    private const int MaxMergeHops = 8;

    /// <summary>Клубы, которые нужно достать из справочника под эти заплывы.</summary>
    public static IReadOnlyList<int> ClubIdsOf(IEnumerable<SeasonSwimRow> rows) =>
        rows.Where(r => !r.IsRelay && r.ClubId > 0).Select(r => r.ClubId).Distinct().ToList();

    /// <summary>
    /// Настоящий клуб под id строки: склеенный — приёмник (по цепочке), псевдоклуб и
    /// неизвестный id — null. Справочник обязан содержать и приёмников.
    /// </summary>
    public static SwimmerClubRef? Resolve(int clubId, IReadOnlyDictionary<int, SwimmerClubRef> clubs)
    {
        if (!clubs.TryGetValue(clubId, out var club)) return null;

        for (var hop = 0; club.MergedIntoId is int target && hop < MaxMergeHops; hop++)
        {
            if (!clubs.TryGetValue(target, out var next)) return null;
            club = next;
        }

        return club.MergedIntoId is null && !club.IsPseudo ? club : null;
    }

    /// <summary>
    /// История: строка на (сезон × клуб), новые сезоны сверху, внутри сезона — клуб
    /// последнего старта первым (К5).
    /// </summary>
    public static List<SwimmerClubSeasonDto> Build(
        IEnumerable<SeasonSwimRow> rows, IReadOnlyDictionary<int, SwimmerClubRef> clubs) =>
        rows
            .Where(r => !r.IsRelay)
            .Select(r => (Row: r, Club: Resolve(r.ClubId, clubs)))
            .Where(x => x.Club is not null)
            .GroupBy(x => (Season: SeasonAggregator.SeasonOf(x.Row), ClubId: x.Club!.Id))
            .Select(g =>
            {
                var club = g.First().Club!;
                var first = g.Min(x => x.Row.CompetitionDate);
                var last = g.Max(x => x.Row.CompetitionDate);
                var then = ThenName(g.Select(x => x.Row.ClubNameId), club);
                return new SwimmerClubSeasonDto
                {
                    Season = g.Key.Season,
                    SeasonLabel = SeasonMath.Label(g.Key.Season),
                    ClubId = club.Id,
                    Name = club.Name,
                    NameEn = string.IsNullOrWhiteSpace(club.NameEn) || club.NameEn == club.Name ? null : club.NameEn,
                    Swims = g.Count(),
                    FirstDate = first.ToString("yyyy-MM-dd"),
                    LastDate = last.ToString("yyyy-MM-dd"),
                    Courses = CoursesOf(g.Select(x => x.Row.PoolType)),
                    ThenName = then?.Name,
                    ThenNameEn = then is null || string.IsNullOrWhiteSpace(then.NameEn) || then.NameEn == then.Name
                        ? null : then.NameEn,
                };
            })
            .OrderByDescending(h => h.Season)
            .ThenByDescending(h => h.LastDate, StringComparer.Ordinal)
            .ThenByDescending(h => h.Swims)
            .ThenBy(h => h.ClubId)
            .ToList();

    /// <summary>
    /// Клуб сезона — клуб ПОСЛЕДНЕГО старта в нём (стороны H2H при выбранном сезоне, Ф3).
    /// Два клуба в один последний день — тот, где заплывов больше. null — в сезоне стартов
    /// за настоящий клуб нет.
    /// </summary>
    public static SwimmerClubSeasonDto? ClubOfSeason(IEnumerable<SwimmerClubSeasonDto> history, int season) =>
        history
            .Where(h => h.Season == season)
            .OrderByDescending(h => h.LastDate, StringComparer.Ordinal)
            .ThenByDescending(h => h.Swims)
            .ThenBy(h => h.ClubId)
            .FirstOrDefault();

    /// <summary>Настоящих клубов за карьеру — значок «несколько клубов» стоит при двух и больше (К4).</summary>
    public static int ClubCount(IEnumerable<SwimmerClubSeasonDto> history) =>
        history.Select(h => h.ClubId).Distinct().Count();

    /// <summary>Имя для витрины: иврит по умолчанию, английское — фоллбек.</summary>
    public static string DisplayName(SwimmerClubSeasonDto club) =>
        string.IsNullOrWhiteSpace(club.Name) ? club.NameEn ?? string.Empty : club.Name;

    /// <summary>
    /// Прежнее название клуба в строке сезона (Н8): если под ним напечатано больше заплывов этой
    /// строки, чем под текущим (<c>Results.ClubNameId</c>). Не по датам: старое и новое
    /// название живут в протоколах внахлёст (Aluma уже с 24.10.2025, Hapoel — до 16.02.2026).
    /// null — все заплывы под текущим названием или название не переименование (дубль).
    /// </summary>
    private static ClubFormerNameRow? ThenName(IEnumerable<int?> clubNameIds, SwimmerClubRef club)
    {
        // Текущее название (null) голосует наравне с прежними; при равенстве — прежнее: строка
        // сезона и так подписана текущим, подсказка о старом важнее.
        var top = clubNameIds
            .GroupBy(id => id ?? 0)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key != 0)
            .ThenBy(g => g.Key)
            .Select(g => g.Key)
            .FirstOrDefault();
        return top != 0
            ? club.FormerNames.FirstOrDefault(n => n.Id == top && n.Kind == Domain.Entities.ClubNameKinds.Former)
            : null;
    }

    /// <summary>25м — зимняя (короткая) вода, 50м — летняя; порядок «зима, лето».</summary>
    private static List<string> CoursesOf(IEnumerable<string> poolTypes)
    {
        var pools = poolTypes.Select(p => (p ?? string.Empty).Trim().ToLowerInvariant()).ToHashSet();
        var courses = new List<string>(2);
        if (pools.Contains("25m")) courses.Add(StandingKinds.Winter);
        if (pools.Contains("50m")) courses.Add(StandingKinds.Summer);
        return courses;
    }
}
