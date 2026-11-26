using System;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Дубль страницы федерации (решение Влада 26.11.2026): строка «Входящих» с тем же LogligId, что у
/// строки уже в базе, затянута вместе с ней и закрывается сама. Живой случай — compID 1489 и 1495
/// (26.01.2024, оба loglig 9933): 1495 затянут, 1489 висел в «Ждут затягивания».
/// </summary>
public class DiscoveryLogligTwinRuleTests
{
    private static readonly DateTime Jan26 = new(2024, 1, 26);

    private static DiscoveryLogligTwinRule.Row R(int id, int compId, int? loglig, bool inDb, DateTime? start = null, DateTime? end = null) =>
        new(id, compId, loglig, start ?? Jan26, end ?? start ?? Jan26, inDb);

    [Fact]
    public void SameLoglig_AsRowInDb_IsTwin()
    {
        var twins = DiscoveryLogligTwinRule.TwinOf([R(296, 1495, 9933, inDb: true), R(297, 1489, 9933, inDb: false)]);
        Assert.Equal(1495, Assert.Single(twins).Value);
        Assert.True(twins.ContainsKey(297));
    }

    [Fact]
    public void NeitherInDb_NoTwins() =>
        Assert.Empty(DiscoveryLogligTwinRule.TwinOf([R(1, 10, 9933, false), R(2, 11, 9933, false)]));

    [Fact]
    public void BothInDb_NoTwins() =>
        Assert.Empty(DiscoveryLogligTwinRule.TwinOf([R(1, 10, 9933, true), R(2, 11, 9933, true)]));

    [Fact]
    public void NoLoglig_IsNeverTwin() =>
        Assert.Empty(DiscoveryLogligTwinRule.TwinOf([R(1, 10, null, true), R(2, 11, null, false)]));

    [Fact]
    public void DifferentLoglig_NoTwins() =>
        Assert.Empty(DiscoveryLogligTwinRule.TwinOf([R(1, 10, 9933, true), R(2, 11, 9934, false)]));

    [Fact]
    public void SeveralTwinsInDb_SmallestCompIdWins() =>
        Assert.Equal(10, DiscoveryLogligTwinRule.TwinOf([R(1, 12, 5, true), R(2, 10, 5, true), R(3, 11, 5, false)])[3]);

    [Fact]
    public void SameLoglig_DifferentDates_IsNotTwin()
    {
        // Прод 26.11.2026: «מוקדמות אליפות צעירים» части א' (24.01.2025, в базе) и ב' (31.01.2025) —
        // один loglig 11792, но это два старта: вторую часть надо тянуть.
        var twins = DiscoveryLogligTwinRule.TwinOf([
            R(1, 6622, 11792, inDb: true, start: new DateTime(2025, 1, 24)),
            R(2, 6621, 11792, inDb: false, start: new DateTime(2025, 1, 31))]);
        Assert.Empty(twins);
    }

    [Fact]
    public void MultiDay_OverlappingRange_IsTwin()
    {
        // Близнец в базе — 24–26.01, дубль — один день 25.01 внутри диапазона.
        var twins = DiscoveryLogligTwinRule.TwinOf([
            R(1, 100, 7, inDb: true, start: new DateTime(2025, 1, 24), end: new DateTime(2025, 1, 26)),
            R(2, 101, 7, inDb: false, start: new DateTime(2025, 1, 25))]);
        Assert.Equal(100, twins[2]);
    }

    [Fact]
    public void AdjacentDays_DoNotOverlap()
    {
        var twins = DiscoveryLogligTwinRule.TwinOf([
            R(1, 100, 7, inDb: true, start: new DateTime(2025, 1, 24), end: new DateTime(2025, 1, 25)),
            R(2, 101, 7, inDb: false, start: new DateTime(2025, 1, 26))]);
        Assert.Empty(twins);
    }

    [Fact]
    public void Twin_IsClosedGrey_NotDebt()
    {
        var day = new DateOnly(2024, 1, 26);
        var s = CompetitionRowStateRules.Evaluate(new CompetitionRowStateInput(
            CompetitionStage.OnSite, day, day, TwinInDb: true), new DateOnly(2026, 11, 26));
        Assert.Equal(CompetitionRowState.Closed, s.State);
        Assert.True(s.NothingToPull);
        Assert.Equal(CompetitionStateBucket.Closed, s.Bucket);
    }

    [Fact]
    public void Twin_BeforeStart_IsClosedToo()
    {
        // Близнец уже в базе — значит протокол есть; «впереди» у дубля быть не может.
        var day = new DateOnly(2026, 12, 5);
        var s = CompetitionRowStateRules.Evaluate(new CompetitionRowStateInput(
            CompetitionStage.OnSite, day, day, TwinInDb: true), new DateOnly(2026, 11, 26));
        Assert.Equal(CompetitionRowState.Closed, s.State);
    }
}
