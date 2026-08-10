using Swimm.Domain;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Витринная граница сезона (docs/season-boundary-rule.md): сезон кончается ПОСЛЕДНИМ
/// зимним чемпионатом, а не 31 августа. Тесты держат именно то, что легко потерять при
/// правках: будущий чемпионат границу не двигает, а без данных работает календарный фолбэк.
/// </summary>
public class ShowcaseSeasonTests
{
    [Fact]
    public void StartsTheDayAfterTheLastWinterChampionship()
    {
        var now = new DateTime(2026, 8, 9);
        var dates = new[]
        {
            new DateTime(2025, 2, 20),   // прошлый цикл
            new DateTime(2026, 2, 24),   // детский
            new DateTime(2026, 2, 26),   // юношеский — самый поздний
        };

        Assert.Equal(new DateTime(2026, 2, 27), ShowcaseSeason.StartOf(dates, now));
    }

    [Fact]
    public void FutureChampionshipDoesNotMoveTheBoundary()
    {
        // Зимний чемпионат следующего цикла уже заведён в базе — сезон он закроет
        // только когда проплывут, а не сейчас.
        var now = new DateTime(2026, 8, 9);
        var dates = new[] { new DateTime(2026, 2, 26), new DateTime(2027, 2, 25) };

        Assert.Equal(new DateTime(2026, 2, 27), ShowcaseSeason.StartOf(dates, now));
    }

    [Fact]
    public void NoWinterChampionships_FallsBackToCalendarSeason()
    {
        // Пустая витрина хуже приблизительной: без размеченных чемпионатов держим
        // календарную границу (1 сентября).
        var now = new DateTime(2026, 8, 9);

        Assert.Equal(new DateTime(2025, 9, 1), ShowcaseSeason.StartOf([], now));
    }
}
