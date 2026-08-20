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
    /// «Кому достались очки» — после «|». Ради него столбец и заводился: «за 9-е место 25
    /// очков» становится доказательством только когда видно, какому клубу они ушли.
    /// </summary>
    [Fact]
    public void Parses_SubjectAfterPipe()
    {
        Assert.True(ScaleDiffText.TryParse(
            """
            1:25>14 | Maccabi Kiryat Bialik
            9:12>25 | Maccabi Haifa
            """,
            out var rows, out var error));

        Assert.Null(error);
        Assert.Equal(
        [
            new ScaleDiffRowDto(1, 25, 14, "Maccabi Kiryat Bialik"),
            new ScaleDiffRowDto(9, 12, 25, "Maccabi Haifa"),
        ], rows);
    }

    /// <summary>
    /// Запятая делит строки только пока «кому» не используется: в названиях и именах она
    /// встречается, и резать по ней значило бы рвать их пополам.
    /// </summary>
    [Fact]
    public void CommaInsideSubject_DoesNotSplitTheRow()
    {
        Assert.True(ScaleDiffText.TryParse("9:12>25 | Cohen, Dan", out var rows, out _));
        Assert.Equal("Cohen, Dan", Assert.Single(rows).Subject);
    }

    [Fact]
    public void RowsWithAndWithoutSubject_LiveTogether()
    {
        Assert.True(ScaleDiffText.TryParse("1:25>14 | Maccabi Haifa; 2:22>13", out var rows, out _));
        Assert.Equal("Maccabi Haifa", rows[0].Subject);
        Assert.Null(rows[1].Subject);
    }

    /// <summary>Формат обратим: то, что форма показала, она же обязана и разобрать.</summary>
    [Fact]
    public void FormatsBack_WithSubjects_AndParsesAgain()
    {
        List<ScaleDiffRowDto> original =
            [new ScaleDiffRowDto(9, 12, 25, "Maccabi Haifa"), new ScaleDiffRowDto(1, 25, 14, "Cohen, Dan")];

        var text = ScaleDiffText.Format(original);
        Assert.True(ScaleDiffText.TryParse(text, out var again, out _));
        Assert.Equal(original.OrderBy(r => r.Place), again);
    }

    [Fact]
    public void OverlongSubject_Rejected()
    {
        var text = $"1:25>14 | {new string('x', ScaleDiffText.SubjectMaxLength + 1)}";
        Assert.False(ScaleDiffText.TryParse(text, out _, out var error));
        Assert.Contains("Кто", error);
    }
}
