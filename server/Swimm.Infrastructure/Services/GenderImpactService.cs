using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Р126: сухой расчёт последствий проставления пола (см. <see cref="GenderImpactRules"/> — что
/// серьёзно, а что нет). Ничего не пишет.
///
/// Что считается ТОЧНО — тем же кодом, что и на сайте, на строках в памяти с подставленным полом:
/// карточки Overview соревнования (New records, Best swim ♂/♀, Most decorated, High Point, Top
/// clubs, награды серии) — <see cref="IGenderOverviewPreview"/>; объединённые места —
/// <see cref="CombinedPlaceCalculator"/>; пара-очки — <see cref="ParaPointsDetector"/>; топ
/// Season best страны — запрос с теми же отборами, что у витрины.
///
/// Что НЕ считается и поэтому сразу уходит человеку: клубные очки по правилу «по заплыву
/// целиком» (их считает <see cref="ContextClubPoints"/> прямо из базы, пол входит в ключ
/// дисциплины) и место общей секции открытой воды (Р78 — пересчитывается внутри пола на
/// переимпорте). Сбой любого расчёта — тоже «не посчитано», а не «последствий нет».
/// </summary>
public sealed class GenderImpactService(
    SwimmDbContext db, IGenderOverviewPreview overview, ILogger<GenderImpactService> logger,
    IShowcaseSeasonProvider? showcase = null) : IGenderImpactService
{
    private sealed record Swim(
        long Id, int CompetitionId, string? Gender, string? EventCategory, int StyleId, string StyleName,
        string Distance, int? TimeMs, bool TimeFail, string? SuspectReason, DateTime Date,
        int? EventId, string CompetitionName, string? PoolType, bool IsMasters, string? StandingKind,
        int? ClubRuleId, string CompetitionDate, bool Combined);

    public async Task<GenderImpact> AssessAsync(int swimmerId, string gender, CancellationToken ct = default)
    {
        if (gender is not ("male" or "female")) return GenderImpact.None;

        var swimmer = await db.Swimmers.AsNoTracking().Where(s => s.Id == swimmerId)
            .Select(s => new { s.BirthYear }).FirstOrDefaultAsync(ct);
        if (swimmer is null) return GenderImpact.None;

        var swims = await db.Results.AsNoTracking()
            .Where(r => r.SwimmerId == swimmerId && r.RelayId == null)
            .Select(r => new Swim(r.Id, r.CompetitionId, r.Gender, r.EventCategory, r.StyleId, r.Style.Name,
                r.Distance, r.TimeMillisecond, r.TimeFail, r.SuspectReason, r.CompetitionDate,
                r.Competition!.EventId, r.Competition.Name, r.Competition.PoolType, r.Competition.IsMasters,
                r.Competition.StandingKindOverride, r.Competition.PointRuleClubsId, r.Competition.Date,
                r.Competition.ShowCombineAllResults))
            .ToListAsync(ct);

        var items = new List<GenderImpactItem>();

        // (г) Личные заплывы противоположного пола в несмешанной программе: либо карточка loglig
        // чужая, либо шапка протокола кривая — в обоих случаях решает человек.
        var opposite = GenderImpactRules.Opposite(gender);
        var against = swims.Where(s => s.Gender == opposite && !GenderImpactRules.IsMixedProgramme(s.EventCategory)).ToList();
        if (against.Count > 0)
            items.Add(new(GenderImpactRules.OppositeGenderSwims,
                $"у пловца уже есть заплывы противоположного пола ({against.Count}): "
                + string.Join(", ", against.Take(3).Select(s => $"{s.Distance} {s.StyleName}, {s.CompetitionName}"))
                + " — карточка loglig или привязка могут быть ошибочны"));

        var empty = swims.Where(s => IsEmpty(s.Gender)).ToList();
        if (empty.Count == 0) return new GenderImpact(items);

        foreach (var scope in empty.GroupBy(s => s.EventId ?? -s.CompetitionId))
        {
            var head = scope.First();
            var name = $"«{head.CompetitionName}» ({head.CompetitionDate})";
            try
            {
                await AssessScopeAsync(swimmerId, gender, scope.Key, scope.ToList(), name, items, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Сухой расчёт последствий пола: пловец {SwimmerId}, {Competition}", swimmerId, name);
                items.Add(new(GenderImpactRules.NotComputed, $"{name}: последствия посчитать не удалось ({ex.Message})"));
            }
        }

        await AssessSeasonBestAsync(swimmerId, swimmer.BirthYear, gender, empty, items, ct);
        return new GenderImpact(items);
    }

    private async Task AssessScopeAsync(int swimmerId, string gender, int scopeKey, List<Swim> empty, string name,
        List<GenderImpactItem> items, CancellationToken ct)
    {
        var filter = scopeKey > 0 ? new ResultFilter { EventId = scopeKey } : new ResultFilter { CompetitionId = -scopeKey };
        var preview = await overview.PreviewAsync(filter, swimmerId, gender);
        if (preview is not null)
            items.AddRange(GenderImpactRules.CompareOverviews(preview.Before, preview.After, name));

        var competitionIds = scopeKey > 0
            ? await db.Competitions.AsNoTracking().Where(c => c.EventId == scopeKey).Select(c => c.Id).ToListAsync(ct)
            : [-scopeKey];
        var emptyIds = empty.Select(s => s.Id).ToHashSet();

        var rows = await db.Results.AsNoTracking()
            .Where(r => competitionIds.Contains(r.CompetitionId))
            .Select(r => new
            {
                r.Id, r.CompetitionId, r.SwimmerId, r.StyleId, r.Distance, r.Gender, r.EventStyleAge, r.EventCategory,
                PoolType = r.Competition!.PoolType, r.TimeMillisecond, r.TimeFail, r.InternationalPoints,
                IsRelay = r.RelayId != null, Combined = r.Competition.ShowCombineAllResults,
            })
            .ToListAsync(ct);
        string GenderOf(long id, string? current) => emptyIds.Contains(id) ? gender : current ?? "";

        // (б) Объединённые места (Combine All Results) — только там, где они материализованы.
        if (rows.Any(r => r.Combined))
        {
            List<CombinedPlaceCalculator.Assignment> Places(bool filled) => CombinedPlaceCalculator.Calculate(rows
                .Select(r => new CombinedPlaceCalculator.Row(r.Id, r.SwimmerId,
                    CombinedPlaceCalculator.EventKeyOf(r.StyleId, r.Distance, r.PoolType,
                        filled ? GenderOf(r.Id, r.Gender) : r.Gender, r.EventCategory, r.EventStyleAge),
                    r.TimeMillisecond, r.TimeFail))
                .ToList());
            var before = Places(false).ToDictionary(a => a.ResultId);
            var changed = Places(true).Count(a => before[a.ResultId] != a);
            if (changed > 0)
                items.Add(new(GenderImpactRules.CombinedPlace,
                    $"{name}: изменится объединённое место (Combine All Results) у {changed} строк"));
        }

        // (б) Пара-очки (Р67): эталон дисциплины считается по полу.
        HashSet<long> Para(bool filled) => ParaPointsDetector.Detect(rows
            .Select(r => new ParaPointsDetector.Row(r.Id, r.CompetitionId, r.SwimmerId, r.StyleId, r.Distance,
                filled ? GenderOf(r.Id, r.Gender) : r.Gender, r.EventCategory, r.InternationalPoints,
                r.TimeMillisecond, r.TimeFail, r.IsRelay))
            .ToList());
        var paraBefore = Para(false);
        var paraAfter = Para(true);
        if (!paraBefore.SetEquals(paraAfter))
            items.Add(new(GenderImpactRules.ParaPoints,
                $"{name}: изменится пометка пара-очков у {paraBefore.Except(paraAfter).Count() + paraAfter.Except(paraBefore).Count()} строк"));

        // Не считается точно → человеку: клубные очки по правилу «по заплыву целиком».
        var rules = await db.PointRulesClubs.AsNoTracking().Include(r => r.Entries).ToListAsync(ct);
        if (empty.Select(s => (s.ClubRuleId, s.IsMasters, s.CompetitionDate)).Distinct()
            .Any(c => PointRulesClubsScoring.NeedsContext(
                CompetitionRuleResolver.Resolve(rules, c.ClubRuleId, c.IsMasters, ParseDate(c.CompetitionDate)))))
            items.Add(new(GenderImpactRules.ContextClubPoints,
                $"{name}: клубные очки считаются по заплыву целиком, пол входит в дисциплину — зачёт может измениться (точно не посчитано)"));

        // Р78: общая секция открытой воды — место внутри пола, пересчитается на переимпорте.
        if (empty.Any(s => s.StandingKind == StandingKinds.OpenWater && GenderImpactRules.IsMixedProgramme(s.EventCategory)))
            items.Add(new(GenderImpactRules.OpenWaterPlace,
                $"{name}: открытая вода — место в общей секции считается внутри пола и изменится при переимпорте"));
    }

    /// <summary>(в) Войдёт ли заплыв в топ Season best страны: те же отборы, что у витрины /season-best.</summary>
    private async Task AssessSeasonBestAsync(int swimmerId, int birthYear, string gender, List<Swim> empty,
        List<GenderImpactItem> items, CancellationToken ct)
    {
        if (birthYear <= 0) return;
        var genderKeys = gender == "male" ? new[] { "male", "M", "m" } : new[] { "female", "F", "f" };
        // Сезон НОВЕЕ витринного (зимний чемпионат ещё не проплыт) на /season-best не показан и
        // отметку SB не даёт (docs/season-boundary-rule.md) — на сайте сейчас ничего не изменится.
        var showcaseYear = showcase is null ? int.MaxValue : await showcase.CurrentStartYearAsync(ct);

        foreach (var s in empty.Where(s => s.TimeMs is > 0 && !s.TimeFail && s.SuspectReason == null && !s.IsMasters
                                           && s.StandingKind != StandingKinds.OpenWater && s.PoolType != null))
        {
            var season = SeasonMath.StartYearOf(s.Date);
            if (season > showcaseYear) continue;
            var (start, endExclusive) = SeasonMath.RangeOf(season);
            var ageBase = season + 1;
            var step = SeasonMath.PeerStepAge(ageBase - birthYear);
            if (step <= 0) continue;

            var faster = await db.Results.AsNoTracking()
                .Where(r => r.TimeMillisecond != null && !r.TimeFail && r.RelayId == null && r.SuspectReason == null
                            && !r.Competition!.IsMasters
                            && r.Competition.StandingKindOverride != StandingKinds.OpenWater
                            && r.Swimmer.BirthYear > 0
                            && r.Competition.PoolType == s.PoolType
                            && r.CompetitionDate >= start && r.CompetitionDate < endExclusive
                            && r.StyleId == s.StyleId && r.Distance == s.Distance
                            && r.SwimmerId != swimmerId
                            && genderKeys.Contains(r.Swimmer.Gender ?? r.Gender)
                            && (ageBase - r.Swimmer.BirthYear >= SeasonMath.AdultPeerAge
                                ? SeasonMath.AdultPeerAge
                                : ageBase - r.Swimmer.BirthYear) == step
                            && r.TimeMillisecond < s.TimeMs)
                .Select(r => r.SwimmerId).Distinct().CountAsync(ct);

            if (faster < GenderImpactRules.SeasonBestTop)
                items.Add(new(GenderImpactRules.SeasonBest,
                    $"Season best {SeasonMath.Label(season)}: {s.Distance} {s.StyleName} — пловец встанет на {faster + 1}-е место "
                    + $"в стране в своей возрастной ступени" + (faster == 0 ? " (получит отметку SB)" : "")));
        }
    }

    private static bool IsEmpty(string? gender) => gender is null or "" or "none";

    private static DateOnly ParseDate(string date) =>
        DateOnly.TryParseExact(date, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : DateOnly.MinValue;
}
