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
    [InlineData("family", true)]
    [InlineData("child", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void StepKey_Known(string? key, bool valid) =>
        Assert.Equal(valid, ProfileSetupRules.IsStepKey(key));

    // ── Шаг семьи у родителя (хендофф set-up-profile-ex3, решение Влада 17.10.2026) ───────

    [Fact]
    public void Parent_GetsFamilyStep_AfterMe()
    {
        var s = ProfileSetupRules.Evaluate(new ProfileSetupFacts(false, false, false), ProfileSetupSkips.None,
            dismissed: false, parent: true);

        Assert.Equal(
            new[] { ProfileSetupRules.StepMe, ProfileSetupRules.StepFamily, ProfileSetupRules.StepClub, ProfileSetupRules.StepGroup },
            s.Steps.Select(x => x.Key));
        Assert.Equal(4, s.Total);
    }

    [Fact]
    public void NotParent_NoFamilyStep_EvenWithFamilySaved()
    {
        // Роль сняли — шаг пропал, сохранённая семья ни на что не влияет.
        var s = ProfileSetupRules.Evaluate(new ProfileSetupFacts(true, true, true, HasFamily: true), dismissed: false);

        Assert.DoesNotContain(s.Steps, x => x.Key == ProfileSetupRules.StepFamily);
        Assert.Equal(3, s.Total);
        Assert.True(s.Complete);
    }

    [Theory]
    [InlineData(true, false, true, false)]   // отметил детей
    [InlineData(false, true, true, true)]    // отказался
    [InlineData(false, false, false, false)] // ни то, ни другое — 3/4
    public void Parent_FamilyStep_DoneBySavedFamilyOrSkip(bool hasFamily, bool skip, bool done, bool skipped)
    {
        var s = ProfileSetupRules.Evaluate(new ProfileSetupFacts(true, true, true, HasFamily: hasFamily),
            new ProfileSetupSkips(false, false, false, Family: skip), dismissed: false, parent: true);

        var f = s.Steps.Single(x => x.Key == ProfileSetupRules.StepFamily);
        Assert.Equal(done, f.Done);
        Assert.Equal(skipped, f.Skipped);
        Assert.Equal(done, s.Complete);
        Assert.Equal(!done, s.Visible);
    }

    [Fact]
    public void Parent_WithOldSnapshot_FamilyIsNew_EvenAfterDismiss()
    {
        // Прошёл настройку до шага семьи (снимок «me,club,group»), родитель — пункт возвращается.
        var oldSnapshot = new HashSet<string> { "me", "club", "group" };
        var s = ProfileSetupRules.Evaluate(new ProfileSetupFacts(true, true, true), ProfileSetupSkips.None,
            dismissed: true, seen: oldSnapshot, parent: true);

        Assert.Equal([ProfileSetupRules.StepFamily], s.NewSteps);
        Assert.True(s.Visible);
        Assert.Equal(3, s.Done);
        Assert.Equal(4, s.Total);
    }

    // ── Новый шаг (хендофф set-up-profile-ex2, 3b) ───────────────────────────────────────
    // Здесь «новый» моделируем снимком, где одного из трёх базовых шагов не было.

    private static readonly IReadOnlySet<string> SeenMeClub = new HashSet<string> { "me", "club" };

    [Fact]
    public void Evaluate_ParentStepsMatchStepKeys()
    {
        // У родителя видны все шаги — список совпадает с обязательными полями целиком.
        var s = ProfileSetupRules.Evaluate(new ProfileSetupFacts(false, false, false), ProfileSetupSkips.None,
            dismissed: false, parent: true);
        Assert.Equal(ProfileSetupRules.StepKeys, s.Steps.Select(x => x.Key));
    }

    [Fact]
    public void NewStep_NotSeenNotDone_ReturnsItem_EvenAfterDismiss()
    {
        var s = ProfileSetupRules.Evaluate(new ProfileSetupFacts(true, true, false), ProfileSetupSkips.None,
            dismissed: true, seen: SeenMeClub);

        Assert.Equal([ProfileSetupRules.StepGroup], s.NewSteps);
        Assert.True(s.Visible);
        Assert.False(s.Complete);
    }

    [Fact]
    public void NewStep_Skipped_IsClosed()
    {
        var s = ProfileSetupRules.Evaluate(new ProfileSetupFacts(true, true, false),
            new ProfileSetupSkips(false, false, Group: true), dismissed: true, seen: SeenMeClub);

        Assert.Empty(s.NewSteps!);
        Assert.False(s.Visible);
        Assert.True(s.Complete);
    }

    [Fact]
    public void NoSnapshot_NothingIsNew()
    {
        var s = ProfileSetupRules.Evaluate(new ProfileSetupFacts(false, false, false), ProfileSetupSkips.None,
            dismissed: true, seen: null);

        Assert.Empty(s.NewSteps!);
        Assert.False(s.Visible); // Dismiss прячет — новых шагов нет
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("me,club", "club,me")]
    [InlineData(" me , group ,", "group,me")]
    public void ParseSeen(string? stored, string? expected) =>
        Assert.Equal(expected, ProfileSetupRules.ParseSeen(stored) is { } set ? string.Join(",", set.Order()) : null);

    [Fact]
    public void Dismissed_HiddenEvenWhenUnfinished()
    {
        var s = ProfileSetupRules.Evaluate(new ProfileSetupFacts(true, false, false), dismissed: true);

        Assert.Equal(1, s.Done);   // счётчик честный — странице он ещё нужен
        Assert.False(s.Visible);
    }
}
