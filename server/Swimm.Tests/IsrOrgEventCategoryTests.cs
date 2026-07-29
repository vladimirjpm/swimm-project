using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Нормализация категории заплыва. Категория — единственное, что отличает три разных
/// заплыва протокола с одинаковыми стилем, дистанцией и полом («- Men», «- U17 Boys»,
/// «- Men Para»); без неё они сливались в одну дисциплину с тремя первыми местами.
/// </summary>
public class IsrOrgEventCategoryTests
{
    [Theory]
    [InlineData("open", "open")]      // «- Men» / «- Women»: взрослые без ограничения
    [InlineData("para", "para")]
    [InlineData("PARA", "para")]
    [InlineData("U17", "17")]         // ParseEnCategory обычно уже срезает U, но не всегда
    [InlineData("17", "17")]
    [InlineData("12", "12")]          // ивритский «גיל 12»
    [InlineData("25-29", "25-29")]    // masters-группа
    public void Normalizes(string raw, string expected)
        => Assert.Equal(expected, IsrOrgParser.NormalizeEventCategory(raw));

    [Fact]
    public void EmptyOrMissing_IsNull()
    {
        Assert.Null(IsrOrgParser.NormalizeEventCategory(null));
        Assert.Null(IsrOrgParser.NormalizeEventCategory("   "));
    }

    /// <summary>
    /// Смешанные заплывы: пол в них "none", а возрастная часть своя. «MIX 18-99» и «MIX U17» —
    /// РАЗНЫЕ программы, поэтому возраст сохраняется в префиксе; свести обе к голому "mix"
    /// значило бы схлопнуть их в одну категорию.
    /// </summary>
    [Theory]
    [InlineData("MIX 18-99", null, "mix-18-99")]
    [InlineData("MIX U17", null, "mix-17")]
    [InlineData("18-99", "none", "mix-18-99")]   // категория без слова MIX, но пол смешанный
    [InlineData("17", "none", "mix-17")]
    [InlineData("open", "none", "mix")]          // смешанный без возрастной части
    public void MixKeepsItsAgeBand(string raw, string? gender, string expected)
        => Assert.Equal(expected, IsrOrgParser.NormalizeEventCategory(raw, gender));

    [Fact]
    public void GenderedEvents_AreNotTreatedAsMix()
    {
        Assert.Equal("open", IsrOrgParser.NormalizeEventCategory("open", "male"));
        Assert.Equal("17", IsrOrgParser.NormalizeEventCategory("17", "female"));
        Assert.Equal("para", IsrOrgParser.NormalizeEventCategory("para", "male"));
    }
}
