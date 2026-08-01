using Swimm.Application.Constants;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты <see cref="StandingKinds"/> — роль соревнования в клубном зачёте.
/// Правило Влада: у возрастной группы за сезон два чемпионата — зимний в 25м и летний в 50м.
/// Проверено на всех 23 чемпионатах в БД (2026-08-01), исключений нет; поэтому роль
/// выводится автоматически, а не проставляется руками.
/// </summary>
public class StandingKindsTests
{
    [Theory]
    [InlineData(true, "25m", StandingKinds.Winter)]
    [InlineData(true, "50m", StandingKinds.Summer)]
    public void Championship_KindComesFromPool(bool isChampionship, string pool, string expected)
    {
        Assert.Equal(expected, StandingKinds.Resolve(isChampionship, pool, null));
    }

    [Theory]
    [InlineData("25m")]
    [InlineData("50m")]
    public void NotChampionship_IsNotAStanding(string pool)
    {
        // Обычный старт в истории клуба виден, но зачёта не образует.
        Assert.Null(StandingKinds.Resolve(false, pool, null));
    }

    [Fact]
    public void UnknownPool_GivesNoKind()
    {
        Assert.Null(StandingKinds.Resolve(true, "", null));
        Assert.Null(StandingKinds.Resolve(true, null, null));
    }

    [Fact]
    public void Override_BeatsDerivedValue()
    {
        // Открытая вода — ради неё поле и заведено: бассейна там нет.
        Assert.Equal(StandingKinds.OpenWater,
            StandingKinds.Resolve(isChampionship: false, "25m", StandingKinds.OpenWater));
        Assert.Equal(StandingKinds.Summer,
            StandingKinds.Resolve(isChampionship: true, "25m", StandingKinds.Summer));
    }

    [Fact]
    public void OverrideNone_ExcludesChampionshipFromStandings()
    {
        Assert.Null(StandingKinds.Resolve(isChampionship: true, "25m", StandingKinds.None));
    }

    [Fact]
    public void GarbageOverride_FallsBackToNoKind()
    {
        // Мусор в поле не должен молча превращаться в «зимний зачёт».
        Assert.Null(StandingKinds.Resolve(isChampionship: true, "25m", "winter-ish"));
        Assert.False(StandingKinds.IsValidOverride("winter-ish"));
        Assert.True(StandingKinds.IsValidOverride(null));
        Assert.True(StandingKinds.IsValidOverride(StandingKinds.None));
    }
}
