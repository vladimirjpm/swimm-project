using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сопоставление имени из пособытийного источника loglig с пловцом в БД (шаг 3,
/// docs/data-integrity.md §10). Каждый кейс — из живого прогона по соревнованию 1581.
/// </summary>
public class LogligSwimmerNameResolverTests
{
    private static LogligSwimmerNameResolver Resolver(params KnownSwimmerName[] known) => new(known);

    /// <summary>Порядок токенов у источников разный: сайт «имя фамилия», PDF «фамилия имя».</summary>
    [Fact]
    public void ExactTokenSet_MatchesRegardlessOfOrder()
    {
        var resolver = Resolver(new KnownSwimmerName("אוגינץ", "מיכל", 2012, "הפועל בית שמש"));

        var r = resolver.Resolve("מיכל אוגינץ", 2012, "הפועל בית שמש");

        Assert.True(r.Matched);
        Assert.Equal("אוגינץ", r.LastName);
        Assert.Equal("מיכל", r.FirstName);
    }

    /// <summary>
    /// Апострофы: у сайта ASCII, у нас ивритский герш (U+05F3). На живом прогоне это
    /// «теряло» 25 имён из 39 — чистая пунктуация, а не разные люди.
    /// </summary>
    [Fact]
    public void ApostropheVariants_AreTheSameName()
    {
        var resolver = Resolver(new KnownSwimmerName("זאבורוייב", "אנג׳לה", 2012, "בני הרצליה"));

        Assert.True(resolver.Resolve("אנג'לה זאבורוייב", 2012, "בני הרצליה").Matched);
    }

    /// <summary>
    /// У сайта имя бывает ПОЛНЕЕ, чем в PDF: «אבינעם יצחק גבאי» против «אבינעם גבאי».
    /// Вложенность наборов при том же годе — та же личность, а не второй пловец.
    /// </summary>
    [Fact]
    public void LongerNameFromSource_MatchesShorterInDb()
    {
        var resolver = Resolver(new KnownSwimmerName("גבאי", "אבינעם", 2010, "מועדון שחייה כפר תבור"));

        var r = resolver.Resolve("אבינעם יצחק גבאי", 2010, "מועדון שחייה כפר תבור");

        Assert.True(r.Matched);
        Assert.Equal("גבאי", r.LastName);
        Assert.Equal("אבינעם", r.FirstName);   // берём поля БД, не разрезку источника
    }

    /// <summary>Двойное имя не должно разъезжаться: «לי חן עובדיה» — фамилия одна, имя из двух слов.</summary>
    [Fact]
    public void CompoundFirstName_KeepsDbSplit()
    {
        var resolver = Resolver(new KnownSwimmerName("עובדיה", "לי חן", 2012, "מכבי וייסגל רחובות"));

        var r = resolver.Resolve("לי חן עובדיה", 2012, "מכבי וייסגל רחובות");

        Assert.True(r.Matched);
        Assert.Equal("עובדיה", r.LastName);
        Assert.Equal("לי חן", r.FirstName);
    }

    /// <summary>Год рождения разводит тёзок — иначе склеили бы двух разных детей (инцидент И-11).</summary>
    [Fact]
    public void SameNameDifferentBirthYear_IsNotAMatch()
    {
        var resolver = Resolver(new KnownSwimmerName("כהן", "טל", 2012, "מכבי חיפה"));

        Assert.False(resolver.Resolve("טל כהן", 2015, "מכבי חיפה").Matched);
    }

    /// <summary>Несколько кандидатов одного года — тайбрейк по клубу.</summary>
    [Fact]
    public void AmbiguousCandidates_AreSplitByClub()
    {
        var resolver = Resolver(
            new KnownSwimmerName("כהן", "טל", 2012, "מכבי חיפה"),
            new KnownSwimmerName("כהן", "טל אור", 2012, "בני הרצליה"));

        var r = resolver.Resolve("טל אור כהן", 2012, "בני הרצליה");

        Assert.True(r.Matched);
        Assert.Equal("טל אור", r.FirstName);
    }

    /// <summary>
    /// Пары нет — режем эвристикой (последний токен фамилия) и ЧЕСТНО помечаем Matched=false:
    /// импорт такой строки заведёт нового пловца, и это должно быть видно в отчёте.
    /// </summary>
    [Fact]
    public void Unknown_IsSplitHeuristically_AndFlagged()
    {
        var resolver = Resolver(new KnownSwimmerName("אוגינץ", "מיכל", 2012, "הפועל בית שמש"));

        var r = resolver.Resolve("נועה בר לוי", 2013, "מכבי נהריה");

        Assert.False(r.Matched);
        Assert.Equal("לוי", r.LastName);
        Assert.Equal("נועה בר", r.FirstName);
    }
}
