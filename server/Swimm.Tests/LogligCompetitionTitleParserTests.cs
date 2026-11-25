using Swimm.Application.Mapping;
using Swimm.Parsing.Parsers.Loglig;
using Xunit;

namespace Swimm.Tests;

/// <summary>Р123: заголовок соревнования с полной страницы AthleticsDisciplines (поиск соседнего дня).</summary>
public class LogligCompetitionTitleParserTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Loglig", name));

    [Fact]
    public void FullPage_TitleAndTailDate()
    {
        var title = LogligStartListParser.ParseCompetitionTitle(Fixture("loglig-disciplines-14668.html"));

        Assert.Equal("אליפות מכבי בשחייה אביב 2026- צעירים - 16/05/2026", title);
        Assert.Equal(new DateOnly(2026, 5, 16), LogligTitleRules.TitleDate(title));
    }

    [Fact]
    public void LiveShape_EntitiesDecoded_PartialViewHasNoTitle()
    {
        const string page = """
            <h3 class="main-title disciplines-title">
                תחרות חלופית ל&quot;מילניום&quot; יום שבת 7.12.24 - 07/12/2024</h3>
            """;
        Assert.Equal("תחרות חלופית ל\"מילניום\" יום שבת 7.12.24 - 07/12/2024", LogligStartListParser.ParseCompetitionTitle(page));
        Assert.Null(LogligStartListParser.ParseCompetitionTitle("<table class=\"table res-table\"><tr class=\"disciplines-title\"></tr></table>"));
    }
}
