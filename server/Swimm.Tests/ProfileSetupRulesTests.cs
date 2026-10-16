using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Шаги и счётчик «Set up profile» (docs/plans/profile-setup-plan.md, решения Влада 16.10.2026):
/// три фиксированных шага, заявка в группу засчитана, Dismiss прячет пункт.
/// </summary>
public class ProfileSetupRulesTests
{
    [Fact]
    public void NewUser_ZeroOfThree_Visible()
    {
        var s = ProfileSetupRules.Evaluate(new ProfileSetupFacts(false, false, false), dismissed: false);

        Assert.Equal(0, s.Done);
        Assert.Equal(3, s.Total);
        Assert.True(s.Visible);
    }

    [Fact]
    public void StepsAreFixed_InOrder()
    {
        var s = ProfileSetupRules.Evaluate(new ProfileSetupFacts(false, false, false), dismissed: false);

        Assert.Equal(
            new[] { ProfileSetupRules.StepMe, ProfileSetupRules.StepClub, ProfileSetupRules.StepGroup },
            s.Steps.Select(x => x.Key));
    }

    [Theory]
    [InlineData(true, false, false, ProfileSetupRules.StepMe)]
    [InlineData(false, true, false, ProfileSetupRules.StepClub)]
    [InlineData(false, false, true, ProfileSetupRules.StepGroup)]   // в т.ч. pending — его считает репозиторий
    public void EachFact_MarksOnlyItsStep(bool me, bool club, bool group, string doneKey)
    {
        var s = ProfileSetupRules.Evaluate(new ProfileSetupFacts(me, club, group), dismissed: false);

        Assert.Equal(1, s.Done);
        Assert.All(s.Steps, x => Assert.Equal(x.Key == doneKey, x.Done));
        Assert.True(s.Visible);
    }

    [Fact]
    public void AllDone_Hidden()
    {
        var s = ProfileSetupRules.Evaluate(new ProfileSetupFacts(true, true, true), dismissed: false);

        Assert.Equal(3, s.Done);
        Assert.False(s.Visible);
    }

    [Theory]
    [InlineData(false, false, false, false, true)]   // снял всё — тоже ответ
    [InlineData(false, false, false, true, true)]    // только watching
    [InlineData(true, true, true, false, true)]      // любые роли без watching
    [InlineData(true, false, false, true, false)]    // watching исключает остальные
    [InlineData(false, true, false, true, false)]
    [InlineData(false, false, true, true, false)]
    public void Survey_WatcherIsExclusive(bool swimmer, bool parent, bool coach, bool watcher, bool valid) =>
        Assert.Equal(valid, ProfileSetupRules.IsValidSurvey(swimmer, parent, coach, watcher));

    [Fact]
    public void Skip_CountsAsDone_AndIsMarkedSkipped()
    {
        // Тренер: клуб отмечен, от «это я» и группы отказался — профиль завершён.
        var s = ProfileSetupRules.Evaluate(
            new ProfileSetupFacts(HasMe: false, HasClub: true, InGroup: false),
            new ProfileSetupSkips(Me: true, Club: false, Group: true), dismissed: false);

        Assert.Equal(3, s.Done);
        Assert.False(s.Visible);
        Assert.True(s.Steps.Single(x => x.Key == ProfileSetupRules.StepMe).Skipped);
        Assert.False(s.Steps.Single(x => x.Key == ProfileSetupRules.StepClub).Skipped);
        Assert.True(s.Steps.Single(x => x.Key == ProfileSetupRules.StepGroup).Skipped);
    }

    [Fact]
    public void RealStep_BeatsSkip()
    {
        // Отказался от группы, потом всё же вступил — шаг сделан делом, не отказом.
        var s = ProfileSetupRules.Evaluate(
            new ProfileSetupFacts(false, false, InGroup: true),
            new ProfileSetupSkips(false, false, Group: true), dismissed: false);

        var g = s.Steps.Single(x => x.Key == ProfileSetupRules.StepGroup);
        Assert.True(g.Done);
        Assert.False(g.Skipped);
        Assert.Equal(1, s.Done);
    }

    [Theory]
    [InlineData("me", true)]
    [InlineData("club", true)]
    [InlineData("group", true)]
    [InlineData("child", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void StepKey_OnlyThree(string? key, bool valid) =>
        Assert.Equal(valid, ProfileSetupRules.IsStepKey(key));

    [Fact]
    public void Dismissed_HiddenEvenWhenUnfinished()
    {
        var s = ProfileSetupRules.Evaluate(new ProfileSetupFacts(true, false, false), dismissed: true);

        Assert.Equal(1, s.Done);   // счётчик честный — странице он ещё нужен
        Assert.False(s.Visible);
    }
}
