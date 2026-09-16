using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Расстановка мест и отставание от мирового (этап 11.2.1). Чистая функция — базы здесь нет
/// намеренно: сортировку делает SQL по вычисляемой колонке <c>TimeMs</c>, а вот места с
/// совпадениями и проценты ломаются молча, и ловить их надо отдельно.
/// </summary>
public class RecordRankingBuilderTests
{
    private static RecordRankingBuilder.Row Row(string code, int ms, string? issue = null)
        => new(code, $"{ms}", ms, $"Holder {code}", "01/01/2020", issue);

    private static RecordRankingWorldDto World(int ms)
        => new() { Time = $"{ms}", TimeMs = ms, HolderName = "World holder", RecordDate = "01/01/2019" };

    /// <summary>Спортивные места: равное время — равное место, следующее перепрыгивает группу.</summary>
    [Fact]
    public void Build_TiedTimesShareRank_AndNextRankSkips()
    {
        var rows = RecordRankingBuilder.Build(
            [Row("USA", 20_240), Row("FRA", 20_770), Row("ITA", 20_770), Row("BRA", 21_010)],
            world: null);

        Assert.Equal([1, 2, 2, 4], rows.Select(r => r.Rank).ToArray());
    }

    /// <summary>
    /// Три одинаковых времени подряд — одно место на всех, а следующее считается от позиции,
    /// а не «предыдущее + 1».
    /// </summary>
    [Fact]
    public void Build_ThreeWayTie_SkipsTwoRanks()
    {
        var rows = RecordRankingBuilder.Build(
            [Row("A", 100), Row("B", 100), Row("C", 100), Row("D", 101)],
            world: null);

        Assert.Equal([1, 1, 1, 4], rows.Select(r => r.Rank).ToArray());
    }

    /// <summary>Отставание считается от мирового: и в миллисекундах, и в процентах от НЕГО.</summary>
    [Fact]
    public void Build_BehindWorld_CountsFromWorldRecord()
    {
        var rows = RecordRankingBuilder.Build([Row("USA", 20_240)], World(20_160));

        var usa = Assert.Single(rows);
        Assert.Equal(80, usa.BehindWorldMs);
        Assert.Equal(0.4, usa.BehindWorldPercent);   // 80 / 20160 = 0.3968… → 0.4
    }

    /// <summary>
    /// Рекорд страны БЫСТРЕЕ мирового — отставание отрицательное, а не обрезанное в ноль.
    /// Это живой случай (И-22, И-23): источник противоречит сам себе, и витрина обязана
    /// иметь возможность это показать, а не спрятать.
    /// </summary>
    [Fact]
    public void Build_FasterThanWorld_KeepsNegativeDelta()
    {
        var rows = RecordRankingBuilder.Build([Row("BER", 27_720)], World(28_370));

        var ber = Assert.Single(rows);
        Assert.Equal(-650, ber.BehindWorldMs);
        Assert.True(ber.BehindWorldPercent < 0);
    }

    /// <summary>
    /// Мирового рекорда у дисциплины может не быть вовсе (4×50 в длинной воде — у World
    /// Aquatics такой рекорд не ведётся, проверено на данных 16.09.2026). Тогда отставание
    /// null, а не ноль: «ноль» означал бы «вровень с мировым».
    /// </summary>
    [Fact]
    public void Build_NoWorldRecord_LeavesDeltaNull()
    {
        var rows = RecordRankingBuilder.Build([Row("ISR", 100_000)], world: null);

        var row = Assert.Single(rows);
        Assert.Null(row.BehindWorldMs);
        Assert.Null(row.BehindWorldPercent);
    }

    /// <summary>Пустая дисциплина — пустой рейтинг, а не исключение.</summary>
    [Fact]
    public void Build_Empty_ReturnsEmpty()
    {
        Assert.Empty(RecordRankingBuilder.Build([], World(1000)));
    }

    /// <summary>
    /// Дистанция приводится к форме СПРАВОЧНИКА: «4x50m» и «4X50M» обязаны находить «4X50m».
    /// Без этого эстафетные рейтинги молча пустые — фильтр не совпадает ни с одной строкой,
    /// а ошибки нет: 200 и ноль результатов.
    /// </summary>
    [Theory]
    [InlineData("4x50m", "4X50m")]
    [InlineData("4X50M", "4X50m")]
    [InlineData(" 4x100M ", "4X100m")]
    [InlineData("50m", "50m")]
    [InlineData("1500M", "1500m")]
    public void Query_NormalizesDistanceToReferenceForm(string input, string expected)
        => Assert.Equal(expected, RecordRankingQuery.NormalizeDistance(input));

    /// <summary>Фабрика приводит и остальные оси: стиль, пол и бассейн — в нижний регистр.</summary>
    [Fact]
    public void Query_Create_LowercasesAxes()
    {
        var q = RecordRankingQuery.Create(" FreeStyle ", "4x50M", "MALE", "25M", null, 50, 0);

        Assert.Equal("freestyle", q.Style);
        Assert.Equal("4X50m", q.Distance);
        Assert.Equal("male", q.Gender);
        Assert.Equal("25m", q.PoolType);
    }

    /// <summary>Претензия к записи доезжает до строки рейтинга как есть.</summary>
    [Fact]
    public void Build_CarriesIssueReason()
    {
        var rows = RecordRankingBuilder.Build([Row("BER", 27_720, "faster-than-world-record")], world: null);

        Assert.Equal("faster-than-world-record", Assert.Single(rows).IssueReason);
    }
}
