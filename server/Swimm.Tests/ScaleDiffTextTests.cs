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
}
