using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Страна и язык, выбранные в UI импорта (DDL), должны побеждать легаси-конвенцию
/// имени файла «*_{COUNTRY}_{LANG}.pdf»; без выбора — конвенция остаётся fallback-ом.
/// </summary>
public class IsrOrgParserCountryLanguageTests
{
    // ── Страна ───────────────────────────────────────────────────────────────

    [Fact]
    public void CountryOverride_FromUi_WinsOverFileName()
    {
        Assert.Equal("HUN", IsrOrgParser.ResolveCountry("protocol_ISR_he.pdf", "HUN"));
    }

    [Fact]
    public void CountryOverride_IsNormalized_TrimmedAndUppercased()
    {
        Assert.Equal("ISR", IsrOrgParser.ResolveCountry("whatever.pdf", " isr "));
    }

    [Fact]
    public void NoCountryOverride_FallsBackToFileNameConvention()
    {
        Assert.Equal("ISR", IsrOrgParser.ResolveCountry("protocol_ISR_he.pdf", null));
    }

    [Fact]
    public void NoCountryOverride_FileNameWithoutConvention_GivesEmpty()
    {
        Assert.Equal(string.Empty, IsrOrgParser.ResolveCountry("protocol.pdf", null));
    }

    // ── Язык ─────────────────────────────────────────────────────────────────

    [Fact]
    public void LanguageOverride_FromUi_WinsOverFileName()
    {
        Assert.Equal("en", IsrOrgParser.ResolveLanguage("protocol_ISR_he.pdf", "en"));
    }

    [Fact]
    public void LanguageOverride_IsNormalized_TrimmedAndLowercased()
    {
        Assert.Equal("he", IsrOrgParser.ResolveLanguage("whatever.pdf", " HE "));
    }

    [Fact]
    public void NoLanguageOverride_FallsBackToFileNameConvention()
    {
        Assert.Equal("he", IsrOrgParser.ResolveLanguage("protocol_ISR_he.pdf", null));
    }
}
