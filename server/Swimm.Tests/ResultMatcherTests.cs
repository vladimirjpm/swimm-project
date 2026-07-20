using System.Linq;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Матчер результатов upsert-импорта (docs/plans/import-upsert-plan.md, Р2) — чистая функция,
/// без доступа к БД.
/// </summary>
public class ResultMatcherTests
{
    private static ResultRecord Old(int id, int compId = 1, int styleId = 1, string distance = "50",
        string gender = "male", int heat = 1, int lane = 1, int? relayId = null) => new()
    {
        Id = id,
        CompetitionId = compId,
        StyleId = styleId,
        Distance = distance,
        Gender = gender,
        Heat = heat,
        Lane = lane,
        RelayId = relayId
    };

    private static ResultRecord New(int compId = 1, int styleId = 1, string distance = "50",
        string gender = "male", int heat = 1, int lane = 1, bool isRelay = false, string? note = null) => new()
    {
        CompetitionId = compId,
        StyleId = styleId,
        Distance = distance,
        Gender = gender,
        Heat = heat,
        Lane = lane,
        Relay = isRelay ? new Relay() : null,
        Note = note
    };

    private static ResultMatch<ResultRecord, ResultRecord> RunMatch(
        IReadOnlyList<ResultRecord> oldRows, IReadOnlyList<ResultRecord> newRows) =>
        ResultMatcher.Match(oldRows, newRows, ResultMatcher.KeyOfPersisted, ResultMatcher.KeyOfTransient);

    [Fact]
    public void SameKey_Matches()
    {
        var old = Old(1);
        var @new = New(note: "updated");

        var result = RunMatch([old], [@new]);

        Assert.Single(result.Matched);
        Assert.Equal(old, result.Matched[0].Old);
        Assert.Equal(@new, result.Matched[0].New);
        Assert.Empty(result.Inserted);
        Assert.Empty(result.Deleted);
    }

    [Fact]
    public void NewRowNotInOld_IsInserted()
    {
        var old = Old(1, lane: 1);
        var @new = New(lane: 2);

        var result = RunMatch([old], [@new]);

        Assert.Empty(result.Matched);
        Assert.Single(result.Inserted);
        Assert.Single(result.Deleted);
        Assert.Equal(old, result.Deleted[0]);
    }

    [Fact]
    public void OldRowNotInNew_IsDeleted()
    {
        var old = Old(1);

        var result = RunMatch([old], []);

        Assert.Empty(result.Matched);
        Assert.Empty(result.Inserted);
        Assert.Single(result.Deleted);
        Assert.Equal(old, result.Deleted[0]);
    }

    [Fact]
    public void DifferentCompetition_NotMatched()
    {
        var old = Old(1, compId: 1);
        var @new = New(compId: 2);

        var result = RunMatch([old], [@new]);

        Assert.Empty(result.Matched);
        Assert.Single(result.Inserted);
        Assert.Single(result.Deleted);
    }

    [Fact]
    public void DifferentHeatOrLane_NotMatched()
    {
        var old1 = Old(1, heat: 1, lane: 1);
        var new1 = New(heat: 2, lane: 1);
        var result1 = RunMatch([old1], [new1]);
        Assert.Empty(result1.Matched);

        var old2 = Old(2, heat: 1, lane: 1);
        var new2 = New(heat: 1, lane: 2);
        var result2 = RunMatch([old2], [new2]);
        Assert.Empty(result2.Matched);
    }

    [Fact]
    public void RelayVsIndividual_SameSwimLane_NotMatched()
    {
        // Одна и та же дорожка/заплыв, но один результат индивидуальный, другой — эстафетный:
        // это разные физические заплывы (IsRelay входит в ключ).
        var old = Old(1, relayId: null);
        var @new = New(isRelay: true);

        var result = RunMatch([old], [@new]);

        Assert.Empty(result.Matched);
        Assert.Single(result.Inserted);
        Assert.Single(result.Deleted);
    }

    [Fact]
    public void RelayVsRelay_SameKey_Matches()
    {
        var old = Old(1, relayId: 42);
        var @new = New(isRelay: true);

        var result = RunMatch([old], [@new]);

        Assert.Single(result.Matched);
    }

    [Fact]
    public void KeyCollision_TwoOldTwoNew_MatchesInEncounterOrder()
    {
        // Два старых результата с одинаковым ключом (в реальности не встречается, но формат
        // не запрещает) — матчатся к новым в порядке следования (FIFO), не по значению полей.
        var old1 = Old(1, lane: 5);
        var old2 = Old(2, lane: 5);
        var new1 = New(lane: 5, note: "first-in-file");
        var new2 = New(lane: 5, note: "second-in-file");

        var result = RunMatch([old1, old2], [new1, new2]);

        Assert.Equal(2, result.Matched.Count);
        Assert.Equal(old1, result.Matched[0].Old);
        Assert.Equal(new1, result.Matched[0].New);
        Assert.Equal(old2, result.Matched[1].Old);
        Assert.Equal(new2, result.Matched[1].New);
        Assert.Empty(result.Inserted);
        Assert.Empty(result.Deleted);
    }

    [Fact]
    public void KeyCollision_MoreOldThanNew_ExcessOldBecomesDeleted()
    {
        var old1 = Old(1, lane: 5);
        var old2 = Old(2, lane: 5);
        var old3 = Old(3, lane: 5);
        var new1 = New(lane: 5);

        var result = RunMatch([old1, old2, old3], [new1]);

        Assert.Single(result.Matched);
        Assert.Equal(old1, result.Matched[0].Old);
        Assert.Empty(result.Inserted);
        Assert.Equal(2, result.Deleted.Count);
        Assert.Contains(old2, result.Deleted);
        Assert.Contains(old3, result.Deleted);
    }

    [Fact]
    public void KeyCollision_MoreNewThanOld_ExcessNewBecomesInserted()
    {
        var old1 = Old(1, lane: 5);
        var new1 = New(lane: 5);
        var new2 = New(lane: 5);
        var new3 = New(lane: 5);

        var result = RunMatch([old1], [new1, new2, new3]);

        Assert.Single(result.Matched);
        Assert.Equal(new1, result.Matched[0].New);
        Assert.Equal(2, result.Inserted.Count);
        Assert.Contains(new2, result.Inserted);
        Assert.Contains(new3, result.Inserted);
        Assert.Empty(result.Deleted);
    }

    [Fact]
    public void MultipleCompetitionsInOneFile_MatchedPerCompetitionIndependently()
    {
        // Многодневный файл: один и тот же lane/heat/style в разных Competition.Id — не должны
        // перепутаться между собой (footgun из плана: матчер работает per-Competition).
        var oldDay1 = Old(1, compId: 10, lane: 1);
        var oldDay2 = Old(2, compId: 20, lane: 1);
        var newDay1 = New(compId: 10, lane: 1, note: "day1");
        var newDay2 = New(compId: 20, lane: 1, note: "day2");

        var result = RunMatch([oldDay1, oldDay2], [newDay1, newDay2]);

        Assert.Equal(2, result.Matched.Count);
        var day1Match = result.Matched.Single(m => m.Old.Id == 1);
        Assert.Equal("day1", day1Match.New.Note);
        var day2Match = result.Matched.Single(m => m.Old.Id == 2);
        Assert.Equal("day2", day2Match.New.Note);
    }

    [Fact]
    public void EmptyOldAndNew_NoOp()
    {
        var result = RunMatch([], []);
        Assert.Empty(result.Matched);
        Assert.Empty(result.Inserted);
        Assert.Empty(result.Deleted);
    }
}
