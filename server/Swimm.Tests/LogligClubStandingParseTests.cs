using Swimm.Domain.Entities;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Разбор официального клубного зачёта loglig и подбор правила под его шкалу.
/// Снапшоты живых страниц от 2026-08-16 лежат в Fixtures/Loglig — в сеть тесты НЕ ходят.
///
/// Главное, что здесь зафиксировано: кнопка «דירוג מועדונים» есть у ВСЕХ соревнований, а
/// зачёт — нет. Признак один: непустая таблица ajax (docs/points-rules-per-competition-plan.md §10.1).
/// </summary>
public class LogligClubStandingParseTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Loglig", name));

    // ── есть зачёт / нет зачёта ───────────────────────────────────────────────

    [Fact]
    public void HasStandingRows_TrueForPublishedStanding()
    {
        // Маккаби-2026 (loglig 14668): зачёт опубликован, 25 клубов в трёх таблицах.
        Assert.True(LogligClient.HasStandingRows(Fixture("loglig-clubstanding-14668.html")));
    }

    [Fact]
    public void HasStandingRows_FalseForEmptyResponse()
    {
        // Региональный этап лета-2025 (loglig 12744): кнопка на странице есть, зачёта нет.
        Assert.False(LogligClient.HasStandingRows(Fixture("loglig-clubstanding-empty-12744.html")));
    }

    // ── шкала из заплыва ──────────────────────────────────────────────────────

    [Fact]
    public void ParseEventClubPoints_ReadsPlaceToPoints()
    {
        var rows = LogligClient.ParseEventClubPoints(Fixture("loglig-event-82997-individual.html"));
        var byPlace = rows.GroupBy(r => r.Place).ToDictionary(g => g.Key, g => g.First().Points);

        // Шкала лета-2026 из регламента (doc 3341): 40,34,30,27,25… с «дырой» между 16 и 17.
        Assert.Equal(40, byPlace[1]);
        Assert.Equal(34, byPlace[2]);
        Assert.Equal(30, byPlace[3]);
        Assert.Equal(11, byPlace[16]);
        Assert.Equal(8, byPlace[17]);
        Assert.Equal(1, byPlace[24]);
    }

    [Fact]
    public void ParseEventClubPoints_MastersRow_TakesColumnByHeader_NotLastCell()
    {
        // Мастерс Маккабиады: у иностранных команд клубных очков нет, строка короче — «последняя
        // ячейка» там оказывалась очками FINA, и шкала выходила мусорной (12,11,10,9,8,7,6,0,7,0…).
        var rows = LogligClient.ParseEventClubPoints(Fixture("loglig-event-82527-masters.html"));
        var byPlace = rows.GroupBy(r => r.Place).ToDictionary(g => g.Key, g => g.First().Points);

        Assert.Equal(12, byPlace[1]);
        Assert.Equal(11, byPlace[2]);
        Assert.Equal(10, byPlace[3]);
        Assert.DoesNotContain(rows, r => r.Points > 12); // очки FINA (сотни) сюда не попадают
    }

    [Fact]
    public void IsRelayEventPage_TrueForRelay_FalseForIndividual()
    {
        // Эстафеты идут с двойными очками — попав в выборку, они удвоили бы всю шкалу.
        Assert.True(LogligClient.IsRelayEventPage(Fixture("loglig-event-82537-relay.html")));
        Assert.False(LogligClient.IsRelayEventPage(Fixture("loglig-event-82997-individual.html")));
    }

    // ── страница соревнования ─────────────────────────────────────────────────

    [Fact]
    public void ParseSeasonId_TakenFromPage_NotConfig()
    {
        // У прошлых сезонов seasonId свой, и с чужим таблица приходит пустой —
        // «зачёта нет» стало бы ложным выводом.
        Assert.Equal(1715, LogligClient.ParseSeasonId(Fixture("loglig-disciplines-14668.html")));
    }

    [Fact]
    public void ParseEventIds_ReturnsDistinctIds()
    {
        var ids = LogligClient.ParseEventIds(Fixture("loglig-disciplines-14668.html"));

        Assert.NotEmpty(ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.True(id > 0));
    }

    // ── подбор правила по шкале ───────────────────────────────────────────────

    private static PointRuleClubs Rule(int id, string version, int? maxPlace, params int[] points) => new()
    {
        Id = id,
        Version = version,
        Scope = "all",
        MaxScoringPlace = maxPlace,
        Entries = points.Select((p, i) => new PointRuleClubsEntry { Place = i + 1, Points = p }).ToList()
    };

    private static readonly PointRuleClubs Federal2025 =
        Rule(1, "30pt.24pl.2025.01", 24, 30, 28, 26, 24, 23, 22, 21, 20, 19, 18, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 5, 3, 2, 1);

    private static readonly PointRuleClubs Bogrim =
        Rule(4, "25pt.20pl.2026.01", 20, 25, 22, 20, 18, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1);

    private static readonly PointRuleClubs Maccabi =
        Rule(5, "maccabi.25pt.20pl.2026", 20, 25, 22, 20, 18, 17, 16, 15, 14, 13, 12, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1);

    private static PointRuleClubs[] All => [Federal2025, Bogrim, Maccabi];

    [Fact]
    public void Match_PicksRuleWithExactScale()
    {
        var observed = new Dictionary<int, int> { [1] = 25, [2] = 22, [3] = 20, [4] = 18, [5] = 16, [11] = 10 };

        Assert.Equal(4, PointRuleScaleMatcher.Match(observed, All)?.Id);
    }

    [Fact]
    public void Match_DistinguishesNearIdenticalScales()
    {
        // Маккаби отличается от федеральной шкалы бугрим только местами 5–11 (+1).
        var observed = new Dictionary<int, int> { [1] = 25, [2] = 22, [3] = 20, [4] = 18, [5] = 17, [11] = 10 };

        Assert.Equal(5, PointRuleScaleMatcher.Match(observed, All)?.Id);
    }

    [Fact]
    public void Match_NullWhenTailDiffers()
    {
        // Ровно случай лета-2025: первые двадцать мест совпадают, а хвост — нет.
        // «Почти подходит» здесь хуже, чем ничего: −3 очка на полосу всплывут только на сверке.
        var observed = new Dictionary<int, int> { [1] = 30, [2] = 28, [3] = 26, [4] = 24, [21] = 6, [22] = 5 };

        Assert.Null(PointRuleScaleMatcher.Match(observed, All));
    }

    [Fact]
    public void Match_NullWhenTooFewPlacesObserved()
    {
        var observed = new Dictionary<int, int> { [1] = 25, [2] = 22, [3] = 20 };

        Assert.Null(PointRuleScaleMatcher.Match(observed, All));
    }

    [Fact]
    public void Match_NullForUnknownScale()
    {
        // Шкала «Хапоэля» до того, как её завели правилом.
        var observed = new Dictionary<int, int> { [1] = 30, [2] = 26, [3] = 23, [4] = 20, [5] = 18, [11] = 10 };

        Assert.Null(PointRuleScaleMatcher.Match(observed, All));
    }

    [Fact]
    public void Match_RealFixtureScale_FindsSummer2026Rule()
    {
        var summer2026 = Rule(3, "40pt.24pl.2026.01", 24,
            40, 34, 30, 27, 25, 24, 23, 22, 18, 17, 16, 15, 14, 13, 12, 11, 8, 7, 6, 5, 4, 3, 2, 1);

        var observed = LogligClient.ParseEventClubPoints(Fixture("loglig-event-82997-individual.html"))
            .GroupBy(r => r.Place)
            .ToDictionary(g => g.Key, g => g.First().Points);

        Assert.Equal(3, PointRuleScaleMatcher.Match(observed, [summer2026, .. All])?.Id);
    }
}
