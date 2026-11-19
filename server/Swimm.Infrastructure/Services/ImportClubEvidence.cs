using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// «Тот же клуб» — второе доказательство запасных путей узнавания пловца на импорте
/// (<see cref="ImportSwimmerResolver"/>: другое написание, переставленные имя и фамилия, год со сдвигом
/// на сто лет). Решение Р109 (§7.2 docs/plans/swimmer-club-change-plan.md, Ф4, 19.11.2026).
///
/// Раньше клуб протокола сравнивался только с <c>Swimmer.ClubId</c>. После Р98 карточка меняет клуб
/// лишь с подтверждения loglig, и ребёнок, перешедший в другой клуб и напечатанный другим написанием,
/// получал дубль. Теперь клуб протокола (канон после склеек) принимается, если он — по порядку:
/// <list type="number">
///   <item><b>клуб карточки</b> (как было);</item>
///   <item><b>любой клуб пловца за 2 последних сезона</b> по строкам протоколов (уже плыл за новый
///     клуб, а кандидат на смену ждёт loglig);</item>
///   <item><b>клуб на карточке loglig</b> кандидата — только с подтверждённым loglig-id. Сначала
///     суточный кэш ответов из <c>Sys_SwimmerClubCandidates</c> (Р98), сеть — только если кэша нет,
///     не больше <see cref="MaxLogligRequests"/> карточек за импорт, ~1 запрос в секунду. Без
///     loglig-id в loglig не ходим.</item>
/// </list>
/// Каждый шаг требует ровно ОДНОГО подходящего кандидата: двое подошли — гадать нельзя, null, и
/// вызывающий заводит новую карточку (решает человек, как раньше). Следующий шаг пробуется, только
/// если на предыдущем не подошёл никто: дорогой шаг (сеть) — в последнюю очередь.
///
/// Живёт один импорт. История и названия клубов грузятся лениво — при первом запасном случае, а
/// таких в протоколе единицы.
/// </summary>
internal sealed class ImportClubEvidence(SwimmDbContext db, ILogligClient? loglig, TimeSpan? requestGap = null)
{
    /// <summary>Сколько карточек loglig можно открыть за один импорт: запасные случаи редки, сеть — не узкое место импорта.</summary>
    public const int MaxLogligRequests = 20;

    /// <summary>Сколько сезонов истории клубов принимаем (текущий и прошлый).</summary>
    public const int HistorySeasons = 2;

    private readonly TimeSpan _gap = requestGap ?? SwimmerClubLogligCheckService.DefaultRequestGap;
    private DateTime? _lastRequestAt;
    private int _requests;

    private ClubNameMatcher? _matcher;
    private Dictionary<int, HashSet<int>>? _history;
    // Ответ loglig за этот импорт: loglig-id → клуб на карточке (null — не открылась / клуба нет).
    private readonly Dictionary<int, string?> _logligAnswers = new();

    /// <summary>Подошедший кандидат и чем доказан «тот же клуб» (для диагностики импорта).</summary>
    public sealed record Match(Swimmer Swimmer, string Evidence);

