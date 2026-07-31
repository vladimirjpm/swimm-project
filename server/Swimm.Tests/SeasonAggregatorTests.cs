using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты <see cref="SeasonAggregator"/> — общий сезонный шов страниц спортсмена и клуба
/// (фаза 10.1): что попадает в best/PB, ключ дисциплины (25m/50m несравнимы) и детекция
/// личных рекордов.
/// </summary>
public class SeasonAggregatorTests
{
    private static SeasonSwimRow Row(
        long id,
        string date = "01/10/2025",
        int ms = 60_000,
        int swimmerId = 1,
        int styleId = 1,
        string distance = "100",
        string pool = "25m",
        string gender = "male",
        string? eventCategory = null,
        bool timeFail = false,
        string? suspect = null,
        bool isRelay = false) =>
        new(
            ResultId: id,
            SwimmerId: swimmerId,
            CompetitionId: 100,
            CompetitionDate: DateTime.ParseExact(date, "dd/MM/yyyy", null),
            StyleId: styleId,
            Distance: distance,
            Gender: gender,
            PoolType: pool,
            EventCategory: eventCategory,
            TimeMilliseconds: ms,
            TimeFail: timeFail,
            SuspectReason: suspect,
            IsRelay: isRelay);

    // ── Что вообще считается ────────────────────────────────────────────────

    [Fact]
    public void Countable_ExcludesDsqSuspectRelayAndNoTime()
    {
        Assert.True(SeasonAggregator.IsCountable(Row(1)));
        Assert.False(SeasonAggregator.IsCountable(Row(2, timeFail: true)));
        Assert.False(SeasonAggregator.IsCountable(Row(3, suspect: "time_outlier")));
        Assert.False(SeasonAggregator.IsCountable(Row(4, isRelay: true)));
        Assert.False(SeasonAggregator.IsCountable(Row(5) with { TimeMilliseconds = null }));
        Assert.False(SeasonAggregator.IsCountable(Row(6, ms: 0)));
    }

    [Fact]
    public void SeasonBests_IgnoresDsqAndSuspectEvenIfFaster()
    {
        var rows = new[]
        {
            Row(1, ms: 60_000),
            Row(2, ms: 55_000, timeFail: true),          // быстрее, но DSQ
            Row(3, ms: 50_000, suspect: "time_outlier"), // быстрее, но ошибка протокола
        };

        var best = SeasonAggregator.SeasonBests(rows);

        Assert.Single(best);
        Assert.Equal(1, best.Single().Value.ResultId);
    }

    // ── Ключ дисциплины ─────────────────────────────────────────────────────

    [Fact]
    public void Pool25And50_AreDifferentDisciplines()
    {
        var rows = new[] { Row(1, pool: "25m", ms: 60_000), Row(2, pool: "50m", ms: 61_000) };

        var best = SeasonAggregator.SeasonBests(rows);

        Assert.Equal(2, best.Count); // не «лучшее из двух», а два отдельных лучших
    }

    [Fact]
    public void DisciplineKey_NormalizesCaseAndDistanceSuffix()
    {
        Assert.Equal(
            SeasonAggregator.DisciplineKey(Row(1, distance: "100", pool: "25m", gender: "male")),
            SeasonAggregator.DisciplineKey(Row(2, distance: "100m", pool: "25M", gender: "Male")));
    }

    [Fact]
    public void EventCategory_SplitsDisciplineOnlyWhenAsked()
    {
        var open = Row(1, eventCategory: "open", ms: 60_000);
        var para = Row(2, eventCategory: "para", ms: 70_000);

        // Клубный зачёт: EventCategory сознательно не учитывается — одна дисциплина.
        Assert.Single(SeasonAggregator.SeasonBests([open, para]));
        // Страница спортсмена: три золота Маккабиады не должны слиться.
        Assert.Equal(2, SeasonAggregator.SeasonBests([open, para], includeEventCategory: true).Count);
    }

    // ── Сезоны ──────────────────────────────────────────────────────────────

    [Fact]
    public void SeasonBests_SplitsBySeasonAcrossBoundary()
    {
        var rows = new[]
        {
            Row(1, date: "31/08/2025", ms: 58_000), // сезон 2024/25
            Row(2, date: "01/09/2025", ms: 62_000), // сезон 2025/26
        };

        var best = SeasonAggregator.SeasonBests(rows);

        Assert.Equal(2, best.Count);
        Assert.Contains(best, kv => kv.Key.Season == 2024 && kv.Value.ResultId == 1);
        Assert.Contains(best, kv => kv.Key.Season == 2025 && kv.Value.ResultId == 2);
    }

    [Fact]
    public void SeasonsPresent_NewestFirstAndDistinct()
    {
        var rows = new[]
        {
            Row(1, date: "10/10/2023"),
            Row(2, date: "10/02/2026"),
            Row(3, date: "11/02/2026"),
        };

        Assert.Equal([2025, 2023], SeasonAggregator.SeasonsPresent(rows));
    }

    // ── Личные рекорды ──────────────────────────────────────────────────────

    [Fact]
    public void FirstSwimInDiscipline_IsAlwaysPb()
    {
        var pbs = SeasonAggregator.PersonalBests([Row(1, ms: 90_000)]);

        Assert.Equal([1L], pbs);
    }

    [Fact]
    public void Pb_RequiresStrictlyFaster_AndCrossesSeasons()
    {
        var rows = new[]
        {
            Row(1, date: "01/10/2024", ms: 60_000), // первый — PB
            Row(2, date: "01/12/2024", ms: 60_000), // повтор времени — НЕ PB
            Row(3, date: "01/02/2025", ms: 61_000), // хуже — не PB
            Row(4, date: "01/10/2025", ms: 59_500), // новый сезон, быстрее прошлогоднего — PB
        };

        Assert.Equal([1L, 4L], SeasonAggregator.PersonalBests(rows).OrderBy(x => x));
    }

    [Fact]
    public void Pb_IsPerSwimmerAndPerDiscipline()
    {
        var rows = new[]
        {
            Row(1, swimmerId: 1, ms: 60_000),
            Row(2, swimmerId: 2, ms: 70_000),                  // другой пловец — свой первый PB
            Row(3, swimmerId: 1, ms: 65_000, distance: "200"), // другая дистанция — свой первый PB
            Row(4, swimmerId: 1, ms: 61_000),                  // хуже своего же — не PB
        };

        Assert.Equal([1L, 2L, 3L], SeasonAggregator.PersonalBests(rows).OrderBy(x => x));
    }

    [Fact]
    public void Pb_OrdersByDateNotByInputOrder()
    {
        var rows = new[]
        {
            Row(9, date: "01/03/2026", ms: 58_000), // поздний, быстрый
            Row(1, date: "01/10/2025", ms: 60_000), // ранний, медленнее
        };

        // Оба — PB на свой момент: сначала 60.0 (первый), затем 58.0 (улучшение).
        Assert.Equal([1L, 9L], SeasonAggregator.PersonalBests(rows).OrderBy(x => x));
    }
}
