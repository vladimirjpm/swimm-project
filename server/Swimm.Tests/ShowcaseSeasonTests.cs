using Swimm.Domain;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Витринный сезон (docs/season-boundary-rule.md, уточнено 2026-08-13): витрина переключается
/// на новый сезон только ПОСЛЕ последнего зимнего чемпионата, до этого держит прошлый.
/// Тесты держат именно то, что легко потерять при правках: сезон отдаётся ЦЕЛИКОМ (а не
/// «с даты чемпионата»), ступени закрывают сезон самой поздней датой, будущий чемпионат
/// границу не двигает, без данных работает календарный фолбэк.
/// </summary>
public class ShowcaseSeasonTests
{
    /// <summary>Реальный календарь 2025/26: мастерс 10 января, возрастные 13–26 февраля.</summary>
    private static readonly DateTime[] Season2025 =
    [
        new(2026, 1, 10), new(2026, 2, 13), new(2026, 2, 26),
    ];

    /// <summary>Прошлый цикл 2024/25.</summary>
    private static readonly DateTime[] Season2024 = [new(2025, 2, 19), new(2025, 2, 21)];

    [Fact]
    public void AfterWinterChampionships_ShowcaseIsTheCurrentSeason()
    {
        var now = new DateTime(2026, 8, 9);
        var dates = Season2024.Concat(Season2025);

        Assert.Equal(2025, ShowcaseSeason.StartYearOf(dates, now));
    }

    [Fact]
    public void BeforeWinterChampionships_ShowcaseHoldsThePreviousSeason()
    {
        // Декабрь: сезон 2025/26 уже идёт и его старты лежат в нём календарно, но зимние
        // чемпионаты (февраль 2026) ещё не проплыли — витрина показывает 2024/25.
        var now = new DateTime(2025, 12, 15);
        var dates = Season2024.Concat(Season2025);

        Assert.Equal(2024, ShowcaseSeason.StartYearOf(dates, now));
    }

    [Fact]
    public void LastStepClosesTheSeason_NotTheFirstOne()
    {
        // Мастерс проплыли 10 января, возрастные — только 26 февраля. Между этими датами
        // сезон ещё не закрыт: «последний зимний» считается по ВСЕМ ступеням сразу.
        var dates = Season2024.Concat(Season2025);

        Assert.Equal(2024, ShowcaseSeason.StartYearOf(dates, new DateTime(2026, 1, 20)));
        Assert.Equal(2025, ShowcaseSeason.StartYearOf(dates, new DateTime(2026, 2, 27)));
    }

    [Fact]
    public void FutureChampionshipDoesNotMoveTheBoundary()
    {
        // Зимний чемпионат следующего цикла уже заведён в базе — витрину он переключит,
        // только когда проплывут.
        var now = new DateTime(2026, 8, 9);
        var dates = Season2025.Concat([new DateTime(2027, 2, 25)]);

        Assert.Equal(2025, ShowcaseSeason.StartYearOf(dates, now));
    }

    [Fact]
    public void NoWinterChampionships_FallsBackToCalendarSeason()
    {
        // Прятать свежие данные, потому что нечем подтвердить границу, хуже, чем показать их.
        var now = new DateTime(2026, 8, 9);

        Assert.Equal(2025, ShowcaseSeason.StartYearOf([], now));
    }

    [Fact]
    public void Range_IsTheWholeSeason_NotFromTheChampionshipDate()
    {
        // Главная правка 2026-08-13: зимний чемпионат ЗАКРЫВАЕТ сезон, а не отрезает его
        // начало. Декабрьские и февральские старты обязаны попадать в витрину.
        var (start, endExclusive) = ShowcaseSeason.RangeOf(
            Season2024.Concat(Season2025), new DateTime(2026, 8, 9));

        Assert.Equal(new DateTime(2025, 9, 1), start);
        Assert.Equal(new DateTime(2026, 9, 1), endExclusive);
        Assert.True(new DateTime(2025, 12, 10) >= start && new DateTime(2025, 12, 10) < endExclusive);
        Assert.True(new DateTime(2026, 2, 16) >= start && new DateTime(2026, 2, 16) < endExclusive);
    }
}