    /// <summary>
    /// Ровно один из <paramref name="candidates"/>, у кого клуб протокола <paramref name="club"/> —
    /// «тот же», и чем это доказано; null — никто или двое и больше.
    /// </summary>
    public async Task<Match?> PickAsync(IReadOnlyCollection<Swimmer> candidates, Club club)
    {
        if (candidates.Count == 0) return null;

        // 1. Клуб карточки — прежнее правило: клуб строки или его канон против Swimmer.ClubId.
        var raw = new[] { club.Id, club.MergedIntoId ?? club.Id };
        var byCard = candidates.Where(s => s.ClubId is int c && raw.Contains(c)).Take(2).ToList();
        if (byCard.Count == 1) return new Match(byCard[0], "клуб карточки");
        if (byCard.Count > 1) return null;

        var matcher = await MatcherAsync();
        var target = matcher.Live(club.Id) ?? club.MergedIntoId ?? club.Id;

        // 2. Любой клуб пловца за последние два сезона (канон после склеек).
        var history = await HistoryAsync(matcher);
        var byHistory = candidates
            .Where(s => s.Id != 0 && history.TryGetValue(s.Id, out var clubs) && clubs.Contains(target))
            .Take(2).ToList();
        if (byHistory.Count == 1) return new Match(byHistory[0], $"клуб из истории за {HistorySeasons} сезона");
        if (byHistory.Count > 1) return null;

        // 3. Клуб на карточке loglig — только у подтверждённых loglig-id.
        if (loglig is null) return null;
        var withLoglig = candidates
            .Where(s => s.LogligId is not null && s.LogligIdStatus == "Verified")
            .Take(3).ToList();   // запасной ключ слабый: больше трёх кандидатов — не тот случай, чтобы ходить в сеть
        if (withLoglig.Count == 0) return null;

        var byLoglig = new List<(Swimmer Swimmer, string ClubName)>();
        foreach (var s in withLoglig)
        {
            var name = await LogligClubAsync(s.Id, s.LogligId!.Value);
            if (name is null || NameKey.Normalize(name).Contains(NameKey.Normalize("ארכיון"), StringComparison.Ordinal))
                continue;   // карточка не открылась, клуба нет или «ארכיון איגוד» — не доказательство
            if (matcher.Find(name).Contains(target)) byLoglig.Add((s, name));
        }
        return byLoglig.Count == 1
            ? new Match(byLoglig[0].Swimmer, $"клуб на карточке loglig #{byLoglig[0].Swimmer.LogligId} «{byLoglig[0].ClubName}»")
            : null;
    }

    /// <summary>Клуб на карточке loglig: кэш этого импорта → суточный кэш кандидата (Р98) → сеть в пределах лимита.</summary>
    private async Task<string?> LogligClubAsync(int swimmerId, int logligId)
    {
        if (_logligAnswers.TryGetValue(logligId, out var known)) return known;

        var cached = await db.SwimmerClubCandidates.AsNoTracking()
            .Where(c => c.SwimmerId == swimmerId)
            .Select(c => new { c.CheckedAt, c.LogligId, c.LogligClubName })
            .FirstOrDefaultAsync();
        if (cached is not null
            && SwimmerClubLogligRule.CacheValid(DateTime.UtcNow, cached.CheckedAt, cached.LogligId, logligId, cached.LogligClubName))
            return _logligAnswers[logligId] = cached.LogligClubName;

        if (_requests >= MaxLogligRequests) return null;   // лимит — не запоминаем: другой импорт сходит
        _requests++;
        await ThrottleAsync();
        string? club;
        try { club = (await loglig!.GetPlayerCardAsync(logligId))?.ClubName?.Trim(); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { club = null; }
        return _logligAnswers[logligId] = string.IsNullOrWhiteSpace(club) ? null : club;
    }

    private async Task ThrottleAsync()
    {
        if (_lastRequestAt is DateTime last)
        {
            var wait = _gap - (DateTime.UtcNow - last);
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
        }
        _lastRequestAt = DateTime.UtcNow;
    }

    private async Task<ClubNameMatcher> MatcherAsync()
    {
        if (_matcher is not null) return _matcher;
        var clubs = await db.Clubs.AsNoTracking()
            .Select(c => new ClubAliasClub(c.Id, c.Name, c.NameEn, c.MergedIntoId, c.IsPseudo))
            .ToListAsync();
        var names = await db.ClubNames.AsNoTracking()
            .Select(n => new ClubAliasName(n.Id, n.ClubId, n.Name, n.NameEn, n.Kind))
            .ToListAsync();
        return _matcher = new ClubNameMatcher(clubs, names);
    }

    /// <summary>Пловец → его клубы (канон) по строкам с начала прошлого сезона. Псевдоклубы не в счёт.</summary>
    private async Task<Dictionary<int, HashSet<int>>> HistoryAsync(ClubNameMatcher matcher)
    {
        if (_history is not null) return _history;
        var since = SeasonMath.StartOf(SeasonMath.CurrentStartYear() - (HistorySeasons - 1));
        var pairs = await db.Results.AsNoTracking()
            .Where(r => r.CompetitionDate >= since)
            .Select(r => new { r.SwimmerId, r.ClubId })
            .Distinct()
            .ToListAsync();
        _history = new Dictionary<int, HashSet<int>>();
        foreach (var p in pairs)
        {
            if (matcher.Live(p.ClubId) is not int club) continue;
            if (!_history.TryGetValue(p.SwimmerId, out var set)) _history[p.SwimmerId] = set = [];
            set.Add(club);
        }
        return _history;
    }
}
