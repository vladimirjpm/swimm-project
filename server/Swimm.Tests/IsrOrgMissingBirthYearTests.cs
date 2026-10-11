using System.Collections.Generic;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Строка результата БЕЗ года рождения. Живой случай — comp 6595 (мастерс, зима 2025,
/// loglig 11586, стр. 5), секция «כללי» 50 на спине: у ויקטור בלכסן колонка года пустая.
/// Разбор держался за год как за границу «имя | клуб» и падал с
/// <c>Index was outside the bounds of the array</c> — импорт всего соревнования вставал.
/// Теперь год = 0 (как неизвестный год в базе), а граница ищется по клубам протокола.
/// </summary>
public class IsrOrgMissingBirthYearTests
{
    private const string Line = "1 1 7 בלכסן ויקטור מכבי חיפה 00:27.93 496";

    [Fact]
    public void NoYear_KnownClub_SplitsNameAndClubByTheClub()
    {
        var known = new HashSet<string> { "מכבי חיפה", "אקוותיקים" };

        var r = IsrOrgResultLineParser.ParseResultLine(Line, known);

        Assert.Equal(0, r.BirthYear);
        Assert.Equal("בלכסן", r.LastName);
        Assert.Equal("ויקטור", r.FirstName);
        Assert.Equal("מכבי חיפה", r.Club);
        Assert.Equal("00:27.93", r.Time);
        Assert.Equal(496, r.InternationalPoints);
        Assert.Equal(1, r.Position);
    }

    [Fact]
    public void NoYear_KnownClub_KeepsALongSurname()
    {
        // Фамилия из двух слов: без клуба-якоря правило «два слова» отрезало бы её.
        var known = new HashSet<string> { "אקוותיקים" };

        var r = IsrOrgResultLineParser.ParseResultLine(
            "1 2 2 בריל אדלמן פולה אקוותיקים 01:06.92 53", known);

        Assert.Equal("בריל אדלמן", r.LastName);
        Assert.Equal("פולה", r.FirstName);
        Assert.Equal("אקוותיקים", r.Club);
    }

    [Fact]
    public void NoYear_UnknownClub_TakesTwoWordName()
    {
        var r = IsrOrgResultLineParser.ParseResultLine(Line);

        Assert.Equal(0, r.BirthYear);
        Assert.Equal("בלכסן", r.LastName);
        Assert.Equal("ויקטור", r.FirstName);
        Assert.Equal("מכבי חיפה", r.Club);
    }

    [Fact]
    public void NoYear_NoShow_StillParses()
    {
        var r = IsrOrgResultLineParser.ParseResultLine(
            "- 3 8 ברנשטיין לאה מכבי פ.מ. רעות NS 0", new HashSet<string> { "מכבי פ.מ. רעות" });

        Assert.Null(r.Position);
        Assert.Equal(0, r.BirthYear);
        Assert.Equal("ברנשטיין", r.LastName);
        Assert.Equal("מכבי פ.מ. רעות", r.Club);
        Assert.Null(r.Time);
        Assert.Equal("NS", r.TimeFailNote);
    }

    [Fact]
    public void WithYear_UnchangedByKnownClubs()
    {
        var r = IsrOrgResultLineParser.ParseResultLine(
            "1 3 2 פישל-בודנר אלן 1957 אקוותיקים 00:53.15 107", new HashSet<string> { "x" });

        Assert.Equal(1957, r.BirthYear);
        Assert.Equal("פישל-בודנר", r.LastName);
        Assert.Equal("אקוותיקים", r.Club);
    }
}
