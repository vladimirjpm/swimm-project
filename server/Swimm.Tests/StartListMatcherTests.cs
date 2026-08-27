using System.Collections.Generic;
using System.Linq;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сверка заявок при ПЕРЕЗАБОРЕ стартового протокола — источник меняется до последнего дня
/// (docs/plans/start-list-plan.md, шаг С4). Аналог <c>ResultMatcherTests</c> для импорта.
/// </summary>
public class StartListMatcherTests
{
    private sealed record Row(int Discipline, int Heat, int Lane, int Swimmer, string Tag = "");

    private static StartListMatch<Row, Row> Run(IEnumerable<Row> old, IEnumerable<Row> fresh) =>
        StartListMatcher.Match(old, fresh,
            r => new StartListKey(r.Discipline, r.Heat, r.Lane, r.Swimmer),
            r => new StartListKey(r.Discipline, r.Heat, r.Lane, r.Swimmer));

    [Fact]
    public void SameProtocol_EverythingMatches()
    {
        var rows = new[] { new Row(1, 1, 3, 100), new Row(1, 1, 4, 200) };

        var m = Run(rows, rows);

        Assert.Equal(2, m.Matched.Count);
        Assert.Empty(m.Moved);
        Assert.Empty(m.Added);
        Assert.Empty(m.Removed);
    }

    [Fact]
    public void LaneChanged_IsAMove_NotDeletePlusInsert()
    {
        // Дорожка входит в ключ (иначе не различить ноги эстафеты), поэтому пересев без
        // второго прохода выглядел бы как «строку удалили и завели другую» — заявка теряла
        // бы Id, а вместе с ним связь с результатом. Это ловушка И8 в чистом виде.
        var old = new[] { new Row(1, 1, 3, 100, "старая") };
        var fresh = new[] { new Row(1, 2, 5, 100, "новая") };

        var m = Run(old, fresh);

        var (o, n) = Assert.Single(m.Moved);
        Assert.Equal("старая", o.Tag);
        Assert.Equal("новая", n.Tag);
        Assert.Empty(m.Added);
        Assert.Empty(m.Removed);
    }

    [Fact]
    public void ScratchedSwimmer_IsRemoved_OthersUntouched()
    {
        var old = new[] { new Row(1, 1, 3, 100), new Row(1, 1, 4, 200) };
        var fresh = new[] { new Row(1, 1, 3, 100) };

        var m = Run(old, fresh);

        Assert.Single(m.Matched);
        Assert.Equal(200, Assert.Single(m.Removed).Swimmer);
        Assert.Empty(m.Added);
    }

    [Fact]
    public void LateRegistration_IsAdded()
    {
        var m = Run([new Row(1, 1, 3, 100)], [new Row(1, 1, 3, 100), new Row(1, 1, 5, 300)]);

        Assert.Single(m.Matched);
        Assert.Equal(300, Assert.Single(m.Added).Swimmer);
        Assert.Empty(m.Removed);
    }

    [Fact]
    public void ScratchShiftingEveryLane_IsAllMoves()
    {
        // Снятие в первой дорожке пересаживает весь заплыв. Если бы это считалось
        // «удалили 4 + добавили 4», журнал заборов было бы невозможно читать.
        var old = Enumerable.Range(1, 4).Select(i => new Row(1, 1, i, 100 + i)).ToList();
        var fresh = Enumerable.Range(1, 4).Select(i => new Row(1, 1, i + 1, 100 + i)).ToList();

        var m = Run(old, fresh);

        Assert.Equal(4, m.Moved.Count);
        Assert.Empty(m.Added);
        Assert.Empty(m.Removed);
        Assert.All(m.Moved, p => Assert.Equal(p.Old.Swimmer, p.New.Swimmer));
    }

    [Fact]
    public void RelayLegs_ShareHeatAndLane_ButStayDistinctBySwimmer()
    {
        // Четыре ноги эстафеты делят заплыв и дорожку — их различает только пловец.
        var team = Enumerable.Range(1, 4).Select(i => new Row(9, 1, 3, 500 + i)).ToList();

        var m = Run(team, team);

        Assert.Equal(4, m.Matched.Count);
        Assert.Empty(m.Removed);
    }

    [Fact]
    public void SameSwimmerInTwoRelayTeams_MoveIsNotGuessed()
    {
        // У клуба бывает две команды в одной дисциплине, и один пловец плывёт в обеих
        // (инцидент comp #1513). Куда какая переехала — неизвестно, и угадывать нельзя:
        // такие строки честно расходятся в Removed/Added, а не сваливаются в лотерею.
        var old = new[] { new Row(9, 1, 3, 500), new Row(9, 1, 4, 500) };
        var fresh = new[] { new Row(9, 2, 6, 500), new Row(9, 2, 7, 500) };

        var m = Run(old, fresh);

        Assert.Empty(m.Moved);
        Assert.Equal(2, m.Added.Count);
        Assert.Equal(2, m.Removed.Count);
    }

    [Fact]
    public void MoveIsScopedToItsOwnDiscipline()
    {
        // Тот же пловец в ДРУГОЙ дисциплине — это отдельная заявка, а не переезд.
        var m = Run([new Row(1, 1, 3, 100)], [new Row(2, 1, 3, 100)]);

        Assert.Empty(m.Moved);
        Assert.Single(m.Added);
        Assert.Single(m.Removed);
    }

    [Fact]
    public void EmptyFresh_MeansEveryoneScratched()
    {
        var m = Run([new Row(1, 1, 3, 100), new Row(1, 1, 4, 200)], []);

        Assert.Equal(2, m.Removed.Count);
        Assert.Empty(m.Matched);
        Assert.Empty(m.Moved);
    }
}
