using System.Linq;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Статусы девяти шагов чек-листа правой панели /Admin/Competitions (вариант E, хэндофф 3.5).
/// </summary>
public class CompetitionChecklistRulesTests
{
    private const ChecklistStepStatus P = ChecklistStepStatus.Pending, C = ChecklistStepStatus.Current,
        D = ChecklistStepStatus.Done, A = ChecklistStepStatus.Attention;

    private static ChecklistStepStatus[] Build(ChecklistInput i, ChecklistPhase phase = ChecklistPhase.None) =>
        CompetitionChecklistRules.Build(i, phase).ToArray();

    private static readonly ChecklistInput Ready = new(CompetitionRowState.ReadyToPull,
        StartList: StartListBadgeKind.Ok, WarmUpDays: 1);

    [Fact]
    public void ReadyToPull_BeforePull_Step5IsCurrent_RestPending() =>
        Assert.Equal(new[] { D, D, D, D, C, P, P, P, P }, Build(Ready));

    [Fact]
    public void NoStartList_AndNoWarmUp_AreAttention()
    {
        var s = Build(new ChecklistInput(CompetitionRowState.ReadyToPull));
        Assert.Equal(A, s[1]);
        Assert.Equal(A, s[2]);
    }

    [Theory]
    [InlineData(StartListBadgeKind.Partial, ChecklistStepStatus.Done)]
    [InlineData(StartListBadgeKind.Empty, ChecklistStepStatus.Attention)]
    [InlineData(StartListBadgeKind.Error, ChecklistStepStatus.Attention)]
    public void StartListKind_MapsToStep2(StartListBadgeKind kind, ChecklistStepStatus expected) =>
        Assert.Equal(expected, Build(Ready with { StartList = kind })[1]);

    [Fact]
    public void StaleStartList_MattersOnlyBeforeStart()
    {
        Assert.Equal(D, Build(Ready with { StartListStale = true })[1]);
        Assert.Equal(A, Build(Ready with { State = CompetitionRowState.Ahead, StartListStale = true })[1]);
    }

    [Fact]
    public void WarmUp_PartOfDays_IsAttention() =>
        Assert.Equal(A, Build(Ready with { DayCount = 3, WarmUpDays = 2 })[2]);

    [Fact]
    public void Ahead_Step4Current_TailPending() =>
        Assert.Equal(new[] { C, P, P, P, P, P }, Build(Ready with { State = CompetitionRowState.Ahead })[3..]);

    [Theory]
    [InlineData(ChecklistPhase.Parsing, new[] { C, P, P, P, P })]
    [InlineData(ChecklistPhase.ParseFailed, new[] { A, P, P, P, P })]
    [InlineData(ChecklistPhase.Preview, new[] { D, C, P, P, P })]
    [InlineData(ChecklistPhase.PreviewExpired, new[] { D, A, P, P, P })]
    [InlineData(ChecklistPhase.Importing, new[] { D, D, C, P, P })]
    [InlineData(ChecklistPhase.ImportFailed, new[] { D, D, A, P, P })]
    public void Phase_DrivesSteps5To9(ChecklistPhase phase, ChecklistStepStatus[] tail) =>
        Assert.Equal(tail, Build(Ready, phase)[4..]);

    [Fact]
    public void Phase_DoesNotTouchSteps1To4() =>
        Assert.Equal(Build(Ready)[..4], Build(Ready, ChecklistPhase.Importing)[..4]);

    [Theory]
    [InlineData(0, false, ChecklistStepStatus.Current)]
    [InlineData(0, true, ChecklistStepStatus.Done)]
    [InlineData(3, true, ChecklistStepStatus.Attention)]
    public void Imported_QualityStep(int suspects, bool scanned, ChecklistStepStatus expected)
    {
        var s = Build(Ready with { State = CompetitionRowState.Closed, Imported = true, SuspectCount = suspects, QualityScanned = scanned });
        Assert.Equal(new[] { D, D, D }, s[4..7]);
        Assert.Equal(expected, s[7]);
        Assert.Equal(C, s[8]);
    }

    [Fact]
    public void WaitingProtocol_Step5Attention() =>
        Assert.Equal(A, Build(Ready with { State = CompetitionRowState.WaitingProtocol })[4]);

    [Fact]
    public void Codes_AreStable()
    {
        Assert.Equal("current", CompetitionChecklistRules.Code(C));
        Assert.Equal("import-failed", CompetitionChecklistRules.Code(ChecklistPhase.ImportFailed));
        Assert.Equal(6, CompetitionChecklistRules.LivePhases.Count);
    }
}
