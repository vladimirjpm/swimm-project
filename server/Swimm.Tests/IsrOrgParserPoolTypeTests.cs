using System.Collections.Generic;
using System.Linq;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Регресс: бассейн, выбранный в UI импорта (poolType), должен побеждать дефолт парсера.
/// Раньше IsrOrgParser писал захардкоженный comp.PoolType ("25m"), игнорируя выбор 50m.
/// </summary>
public class IsrOrgParserPoolTypeTests
{
    private static IsrOrgCompetitionResult SampleComp(string poolType) => new(
        Competition: "Maccabiah 2026",
        AgeGroup: "",
        Date: "05/07/2026",
        Event: "50m Freestyle - U17 Girls",
        EventStyleName: "freestyle",
        EventStyleLen: "50",
        EventStyleGender: "female",
        EventStyleAge: "17",
        PoolType: poolType,
        Results: new List<IsrOrgResult>
        {
            new(
                Country: "", Position: 1, Heat: 3, Lane: 4,
                LastName: "Goltsov", FirstName: "Eva", BirthYear: 2009,
                Club: "Israel", Time: "00:26.89", TimeFailNote: null,
                InternationalPoints: 676, IsRelay: false,
                RelayTeamName: null, RelaySwimmersName: null, RelaySwimmers: null)
        });

    [Fact]
    public void PoolOverride_FromUi_WinsOverParserDefault()
    {
        var comps = new[] { SampleComp("25m") };

        var mapped = IsrOrgParser.MapHebrewOnly(comps, country: "IL",
            isMastersFile: false, isAward: false, poolOverride: "50m").ToList();

        var r = Assert.Single(mapped);
        Assert.Equal("50m", r.PoolType);   // выбор из UI, а не захардкоженный "25m"
        Assert.Equal("Goltsov", r.LastName);
    }

    [Fact]
    public void NoPoolOverride_FallsBackToParserDefault()
    {
        var comps = new[] { SampleComp("25m") };

        var mapped = IsrOrgParser.MapHebrewOnly(comps, country: "IL",
            isMastersFile: false, isAward: false, poolOverride: null).ToList();

        Assert.Equal("25m", Assert.Single(mapped).PoolType);
    }
}
