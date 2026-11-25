using System.Text.Json;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>Одно серьёзное последствие проставления пола: код (для тестов и аудита) + текст человеку.</summary>
public sealed record GenderImpactItem(string Code, string Text);

/// <summary>Итог сухого расчёта: пусто — пол можно писать автоматически.</summary>
public sealed record GenderImpact(IReadOnlyList<GenderImpactItem> Serious)
{
    public static readonly GenderImpact None = new([]);
    public bool IsSerious => Serious.Count > 0;
}

/// <summary>
/// Р126 (Влад, 25.11.2026: «если есть переписывания рекордов или что то серьезное — тогда ручная
/// проверка»): что считается серьёзным последствием проставления пола пловцу и его строкам без
/// пола. Чистые функции; данные собирает <c>GenderImpactService</c>.
///
/// Серьёзное — пол автоматически не пишется:
/// (а) рекорд появится, исчезнет или сменит владельца (карточка New records / таб Records);
/// (б) изменится место, медаль или очки у любой строки: объединённые места (Combine All Results),
///     медальные списки Most decorated, клубный зачёт (общий — любое изменение; ♂/♀ — смена
///     порядка клубов), пара-очки, место открытой воды при переимпорте, награды серии;
/// (в) пловец войдёт в Best swim, High Point или в топ Season best страны (или вытеснит оттуда);
/// (г) у пловца уже есть личные заплывы противоположного пола в несмешанной программе.
/// Плюс всё, что точно посчитать нельзя (клубные очки «по заплыву целиком»), — тоже человеку.
///
/// НЕ серьёзное (это и есть закрываемая дыра Р18): строка появляется под фильтром своего пола
/// (результаты, состав клуба), очки клуба в ♂/♀-вкладке Top clubs растут на очки этой строки без
/// смены порядка клубов, заплыв на странице пловца сливается с его же дисциплиной, место среди
/// сверстников ниже топа.
/// </summary>
public static class GenderImpactRules
{
    public const string Record = "record";
    public const string BestSwim = "best-swim";
    public const string Medals = "medals";
    public const string HighPoint = "high-point";
    public const string ClubPoints = "club-points";
    public const string ClubOrder = "club-order";
    public const string SeriesAwards = "series-awards";
    public const string CombinedPlace = "combined-place";
    public const string ParaPoints = "para-points";
    public const string ContextClubPoints = "club-points-context";
    public const string OpenWaterPlace = "open-water-place";
    public const string SeasonBest = "season-best";
    public const string OppositeGenderSwims = "opposite-gender-swims";
    public const string IdentityUnconfirmed = "identity-unconfirmed";
    public const string NotComputed = "not-computed";

    /// <summary>Сколько первых мест рейтинга Season best считаются «топом».</summary>
    public const int SeasonBestTop = 10;

    /// <summary>Смешанная программа заплыва (<c>mix</c>, <c>mix-0</c>, <c>mix-shabbat</c>…).</summary>
    public static bool IsMixedProgramme(string? eventCategory) =>
        (eventCategory ?? "").StartsWith("mix", StringComparison.OrdinalIgnoreCase);

    public static string Opposite(string gender) => gender == "male" ? "female" : "male";

    public static string GenderWord(string gender) => gender == "male" ? "мужчина" : "женщина";

