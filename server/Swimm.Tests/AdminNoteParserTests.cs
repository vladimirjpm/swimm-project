using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>Формат справки админки (docs/admin-notes/README.md): заголовок, «Проверено», тело; форма ключа.</summary>
public class AdminNoteParserTests
{
    [Fact]
    public void Parses_title_checked_and_body()
    {
        var note = AdminNoteParser.Parse("# Почему выключена\r\n\r\nПроверено: 20.11.2026\r\n\r\nТекст **жирный**.\r\n\r\n| a | b |\r\n|---|---|\r\n| 1 | 2 |\r\n", "fallback");

        Assert.Equal("Почему выключена", note.Title);
        Assert.Equal(new DateOnly(2026, 11, 20), note.Checked);
        Assert.StartsWith("Текст **жирный**.", note.Body);
        Assert.Contains("| 1 | 2 |", note.Body);
        Assert.DoesNotContain("Проверено", note.Body);
        Assert.DoesNotContain("# Почему", note.Body);
    }

    [Theory]
    [InlineData("Проверено: 5.1.2026", 2026, 1, 5)]
    [InlineData("**Проверено:** 05.01.2026", 2026, 1, 5)]
    [InlineData("проверено 05.01.2026 по прод-базе", 2026, 1, 5)]
    public void Checked_line_variants(string line, int y, int m, int d)
    {
        var note = AdminNoteParser.Parse($"# T\n{line}\nbody", "k");
        Assert.Equal(new DateOnly(y, m, d), note.Checked);
        Assert.Equal("body", note.Body);
    }

    [Fact]
    public void Missing_header_and_date_keep_everything_in_body()
    {
        var note = AdminNoteParser.Parse("Просто текст.\nВторая строка.", "job.x");
        Assert.Equal("job.x", note.Title);
        Assert.Null(note.Checked);
        Assert.Equal("Просто текст.\nВторая строка.", note.Body);
    }

    [Fact]
    public void Invalid_date_is_null_but_line_is_consumed()
    {
        var note = AdminNoteParser.Parse("# T\nПроверено: 31.02.2026\ntext", "k");
        Assert.Null(note.Checked);
        Assert.Equal("text", note.Body);
    }

    [Fact]
    public void Subheading_is_not_a_title()
    {
        var note = AdminNoteParser.Parse("## Раздел\ntext", "k");
        Assert.Equal("k", note.Title);
        Assert.StartsWith("## Раздел", note.Body);
    }

    [Theory]
    [InlineData("job.loglig-batch", true)]
    [InlineData("page.swimmers-loglig", true)]
    [InlineData("setting.results-auto-mode", true)]
    [InlineData("a", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("..", false)]
    [InlineData("job..x", false)]
    [InlineData("../secret", false)]
    [InlineData("..\\secret", false)]
    [InlineData("job/x", false)]
    [InlineData("job\\x", false)]
    [InlineData(".hidden", false)]
    [InlineData("job.", false)]
    [InlineData("Job.LogligBatch", false)]
    [InlineData("c:x", false)]
    [InlineData("job x", false)]
    [InlineData("job%2e%2e", false)]
    public void Key_shape(string? key, bool ok) => Assert.Equal(ok, AdminNoteParser.IsValidKey(key));

    [Fact]
    public void Key_length_is_capped() =>
        Assert.False(AdminNoteParser.IsValidKey(new string('a', AdminNoteParser.MaxKeyLength + 1)));
}
