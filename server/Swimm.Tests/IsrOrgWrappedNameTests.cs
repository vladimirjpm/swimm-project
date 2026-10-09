using System;
using System.IO;
using System.Linq;
using Swimm.Parsing.Helpers;
using Swimm.Parsing.Parsers;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// И-48: имя, перенесённое в ячейке протокола на две строки, склеивалось без пробела
/// («SASONTASHKENT», «HENROSENTAL», «גנדלריזהר»), а перенесённое ИМЯ ложилось в фамилию
/// («YEHONATAN» в LastNameEn). Сверка с loglig 08.10.2026 (И-47) нашла 113 склеенных и 34
/// переставленных EN-имени. Фикстура — настоящая пара loglig 14220 (comp 57, «ארנה» центр,
/// мальчики, 16/02/2026): 100 комплекс 10 лет и 100 на спине 10–11 лет, HE и EN.
/// </summary>
public class IsrOrgWrappedNameTests
{
    private static string Fixture(string lang) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", $"ArenaCenterBoys2026_57_wrapped_names_{lang}.pdf");

    [Theory]
    [InlineData("KIDRON", "MERIDOR", "KIDRON MERIDOR")]   // перенос по пробелу
    [InlineData("HEN", "ROSENTAL", "HEN ROSENTAL")]
    [InlineData("SCHWARTZ", "ZIGLER", "SCHWARTZ ZIGLER")] // длинная первая часть, но хвост — слово
    [InlineData("שלמה", "איתן", "שלמה איתן")]
    [InlineData("TSCHERKOWS", "KI", "TSCHERKOWSKI")]      // посреди слова: ячейка заполнена, хвост короткий
    [InlineData("GERSHKOV", "ICH", "GERSHKOVICH")]
    [InlineData("OLEKSAND", "R", "OLEKSANDR")]
    [InlineData("BEN", "ZVI", "BEN ZVI")]                 // короткий хвост, но первая часть короткая
    public void JoinWrappedName_SpaceOrGlue(string before, string after, string expected) =>
        Assert.Equal(expected, IsrOrgCompetitionParser.JoinWrappedName(before, after));

    private static IsrOrgResult Row(string last, string first, IsrOrgNameWrap? wrap = null) =>
        new("ISR", 1, 1, 4, last, first, 2016, "club", "01:00.00", null, 0, null, null, null, null) { NameWrap = wrap };

    [Fact]
    public void Resolve_WrappedSurname_SpaceFromHebrewWordCount()
    {
        // Длинная первая часть + короткая вторая парсер бы склеил; два ивритских слова — пробел.
        var en = Row("SCHWARTZBAR", "NOA", new IsrOrgNameWrap("SCHWARTZ", "BAR"));
        var he = Row("שורץ בר", "נועה");
        Assert.Equal(("SCHWARTZ BAR", "NOA"), IsrOrgParser.ResolveWrappedEnName(en, he));
    }

    [Fact]
    public void Resolve_WrappedFirstName_GoesToFirstName()
    {
        // Перенесено ИМЯ: парсер положил его в фамилию, а фамилию строки данных — в имя.
        var en = Row("OLEKSANDR", "MYKHALKIN", new IsrOrgNameWrap("OLEKSAND", "R"));
        var he = Row("מיכלקין", "אלכסנדר");
        Assert.Equal(("MYKHALKIN", "OLEKSANDR"), IsrOrgParser.ResolveWrappedEnName(en, he));
    }

    [Fact]
    public void Resolve_OneHebrewWord_DoesNotForceGlue()
    {
        // Второе имя есть только в EN («יהונתן» ↔ «YONATHAN SHLOMO») — пробел парсера остаётся.
        var en = Row("YONATHAN SHLOMO", "COHEN", new IsrOrgNameWrap("YONATHAN", "SHLOMO"));
        var he = Row("כהן", "יהונתן");
        Assert.Equal(("COHEN", "YONATHAN SHLOMO"), IsrOrgParser.ResolveWrappedEnName(en, he));
    }

    [Fact]
    public void Resolve_NoWrap_AsParsed()
    {
        var en = Row("COHEN", "DANA");
        Assert.Equal(("COHEN", "DANA"), IsrOrgParser.ResolveWrappedEnName(en, Row("כהן", "דנה")));
    }

    [Theory]
    [InlineData("שדמן", "SHADMAN", true)]
    [InlineData("ששון טשקנט", "SASON TASHKENT", true)]
    [InlineData("מיכלקין", "MYKHALKIN", true)]
    [InlineData("מיכלקין", "OLEKSANDR", false)]
    [InlineData("אלכסנדר", "OLEKSANDR", true)]
    public void Transliteration_SameWordScoresHigh(string he, string en, bool same) =>
        Assert.Equal(same, NameTransliteration.Similarity(he, en) >= 0.7);

    [Fact]
    public void RealPair_WrappedNames_SpacedAndInTheRightField()
    {
        using var he = File.OpenRead(Fixture("HE"));
        using var en = File.OpenRead(Fixture("EN"));
        var rows = new IsrOrgParser()
            .Parse(new ParseRequest(he, "x-he.pdf", en, "x-en.pdf", Language: "he"))
            .ToList();

        (string, string) En(string heLast, string heFirst) =>
            rows.Where(r => r.LastName == heLast && r.FirstName == heFirst).Select(r => (r.LastNameEn, r.FirstNameEn)).Distinct().Single();

        Assert.Equal(("SASON TASHKENT", "NETA"), En("ששון טשקנט", "נטע"));
        Assert.Equal(("SHADMAN MELLER", "ITAMAR"), En("שדמן מלר", "איתמר"));
        Assert.Equal(("GLOTOV DAVYDOV", "FADDEI"), En("גלוטוב דוידוב", "פאדיי"));
        Assert.Equal(("MYKHALKIN", "OLEKSANDR"), En("מיכלקין", "אלכסנדר"));
        Assert.Equal(("COHEN", "YONATHAN SHLOMO"), En("כהן", "יהונתן"));
        // Ивритская граница неверна («ויזן אדם | דניאל»), EN-поля — как у loglig.
        Assert.Equal(("VIZEN", "ADAM DANIEL"), En("ויזן אדם", "דניאל"));
    }
}
