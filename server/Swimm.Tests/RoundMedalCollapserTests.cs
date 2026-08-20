using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Схлопывание медалей, задвоенных раундами (И13, docs/data-integrity.md §10). Кейсы —
/// из живого чемпионата 1581: מיכל אוגינץ выиграла и утренний зачёт возрастной группы,
/// и вечерний финал на всех четырёх своих дистанциях.
/// </summary>
public class RoundMedalCollapserTests
{
    private sealed record Row(string Key, string? Round, int? Position);

    private static List<Row> Collapse(params Row[] rows) =>
        RoundMedalCollapser.Collapse(rows, r => r.Key, r => r.Round, r => r.Position);

    /// <summary>Две победы одной ступени за день — одна медаль, а не две.</summary>
    [Fact]
    public void TwoRoundsOfSameAgeBand_CountAsOneMedal()
    {
        var result = Collapse(
            new Row("michal|freestyle|50|14", "timed-final", 1),
            new Row("michal|freestyle|50|14", "final", 1));

        var kept = Assert.Single(result);
        Assert.Equal(1, kept.Position);
    }

    /// <summary>Места разошлись — засчитываем ЛУЧШЕЕ: утром выиграла, вечером была второй.</summary>
    [Fact]
    public void DifferentPlacesAcrossRounds_KeepsTheBest()
    {
        var result = Collapse(
            new Row("michal|freestyle|200|14", "timed-final", 3),
            new Row("michal|freestyle|200|14", "final", 1));

        Assert.Equal(1, Assert.Single(result).Position);
    }

    /// <summary>Разные дистанции — разные медали, схлопывать нечего.</summary>
    [Fact]
    public void DifferentDisciplines_AreKeptApart()
    {
        var result = Collapse(
            new Row("michal|freestyle|50|14", "timed-final", 1),
            new Row("michal|freestyle|50|14", "final", 1),
            new Row("michal|freestyle|100|14", "timed-final", 1),
            new Row("michal|freestyle|100|14", "final", 1));

        Assert.Equal(2, result.Count);
    }

    /// <summary>
    /// Обычные соревнования: раунда нет ни у кого — список не трогаем. Это гарантия, что
    /// правило работает ТОЛЬКО на чемпионатах с раундами и ничего не меняет в остальной базе.
    /// </summary>
    [Fact]
    public void WithoutRounds_NothingIsCollapsed()
    {
        var result = Collapse(
            new Row("swimmer|freestyle|50|14", null, 1),
            new Row("swimmer|freestyle|50|14", null, 2));   // напр. два дня многодневки

        Assert.Equal(2, result.Count);
    }

    /// <summary>Один раунд на группу — тоже не трогаем (мокдамот скрыты правилом Р34 раньше).</summary>
    [Fact]
    public void SingleRound_IsNotCollapsed()
    {
        var result = Collapse(
            new Row("swimmer|freestyle|50|14", "final", 1),
            new Row("swimmer|freestyle|50|14", "final", 2));

        Assert.Equal(2, result.Count);
    }
}
