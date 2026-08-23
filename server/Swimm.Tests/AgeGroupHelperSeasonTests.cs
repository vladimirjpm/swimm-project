using Swimm.Parsing.Helpers;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Возраст и возрастная полоса на импорте считаются по СЕЗОНУ, а не по календарному году.
///
/// ⚠ Реальный случай, ради которого тесты и написаны: אליפות הרצליה, 31/10/2025. В протоколе
/// заплыв назывался «50 חזה - בנות 11-12», и плыли в нём 2014-2015 г.р. Календарный счёт давал
/// 2015 → 10 лет → полоса «9-10»: один и тот же заплыв разъезжался у нас по двум группам, а
/// места в группе считались не по протоколу (docs/season-boundary-rule.md).
/// </summary>
public class AgeGroupHelperSeasonTests
{
    [Theory]
    [InlineData("31/10/2025", 2026)]   // осень — сезон 2025/26, год окончания 2026
    [InlineData("01/11/2025", 2026)]
    [InlineData("24/12/2025", 2026)]
    [InlineData("15/02/2026", 2026)]   // весна того же сезона — тот же год
    [InlineData("31/08/2026", 2026)]   // последний день сезона
    [InlineData("01/09/2026", 2027)]   // следующий сезон (граница SeasonMath)
    public void SeasonEndYear_CountsByEndOfSeason(string date, int expected)
        => Assert.Equal(expected, AgeGroupHelper.SeasonEndYearFromDateString(date));

    [Fact]
    public void SeasonEndYear_UnparsableDate_FallsBackToCalendarYear()
    {
        // Формат не распознан — лучше прежнее поведение, чем ноль: год ещё виден в строке.
        Assert.Equal(2025, AgeGroupHelper.SeasonEndYearFromDateString("октябрь 2025"));
    }

    [Theory]
    [InlineData(2015, 11, "11-12")]    // Мия: осенью 2025 ей 11 — та же полоса, что в протоколе
    [InlineData(2014, 12, "11-12")]    // её соперница по тому же заплыву
    [InlineData(2013, 13, "13-14")]
    [InlineData(2016, 10, "9-10")]
    public void AutumnSwim_AgeAndBand_MatchProtocol(int birthYear, int expectedAge, string expectedBand)
    {
        var seasonYear = AgeGroupHelper.SeasonEndYearFromDateString("31/10/2025");
        var age = seasonYear - birthYear;

        Assert.Equal(expectedAge, age);
        Assert.Equal(expectedBand, AgeGroupHelper.GetAgeGroup(age));
    }

    [Fact]
    public void SummerSwim_UnchangedByTheRule()
    {
        // С января по август год окончания сезона равен календарному — импорт летних
        // протоколов правило не трогает вовсе.
        Assert.Equal(2026, AgeGroupHelper.SeasonEndYearFromDateString("20/07/2026"));
        Assert.Equal(11, AgeGroupHelper.SeasonEndYearFromDateString("20/07/2026") - 2015);
    }
}
