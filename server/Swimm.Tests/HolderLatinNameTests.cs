using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Держатель рекорда латиницей на международных экранах (решение Влада 16.09.2026).
///
/// ⚠ Это исключение из правила «имена на витрине ивритские по умолчанию», и держать его
/// надо аккуратно: подмена допустима ТОЛЬКО когда латинское имя нашлось в карточке нашего
/// же пловца. Придумать транслитерацию — значит соврать в имени человека, поэтому «не
/// нашли» обязано оставлять строку как есть, а не пытаться.
/// </summary>
public class HolderLatinNameTests
{
    private static readonly Dictionary<string, string> Latin = new(StringComparer.OrdinalIgnoreCase)
    {
        ["תומר פרנקל"] = "Tomer Frankel",
        ["פרנקל תומר"] = "Tomer Frankel",
        ["מרטין קרטאבי"] = "Martin Kartavi",
        ["תום סלע"] = "Tom Sela",
    };

    [Fact]
    public void Resolve_KnownHebrewName_BecomesLatin()
        => Assert.Equal("Tomer Frankel", HolderLatinName.Resolve("תומר פרנקל", Latin));

    /// <summary>Порядок слов в справочнике не гарантирован — ключи лежат в обеих перестановках.</summary>
    [Fact]
    public void Resolve_SwappedWordOrder_StillFound()
        => Assert.Equal("Tomer Frankel", HolderLatinName.Resolve("פרנקל תומר", Latin));

    /// <summary>Лишние пробелы в справочнике встречаются — ключ от них не зависит.</summary>
    [Fact]
    public void Resolve_ExtraSpaces_StillFound()
        => Assert.Equal("Tomer Frankel", HolderLatinName.Resolve("  תומר   פרנקל ", Latin));

    /// <summary>
    /// Незнакомое имя остаётся как есть. Это главный инвариант: транслитерацию мы не
    /// придумываем ни при каких условиях.
    /// </summary>
    [Fact]
    public void Resolve_UnknownName_StaysAsIs()
        => Assert.Equal("שם לא מוכר", HolderLatinName.Resolve("שם לא מוכר", Latin));

    /// <summary>
    /// Эстафета: каждый из четвёрки разбирается отдельно, и найденные заменяются даже если
    /// кто-то один не нашёлся. Половина состава, которую мы знаем, читается — это лучше,
    /// чем оставить всю строку ивритской из-за одного человека.
    /// </summary>
    [Fact]
    public void Resolve_Relay_TranslatesEachMemberIndependently()
    {
        var relay = "מרטין קרטאבי, שם לא מוכר, תום סלע";

        Assert.Equal("Martin Kartavi, שם לא מוכר, Tom Sela", HolderLatinName.Resolve(relay, Latin));
    }

    /// <summary>Латиница не трогается вовсе — у остальных стран держатели и так латиницей.</summary>
    [Fact]
    public void Resolve_LatinName_IsUntouched()
        => Assert.Equal("Cameron Mcevoy", HolderLatinName.Resolve("Cameron Mcevoy", Latin));

    [Fact]
    public void Resolve_NullOrEmpty_IsUntouched()
    {
        Assert.Null(HolderLatinName.Resolve(null, Latin));
        Assert.Equal("", HolderLatinName.Resolve("", Latin));
    }

    [Theory]
    [InlineData("תומר פרנקל", true)]
    [InlineData("Cameron Mcevoy", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HasHebrew_DetectsHebrewLetters(string? text, bool expected)
        => Assert.Equal(expected, HolderLatinName.HasHebrew(text));
}
