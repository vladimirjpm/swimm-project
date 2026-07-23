using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Нормализация имени стиля при JSON-импорте: пробелы→_, нижний регистр, и сведение
/// синонимов комплекса к канону individual_medley (чтобы не плодить дубли в Styles).
/// </summary>
public class NormalizeStyleNameTests
{
    [Theory]
    [InlineData("Freestyle", "freestyle")]
    [InlineData("Individual Medley", "individual_medley")]
    [InlineData("medley", "individual_medley")]
    [InlineData("Medley", "individual_medley")]
    [InlineData("IM", "individual_medley")]
    [InlineData("medley_relay", "medley_relay")]   // эстафетный комплекс не трогаем
    [InlineData("free relay", "free_relay")]
    [InlineData(null, "unknown")]
    [InlineData("", "unknown")]
    public void NormalizeStyleName_MapsSynonymsToCanonical(string? input, string expected)
        => Assert.Equal(expected, JsonImportService.NormalizeStyleName(input));
}
