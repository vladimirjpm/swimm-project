using System.Linq;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Services;
using Swimm.Parsing.Helpers;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Э4 плана records-relays-plan (22.09.2026): в Results «mixed» — смешанная эстафета, «none» —
/// пол неизвестен. Слепого перевода none → mixed нет; доказательство — только состав.
/// </summary>
public class RelayGenderTests
{
    [Theory]
    [InlineData("none", new[] { "male", "female", "male" }, "mixed")]
    [InlineData("", new[] { "M", "F" }, "mixed")]                  // старые M/F в карточках
    [InlineData("none", new[] { "male", "male", "male" }, "none")] // однополый при none — к loglig, не в male
    [InlineData("none", new[] { "male", null, "" }, "none")]       // состав не полный — не знаем
    [InlineData("female", new[] { "male", "female" }, "female")]   // шапка протокола главнее
    [InlineData("mixed", new string?[0], "mixed")]
    public void Resolve(string eventGender, string?[] members, string expected) =>
        Assert.Equal(expected, RelayGender.Resolve(eventGender, members));

    /// <summary>
    /// Ключ переимпорта: база уже mixed (переливка по составу), парсер снова отдаёт none — та же
    /// строка. У личных заплывов none и mixed не склеиваются: там это разные ключи.
    /// </summary>
    [Fact]
    public void MatcherKey_RelayNoneAndMixedAreOne()
    {
        Assert.Equal(ResultMatcher.KeyGender("none", isRelay: true), ResultMatcher.KeyGender("mixed", isRelay: true));
        Assert.NotEqual(ResultMatcher.KeyGender("male", isRelay: true), ResultMatcher.KeyGender("mixed", isRelay: true));
        Assert.Equal("none", ResultMatcher.KeyGender("none", isRelay: false));
    }

    [Theory]
    [InlineData("מיקס", true)]
    [InlineData("סקימ", true)]   // реверс из PDF
    [InlineData("mixed", true)]
    [InlineData("בנות", false)]
    [InlineData("מאסטרס", false)]
    public void MixToken(string token, bool expected) =>
        Assert.Equal(expected, HebrewTextHelper.IsMixToken(token));

    /// <summary>
    /// loglig-ремонт полос: «none» источника — «не знаем», а не поправка; mixed, выведенный по
    /// составу, он стирать не должен. Настоящий пол источника (female) по-прежнему приезжает.
    /// </summary>
    [Fact]
    public void BandMatcher_SourceNone_KeepsOurMixed()
    {
        var plan = LogligRelayBandMatcher.Build(
            [new RelayRowFromSource("freestyle", "4X50", "מכבי חיפה", 121_310, 1, "none", "14-15", 0)],
            [new RelayRowInDb(1, "freestyle", "4X50", "מכבי חיפה", 121_310, 1, "mixed", "14-15", "14-15", null)]);

        Assert.DoesNotContain(plan.Changes, c => c.GenderAfter != "mixed");
    }
}
