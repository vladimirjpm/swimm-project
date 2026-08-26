using Swimm.Application.Validation;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сопоставление имён между источниками, где порядок и полнота имени расходятся.
///
/// Живой случай, ради которого правило и появилось: на loglig «אליה מאשה גדול», у нас
/// «אליה גדול» — один человек, а по равенству наборов токенов он терялся.
/// </summary>
public class TokenNameMatcherTests
{
    private static (IReadOnlyCollection<string>, int?, int) C(string name, int? year, int id) =>
        (LogligClient.NameTokens(name), year, id);

    private static int Resolve(string name, int? year, params (IReadOnlyCollection<string>, int?, int)[] candidates) =>
        TokenNameMatcher.ResolveSingle([.. candidates], LogligClient.NameTokens(name), year);

    [Fact]
    public void ExactSetOfTokens_MatchesRegardlessOfOrder()
    {
        Assert.Equal(7, Resolve("גולוס אמילי", 2008, C("אמילי גולוס", 2008, 7)));
    }

    [Fact]
    public void LongerNameOnTheSite_StillMatches()
    {
        // Двойное имя напечатано не везде — «אליה גדול» это «אליה מאשה גדול».
        Assert.Equal(11, Resolve("אליה גדול", 2011, C("אליה מאשה גדול", 2011, 11)));
    }

    [Fact]
    public void ShorterNameOnTheSite_StillMatches()
    {
        // И наоборот: полнее бывает наша запись.
        Assert.Equal(11, Resolve("אליה מאשה גדול", 2011, C("אליה גדול", 2011, 11)));
    }

    [Fact]
    public void DifferentBirthYear_IsNotTheSamePerson()
    {
        Assert.Equal(0, Resolve("אליה גדול", 2011, C("אליה מאשה גדול", 2009, 11)));
    }

    [Fact]
    public void TwoPossibleCandidates_MeanWeDoNotKnow()
    {
        // Двое подходящих — привязать не тому хуже, чем не привязать.
        Assert.Equal(0, Resolve(
            "אליה גדול", 2011,
            C("אליה מאשה גדול", 2011, 11),
            C("אליה נועה גדול", 2011, 12)));
    }

    [Fact]
    public void ExactMatchWins_OverPartialOne()
    {
        // Точное совпадение не должно проигрывать «похожему»: иначе двойное имя перетянуло бы
        // на себя человека, который в протоколе есть ровно как записан.
        Assert.Equal(11, Resolve(
            "אליה גדול", 2011,
            C("אליה גדול", 2011, 11),
            C("אליה מאשה גדול", 2011, 12)));
    }

    [Fact]
    public void WithoutBirthYear_PartialMatchIsNotEnough()
    {
        // Без года подмножество токенов — слишком слабый признак (однофамильцы).
        Assert.Equal(0, Resolve("גדול", null, C("אליה מאשה גדול", null, 11)));
    }

    [Fact]
    public void NormalizesFinalLettersAndGeresh()
    {
        // Нормализация общая с дедупом: гереш ׳ и ASCII-апостроф — одно и то же.
        Assert.Equal(5, Resolve("אנג׳לה כהן", 2010, C("אנג'לה כהן", 2010, 5)));
    }
}
