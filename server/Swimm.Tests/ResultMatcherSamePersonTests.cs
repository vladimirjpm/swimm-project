using System.Collections.Generic;
using System.Linq;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// И-63 (07.11.2026), comp 1580 «ליגה מספר 5- ראשון הרצליה»: протокол печатает девочек 14+ в секции
/// «בנים 14», у наших строк пол выровнен по карточке. Перезабор без этих правил отдавал девочке
/// строку мальчика того же заплыва и дорожки (FIFO по ключу с полом), а мальчика вставлял заново.
/// </summary>
public class ResultMatcherSamePersonTests
{
    private const int Girl = 61768, Boy = 60129;

    private static ResultRecord Old(int id, int swimmerId, string gender, int heat = 1, int lane = 4) => new()
    {
        Id = id, CompetitionId = 1580, StyleId = 4, Distance = "200",
        Gender = gender, Heat = heat, Lane = lane, SwimmerId = swimmerId
    };

    private static ResultRecord New(int swimmerId, string gender, int heat = 1, int lane = 4) => new()
    {
        CompetitionId = 1580, StyleId = 4, Distance = "200",
        Gender = gender, Heat = heat, Lane = lane, SwimmerId = swimmerId
    };

    private static ResultMatch<ResultRecord, ResultRecord> Run(
        IReadOnlyList<ResultRecord> oldRows, IReadOnlyList<ResultRecord> newRows) =>
        ResultMatcher.Match(oldRows, newRows,
            ResultMatcher.KeyOfPersisted, ResultMatcher.KeyOfTransient,
            ResultMatcher.DiscriminatorOfPersisted, ResultMatcher.DiscriminatorOfTransient);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GirlPrintedInBoysSection_MatchesHerOwnRow_BoyKeepsHis(bool girlFirstInFile)
    {
        var girlOld = Old(6079927, Girl, "female");
        var boyOld = Old(6079930, Boy, "male");
        var girlNew = New(Girl, "male");   // как напечатано: секция «בנים 14»
        var boyNew = New(Boy, "male");
        var file = girlFirstInFile ? new[] { girlNew, boyNew } : new[] { boyNew, girlNew };

        var match = Run([girlOld, boyOld], file);

        Assert.Empty(match.Inserted);
        Assert.Empty(match.Deleted);
        Assert.Contains(match.Matched, p => p.Old == girlOld && p.New == girlNew);
        Assert.Contains(match.Matched, p => p.Old == boyOld && p.New == boyNew);
        var cross = Assert.Single(match.CrossGender);
        Assert.Same(girlOld, cross.Old);
    }

    [Fact]
    public void OtherSwimmersRow_NotTaken_WhenOwnerIsInFile()
    {
        // Своей строки у девочки нет (пришла впервые) — строку мальчика она не забирает.
        var boyOld = Old(6079930, Boy, "male");
        var girlNew = New(Girl, "male");
        var boyNew = New(Boy, "male");

        var match = Run([boyOld], [girlNew, boyNew]);

        Assert.Equal(girlNew, Assert.Single(match.Inserted));
        Assert.Equal((boyOld, boyNew), Assert.Single(match.Matched));
        Assert.Empty(match.CrossGender);
    }

    [Fact]
    public void RenamedSwimmer_OwnerNotInFile_StillMatchedByKey()
    {
        // Обычный перезабор: опечатку в имени исправили, пловец сменился (Р5) — старого в файле нет,
        // строка его — свободна, FIFO как раньше.
        var old = Old(1, swimmerId: 500, gender: "male");
        var renamed = New(swimmerId: 501, gender: "male");

        var match = Run([old], [renamed]);

        Assert.Equal((old, renamed), Assert.Single(match.Matched));
        Assert.Empty(match.Inserted);
        Assert.Empty(match.CrossGender);
    }

    [Fact]
    public void SameGender_IsOrdinaryMatch_NotCrossGender()
    {
        var old = Old(1, Girl, "female");
        var incoming = New(Girl, "female");

        var match = Run([old], [incoming]);

        Assert.Single(match.Matched);
        Assert.Empty(match.CrossGender);
    }

    [Fact]
    public void TwoStoredRowsOfSamePerson_Ambiguous_LeftToKeyMatch()
    {
        // У девочки уже две строки на этом месте (female + male — старый дубль): предпроход не
        // гадает, ключ сводит male с male, female остаётся «исчезнувшей».
        var female = Old(1, Girl, "female");
        var male = Old(2, Girl, "male");
        var incoming = New(Girl, "male");

        var match = Run([female, male], [incoming]);

        Assert.Equal((male, incoming), Assert.Single(match.Matched));
        Assert.Equal(female, Assert.Single(match.Deleted));
        Assert.Empty(match.CrossGender);
    }

    [Fact]
    public void AnonymousSwimmers_AreNotAffected()
    {
        // SwimmerId 0 (ещё не сохранён / анонимная нога) — правила не участвуют.
        var old = Old(1, swimmerId: 7, gender: "female");
        var incoming = New(swimmerId: 0, gender: "male");

        var match = Run([old], [incoming]);

        Assert.Empty(match.Matched);
        Assert.Single(match.Inserted);
    }
}
