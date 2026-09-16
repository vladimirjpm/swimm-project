using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сравнение двух стран (этап 11.3.1). Главное здесь — не арифметика, а правило 11.3.3:
/// **«нет данных» ≠ «медленнее»**. Нарушить его можно одной строкой кода, а заметить —
/// только сравнив страны с разным покрытием, поэтому оно и вынесено в тесты первым.
/// </summary>
public class RecordCompareBuilderTests
{
    private static RecordCompareBuilder.Row Row(
        string code, int ms, string style = "freestyle", string distance = "50m",
        string gender = "male", string pool = "50m", string? issue = null)
        => new(code, style, distance, gender, pool, $"{ms}", ms, $"Holder {code}", "01/01/2020", issue);

    [Fact]
    public void Build_FasterSideWins_AndDeltaIsAbsolute()
    {
        var cmp = RecordCompareBuilder.Build([Row("ISR", 22_000), Row("USA", 21_000)], "ISR", "USA");

        var row = Assert.Single(cmp.Rows);
        Assert.Equal("b", row.Outcome);
        Assert.Equal(1000, row.DeltaMs);
        Assert.Equal(1, cmp.Score.Compared);
        Assert.Equal(1, cmp.Score.B);
        Assert.Equal(0, cmp.Score.A);
    }

    /// <summary>
    /// Дисциплина, где рекорд есть только у одной стороны, остаётся ВИДИМОЙ строкой, но в
    /// счёт не идёт ни в чью пользу. Это и есть «страна с половинным покрытием не выигрывает
    /// пустотами».
    /// </summary>
    [Fact]
    public void Build_MissingSide_IsNoData_NotALoss()
    {
        var cmp = RecordCompareBuilder.Build(
            [Row("USA", 21_000), Row("USA", 47_000, distance: "100m")],
            "ISR", "USA");

        Assert.Equal(2, cmp.Rows.Count);                       // обе дисциплины видны
        Assert.All(cmp.Rows, r => Assert.Equal("no_data", r.Outcome));
        Assert.All(cmp.Rows, r => Assert.Null(r.DeltaMs));
        Assert.All(cmp.Rows, r => Assert.Null(r.A));

        Assert.Equal(0, cmp.Score.Compared);                   // сравнивать было нечего
        Assert.Equal(0, cmp.Score.A);
        Assert.Equal(0, cmp.Score.B);                          // ← пустоты НЕ победа USA
        Assert.Equal(2, cmp.Score.BOnly);
        Assert.Equal(0, cmp.Score.AOnly);
    }

    /// <summary>
    /// Живой перекос: у A три дисциплины и все выиграны, у B — тридцать, но общих только три.
    /// Счёт обязан считаться по общим, иначе «3:0» превратилось бы в «3:30».
    /// </summary>
    [Fact]
    public void Build_LopsidedCoverage_ScoreCountsOnlyCommonEvents()
    {
        var rows = new List<RecordCompareBuilder.Row>();
        foreach (var d in new[] { "50m", "100m", "200m" })
        {
            rows.Add(Row("ISR", 20_000, distance: d));
            rows.Add(Row("USA", 21_000, distance: d));
        }
        // ещё десяток дисциплин, где есть только USA
        foreach (var d in new[] { "400m", "800m", "1500m" })
            rows.Add(Row("USA", 200_000, distance: d));

        var cmp = RecordCompareBuilder.Build(rows, "ISR", "USA");

        Assert.Equal(3, cmp.Score.Compared);
        Assert.Equal(3, cmp.Score.A);
        Assert.Equal(0, cmp.Score.B);
        Assert.Equal(3, cmp.Score.BOnly);
        Assert.Equal(6, cmp.Rows.Count);    // пустые дисциплины не выкинуты
    }

    [Fact]
    public void Build_EqualTimes_IsTie_NotAWin()
    {
        var cmp = RecordCompareBuilder.Build([Row("ISR", 21_000), Row("USA", 21_000)], "ISR", "USA");

        Assert.Equal("tie", Assert.Single(cmp.Rows).Outcome);
        Assert.Equal(1, cmp.Score.Tie);
        Assert.Equal(0, cmp.Score.A);
        Assert.Equal(0, cmp.Score.B);
        Assert.Equal(1, cmp.Score.Compared);
    }

    /// <summary>
    /// Обмен сторонами даёт ЗЕРКАЛЬНЫЙ результат: те же дисциплины, тот же модуль дельты,
    /// исходы и счёт поменялись местами. Приёмка 11.3.2 требует именно этого.
    /// </summary>
    [Fact]
    public void Build_SwappingSides_MirrorsTheResult()
    {
        var rows = new List<RecordCompareBuilder.Row>
        {
            Row("ISR", 22_000), Row("USA", 21_000),
            Row("ISR", 47_000, distance: "100m"),
            Row("USA", 48_000, distance: "100m"),
            Row("USA", 200_000, distance: "400m"),
        };

        var direct = RecordCompareBuilder.Build(rows, "ISR", "USA");
        var mirror = RecordCompareBuilder.Build(rows, "USA", "ISR");

        Assert.Equal(direct.Score.Compared, mirror.Score.Compared);
        Assert.Equal(direct.Score.A, mirror.Score.B);
        Assert.Equal(direct.Score.B, mirror.Score.A);
        Assert.Equal(direct.Score.AOnly, mirror.Score.BOnly);
        Assert.Equal(direct.Score.BOnly, mirror.Score.AOnly);

        Assert.Equal(direct.Rows.Select(r => r.DeltaMs), mirror.Rows.Select(r => r.DeltaMs));
        Assert.Equal(
            direct.Rows.Select(r => r.Outcome switch { "a" => "b", "b" => "a", var x => x }),
            mirror.Rows.Select(r => r.Outcome));
    }

    /// <summary>
    /// Порядок дистанций числовой, а не строковый: «1500m» после «800m», эстафеты — после
    /// личных. Строковая сортировка ставила бы «1500m» между «100m» и «200m».
    /// </summary>
    [Fact]
    public void Build_OrdersDistancesNumerically_RelaysLast()
    {
        var rows = new[] { "4X100m", "1500m", "50m", "800m", "100m", "4X50m" }
            .Select(d => Row("ISR", 1000, distance: d)).ToList();

        var cmp = RecordCompareBuilder.Build(rows, "ISR", "USA");

        Assert.Equal(
            ["50m", "100m", "800m", "1500m", "4X50m", "4X100m"],
            cmp.Rows.Select(r => r.Distance).ToArray());
    }

    /// <summary>Претензия к записи доезжает до стороны — инвариант И11 работает и здесь.</summary>
    [Fact]
    public void Build_CarriesIssueReason()
    {
        var cmp = RecordCompareBuilder.Build(
            [Row("BER", 27_720, issue: "faster-than-world-record"), Row("USA", 28_000)],
            "BER", "USA");

        Assert.Equal("faster-than-world-record", Assert.Single(cmp.Rows).A!.IssueReason);
    }

    /// <summary>Совсем нет данных ни у кого — пустое сравнение, а не исключение.</summary>
    [Fact]
    public void Build_Empty_ReturnsEmptyScore()
    {
        var cmp = RecordCompareBuilder.Build([], "ISR", "USA");

        Assert.Empty(cmp.Rows);
        Assert.Equal(0, cmp.Score.Compared);
    }
}