    /// <summary>Сравнить карточки Overview соревнования до и после проставления пола.</summary>
    public static IReadOnlyList<GenderImpactItem> CompareOverviews(
        GenderOverviewCards before, GenderOverviewCards after, string competition)
    {
        var items = new List<GenderImpactItem>();

        // (а) рекорды: любое отличие списка.
        if (!Same(before.Records, after.Records))
        {
            var added = after.Records.Where(a => !before.Records.Any(b => Same(a, b))).ToList();
            var removed = before.Records.Where(b => !after.Records.Any(a => Same(a, b))).ToList();
            var text = string.Join("; ", added.Select(r => $"появится рекорд {Disc(r)} — {r.Time} ({r.Kind})")
                .Concat(removed.Select(r => $"уйдёт из списка рекорд {Disc(r)} — {r.Time} ({r.HolderName})")));
            items.Add(new(Record, $"{competition}: {(text.Length > 0 ? text : "изменится список новых рекордов")}"));
        }

        // (в) Best swim ♂/♀.
        foreach (var (b, a, tab) in new[] { (before.BestSwimMale, after.BestSwimMale, "♂"), (before.BestSwimFemale, after.BestSwimFemale, "♀") })
            if (b?.ResultId != a?.ResultId)
                items.Add(new(BestSwim, $"{competition}: Best swim {tab} станет {a?.LastName} {a?.FirstName} — "
                    + $"{a?.Distance} {a?.StyleName} {a?.Time} (сейчас {(b is null ? "пусто" : $"{b.LastName} {b.FirstName}")})"));

        // (б) медали: списки Most decorated.
        if (!Same(before.TopMedalists, after.TopMedalists)
            || !Same(before.TopMedalistsMale, after.TopMedalistsMale)
            || !Same(before.TopMedalistsFemale, after.TopMedalistsFemale))
            items.Add(new(Medals, $"{competition}: изменится список Most decorated (медали по полу)"));

        // (в) High Point.
        if (!Same(before.HighPointAwards, after.HighPointAwards))
        {
            var entered = after.HighPointAwards
                .Where(a => !before.HighPointAwards.Any(b => b.SwimmerId == a.SwimmerId && b.Gender == a.Gender && b.AgeGroup == a.AgeGroup))
                .Select(a => $"{a.LastName} {a.FirstName} ({(a.AgeGroup.Length > 0 ? a.AgeGroup : $"возраст {a.Age}")}, {a.Points} очк.)").ToList();
            items.Add(new(HighPoint, $"{competition}: изменится High Point"
                + (entered.Count > 0 ? $" — войдёт {string.Join(", ", entered)}" : "")));
        }

        if (!Same(before.OfficialAwards, after.OfficialAwards))
            items.Add(new(SeriesAwards, $"{competition}: изменятся награды серии турниров (Official awards)"));

        // (б) клубный зачёт: общий — любое отличие; ♂/♀ — только смена порядка клубов (рост очков
        // клуба на очки дописанной строки без смены мест — закрываемая дыра, не последствие).
        if (!Same(before.TopClubs, after.TopClubs))
            items.Add(new(ClubPoints, $"{competition}: изменится общий клубный зачёт"));
        foreach (var (b, a, tab) in new[] { (before.TopClubsMen, after.TopClubsMen, "♂"), (before.TopClubsWomen, after.TopClubsWomen, "♀") })
            if (!b.Select(c => c.ClubId).SequenceEqual(a.Select(c => c.ClubId)))
                items.Add(new(ClubOrder, $"{competition}: в клубном зачёте {tab} изменится порядок или состав клубов"));

        return items;
    }

    private static string Disc(OverviewRecordDto r) => $"{r.Distance} {r.StyleName} {r.Gender} {r.AgeGroup}".Trim();

    private static bool Same<T>(T a, T b) => JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);

    /// <summary>Текст находки ручной проверки — простыми словами.</summary>
    public static string ReviewMessage(string swimmerName, int birthYear, string gender, int serious) =>
        $"{swimmerName} ({birthYear}): loglig говорит, что это {GenderWord(gender)}. "
        + $"Автоматически не проставлено — есть последствия ({serious}), проверь и реши";

    /// <summary>Перечень последствий для <c>Details</c> находки (обрезается под лимит колонки).</summary>
    public static string ReviewDetails(int logligId, IReadOnlyList<GenderImpactItem> serious, int max = 1000)
    {
        var text = $"Карточка loglig #{logligId}. Если проставить, изменится: "
                   + string.Join(" · ", serious.Select(s => s.Text));
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }
}
