using Swimm.Application.Dtos;
using Swimm.Application.Validation;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Ввод таблички расхождения строкой «21:5&gt;6, 22:3&gt;5» — тем же приёмом, что и шкала
/// правила: админ переносит пару чисел из протокола, а не заполняет сетку полей.
/// </summary>
public class ScaleDiffTextTests
{
    [Fact]
    public void Parses_PlaceExpectedActual()
    {
        Assert.True(ScaleDiffText.TryParse("21:5>6, 22:3>5", out var rows, out var error));

        Assert.Null(error);
        Assert.Equal([new ScaleDiffRowDto(21, 5, 6), new ScaleDiffRowDto(22, 3, 5)], rows);
    }

    [Theory]
    [InlineData("21:5->6")]
    [InlineData("21 = 5 → 6")]
    [InlineData("21:5>6\n")]
    public void AcceptsArrowVariantsAndSpacing(string text)
    {
        Assert.True(ScaleDiffText.TryParse(text, out var rows, out _));
        Assert.Equal(new ScaleDiffRowDto(21, 5, 6), Assert.Single(rows));
    }

    [Fact]
    public void SortsByPlace()
    {
        ScaleDiffText.TryParse("22:3>5; 21:5>6", out var rows, out _);
        Assert.Equal([21, 22], rows.Select(r => r.Place));
    }

    [Fact]
    public void EmptyInput_IsValid_AndMeansNoTable()
    {
        Assert.True(ScaleDiffText.TryParse("   ", out var rows, out var error));
        Assert.Empty(rows);
        Assert.Null(error);
    }

    [Fact]
    public void Garbage_ReportsWhatItCouldNotRead()
    {
        Assert.False(ScaleDiffText.TryParse("21:5>6, чепуха", out _, out var error));
        Assert.Contains("чепуха", error);
    }

    [Fact]
    public void DuplicatePlace_Rejected()
    {
        // Иначе таблица показала бы одно место дважды с разными числами.
        Assert.False(ScaleDiffText.TryParse("21:5>6, 21:4>7", out _, out var error));
        Assert.Contains("дважды", error);
    }

    [Fact]
    public void FormatsBack_ForTheForm()
    {
        var text = ScaleDiffText.Format([new ScaleDiffRowDto(22, 3, 5), new ScaleDiffRowDto(21, 5, 6)]);
        Assert.Equal("21:5>6, 22:3>5", text);
    }

    /// <summary>
    /// Контекст строки протокола после черт: заплыв, время, пловец, клуб — в том же порядке,
    /// в каком стоят колонки таблицы. Ради заплыва всё и заводилось: у 1581 очки розданы ПО
    /// НОМЕРУ ЗАПЛЫВА, и утверждать это, не показывая номер, значит просить верить на слово.
    /// </summary>
    [Fact]
    public void Parses_ProtocolContextAfterPipes()
    {
        Assert.True(ScaleDiffText.TryParse(
            """
            1:25>14 | 3 | 29.17 | Ofir Simcha | Maccabi Kiryat Bialik
            9:12>25 | 1 | 32.90 | Ruth Gutman | Maccabi Haifa
            """,
            out var rows, out var error));

        Assert.Null(error);
        Assert.Equal(
        [
            new ScaleDiffRowDto(1, 25, 14, 3, "29.17", "Ofir Simcha", "Maccabi Kiryat Bialik"),
            new ScaleDiffRowDto(9, 12, 25, 1, "32.90", "Ruth Gutman", "Maccabi Haifa"),
        ], rows);
    }

    /// <summary>
    /// Запятая делит строки только пока контекст не используется: в именах и названиях она
    /// встречается, и резать по ней значило бы рвать их пополам.
    /// </summary>
    [Fact]
    public void CommaInsideName_DoesNotSplitTheRow()
    {
        Assert.True(ScaleDiffText.TryParse("9:12>25 | 1 | 32.90 | Cohen, Dan", out var rows, out _));
        Assert.Equal("Cohen, Dan", Assert.Single(rows).Swimmer);
    }

    [Fact]
    public void RowsWithAndWithoutContext_LiveTogether()
    {
        Assert.True(ScaleDiffText.TryParse("1:25>14 | 3; 2:22>13", out var rows, out _));
        Assert.Equal(3, rows[0].Heat);
        Assert.Null(rows[1].Heat);
    }

    /// <summary>
    /// Позиция ячейки важнее её наличия: пропущенные заплыв и время оставляют пустые места
    /// между чертами, иначе имя уехало бы в колонку заплыва.
    /// </summary>
    [Fact]
    public void EmptyCells_KeepTheColumnsInPlace()
    {
        Assert.True(ScaleDiffText.TryParse("9:12>25 ||| Ruth Gutman | Maccabi Haifa", out var rows, out _));

        var row = Assert.Single(rows);
        Assert.Null(row.Heat);
        Assert.Null(row.Time);
        Assert.Equal("Ruth Gutman", row.Swimmer);
        Assert.Equal("Maccabi Haifa", row.Club);
    }

    [Fact]
    public void NonNumericHeat_ReportsWhatItCouldNotRead()
    {
        Assert.False(ScaleDiffText.TryParse("9:12>25 | первый", out _, out var error));
        Assert.Contains("первый", error);
    }

    [Fact]
    public void OverlongName_Rejected()
    {
        var text = $"1:25>14 | 3 | 29.17 | {new string('x', ScaleDiffText.SubjectMaxLength + 1)}";
        Assert.False(ScaleDiffText.TryParse(text, out _, out var error));
        Assert.Contains("Пловец", error);
    }

    /// <summary>Формат обратим и с контекстом — включая дыру посередине.</summary>
    [Fact]
    public void FormatsBack_WithContext_AndParsesAgain()
    {
        List<ScaleDiffRowDto> original =
        [
            new ScaleDiffRowDto(9, 12, 25, 1, "32.90", "Ruth Gutman", "Maccabi Haifa"),
            new ScaleDiffRowDto(1, 25, 14, null, "29.17", "Cohen, Dan", null),
            new ScaleDiffRowDto(3, 20, 12, 3, null, null, "Maccabi Haifa"),
        ];

        var text = ScaleDiffText.Format(original);
        Assert.True(ScaleDiffText.TryParse(text, out var again, out var error));
        Assert.Null(error);
        Assert.Equal(original.OrderBy(r => r.Place), again);
    }
}
