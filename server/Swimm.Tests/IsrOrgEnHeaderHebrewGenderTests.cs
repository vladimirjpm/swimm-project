using System.Linq;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// EN-экспорт loglig переведён ЧЕРЕЗ СТРОЧКУ. В одном протоколе (зимний чемпионат
/// נוער ובוגרים, loglig 13627, день 26/12/2025) соседствуют
/// «50m Freestyle - Girls 14» и «50m Freestyle - 15 תונב» — организатор перевёл не все
/// заголовки. Ивритское слово приходит в EN-файле в визуальном порядке («תונב» вместо
/// «בנות»), и в EN-режиме строка через NormalizeHebrewLine не проходит.
///
/// Пока эти токены не опознавались, пол терялся ("none"), заголовок объявлялся смешанным
/// заплывом и категория становилась несуществующей «mix-15». Хуже: событие мальчиков и
/// событие девочек одного возраста получали ОДИН ключ дисциплины, и AssignHeatTypes
/// объявляла их парой «прелимы + финал» — девичьи заплывы уезжали в prelim и скрывались
/// фильтром, а мальчиковые мокдамот показывались как финал.
/// </summary>
public class IsrOrgEnHeaderHebrewGenderTests
{
    /// <summary>Страница EN-протокола, как её отдаёт loglig: заголовок с ивритским полом.</summary>
    private static string[] EnPage(string headerCategory) =>
    [
        "\"Arena\" Israel National Winter Swimming Championships - Youth and Seniors",
        "2025\"",
        "23/12/2025 - 27/12/2025",
        "Results",
        $"50m Freestyle - {headerCategory}",
        "26/12/2025 09:06",
        "Year Of International",
        "Rank Heat Lane Last name First name Club Result",
        "Birth Score",
        "1 6 5 BACHAR MICHAEL 2011 Hapoel Emek Hefer 00:24.05 566",
        "2 6 4 Versolker Amit 2011 Maccabi Rishon Seals 00:24.30 549",
    ];

    [Theory]
    [InlineData("15 םינב", "male", "15")]     // «15 בנים» (реверс)
    [InlineData("15 תונב", "female", "15")]   // «15 בנות»
    [InlineData("17-18 םירבג", "male", "17-18")]   // «17-18 גברים»
    [InlineData("17-18 םישנ", "female", "17-18")]       // «17-18 נשים»
    [InlineData("בנים 15", "male", "15")]     // и в прямом порядке тоже
    public void UntranslatedHebrewGender_IsRecognized(string headerCategory, string gender, string age)
    {
        var comp = Assert.Single(
            IsrOrgCompetitionParser.ParseLines([EnPage(headerCategory)], "EN").ToList());

        Assert.Equal(gender, comp.EventStyleGender);
        Assert.Equal(age, comp.EventStyleAge);

        // Главное следствие: категория — возрастная полоса, а не выдуманная «mix-15».
        Assert.Equal(age, IsrOrgParser.NormalizeEventCategory(comp.EventStyleAge, comp.EventStyleGender));
    }

    [Fact]
    public void TranslatedHeaders_StillWork()
    {
        var comp = Assert.Single(IsrOrgCompetitionParser.ParseLines([EnPage("Men 19-99")], "EN").ToList());

        Assert.Equal("male", comp.EventStyleGender);
        Assert.Equal("19-99", comp.EventStyleAge);
    }

    /// <summary>
    /// Ивритское «מיקס» полом не является — смешанный заплыв обязан остаться смешанным.
    /// </summary>
    [Fact]
    public void HebrewMix_StaysGenderless()
    {
        var comp = Assert.Single(
            IsrOrgCompetitionParser.ParseLines([EnPage("13-99 סקימ")], "EN").ToList());

        Assert.Equal("none", comp.EventStyleGender);
        Assert.Equal("mix-13-99",
            IsrOrgParser.NormalizeEventCategory(comp.EventStyleAge, comp.EventStyleGender));
    }
}
