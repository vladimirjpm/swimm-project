using Swimm.Parsing.Parsers.Regulation;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Длина бассейна из регламента — все варианты записи в одном классе (<see cref="PoolLengthFinder"/>).
/// Новый способ записи в регламенте федерации — новый кейс здесь. Строки «перевёрнутые» — сырой
/// текст PdfPig: слова задом наперёд, цифры в нормальном порядке.
/// </summary>
public class PoolLengthFinderTests
{
    // ── надёжно: с числом дорожек ────────────────────────────────────────────

    [Theory]
    [InlineData("ות: 03.07.2026 מקום התחרות: בריכת מכבי - 25 מ', 8 מסלולים אגו", "25m", "lanes")]
    [InlineData("בב םייקתת תורחתה תכירב50 , רטמ10 םילולסמ םיכיראתה ןיב 23-27", "50m", "lanes-reversed")]
    [InlineData("םוקמ :תורחתה  כירבה  תימואל ןוכמ טייגניו -  50 ,'מ  10  םילולסמ הדוגא", "50m", "lanes-reversed")]
    [InlineData("םוקמ תורחתה:  תכירב  םורמ הוונ תמר ןג -  25 מ,' 5  םילולסמ הדוגא תנגראמ: יבכמ", "25m", "lanes-reversed")]
    [InlineData("רעי תומדקומה- תכירב( טייגניו50  ,'מ10 :םיאבה םיכיראתה ןיב ,)םילולסמ  םוישישי", "50m", "lanes-reversed")]
    public void Lanes(string line, string expected, string rule)
    {
        var hit = Assert.Single(PoolLengthFinder.FindInLine(line));
        Assert.Equal(expected, hit.Length);
        Assert.Equal(rule, hit.Rule);
        Assert.Equal(PoolEvidence.Lanes, hit.Evidence);
    }

    // ── надёжно: «בריכת N» вплотную ──────────────────────────────────────────

    [Theory]
    [InlineData("התחרות תתקיים בבריכת 25 מ' במכון וינגייט", "25m", "pool-of")]
    [InlineData("התחרות תתקיים בבריכת 50 מטר", "50m", "pool-of")]
    // «ההזדמנות האחרונה» 2023, loglig doc 1548.
    [InlineData("וכמב תימואלה הכירבב םייקתת תורחתה  תכירב25 יראתב 'מםיכ  .12.202323-22 .", "25m", "pool-of-reversed")]
    [InlineData("22-23.12.2023,    ,טייגניו ןוכמ תכירבב25   תכירבב םינמז תעיבקל הנורחא תונמדזה", "25m", "pool-of-reversed")]
    public void PoolOf(string line, string expected, string rule)
    {
        var hit = Assert.Single(PoolLengthFinder.FindInLine(line));
        Assert.Equal(expected, hit.Length);
        Assert.Equal(rule, hit.Rule);
        Assert.Equal(PoolEvidence.PoolOf, hit.Evidence);
    }

    // ── слабее: фраза о месте, «бассейн» и «N метров» не вплотную ────────────

    [Theory]
    // Чемпионат Израиля на 5 км в бассейне, 12.01.2024 (compID 486, loglig doc 1541) — показал Влад
    // 25.11.2026: в регламенте «בריכת 50 מ'», а превью писало «выбери — без бассейна импорт не начнётся».
    // В сыром тексте «תכירב» и «50 … 'מ» разнесены, между числом и метром — обрывок «ב».
    [InlineData("  :תורחתה םוקימו דעומ  תכירב ,טייגניו ןוכמב תימואלה הכירבב םייקתת תורחתה50 ב 'מה ישיש םוי-  12.1.24 .", "50m", "venue-reversed")]
    [InlineData("התחרות תתקיים בבריכה הלאומית במכון וינגייט, בריכה באורך 50 מטר", "50m", "venue")]
    [InlineData("מקום התחרות: בריכה עירונית, אורך 25 מ'", "25m", "venue")]
    public void VenuePhrase(string line, string expected, string rule)
    {
        var hit = Assert.Single(PoolLengthFinder.FindInLine(line));
        Assert.Equal(expected, hit.Length);
        Assert.Equal(rule, hit.Rule);
        Assert.Equal(PoolEvidence.Venue, hit.Evidence);
    }

    // ── чего не считаем длиной бассейна ──────────────────────────────────────

    [Theory]
    [InlineData("מקצים: 50 מטר חופשי, 100 מטר גב")]                       // дистанции программы
    [InlineData("150 מטר, 10 מסלולים")]                                    // не 50: перед числом цифра
    [InlineData("בריכת 250 מטר")]                                          // не 25: дальше цифра
    [InlineData("לדוג רדסב( 50  ,'מ2 )האלה ןכו האמל")]                      // «50 м» в правилах, бассейна рядом нет
    // Условие допуска из того же регламента 1541: «800 ו-1500 חופשי (בריכת 50 מ')» — где показано
    // время, а не где плывут. Перед числом вплотную — название стиля.
    [InlineData("ימואלניבה דוקינה ילעב םינייחש800  ו-1500    תכירב( ישפוח50    ןיבש הפוקתב )'מ1.1.23    דע1.1.24")]
    [InlineData("םייקתת תורחתה ירוגטקב םינייחש יחשמב  400  ,800  ו-1500    תכירב( ישפוח50    )'מ  הפוקתב  ןיבש1.6.23")]
    // Фраза о месте есть, но «50 מטר» рядом — дистанция.
    [InlineData("התחרות תתקיים בבריכת וינגייט. מקצים: 50 מטר חופשי, 50 מטר גב, 4X50 מטר שליחים")]
    [InlineData("בריכה 25")]                                               // ни метра, ни дорожек
    public void NotAPoolLength(string line) =>
        Assert.Empty(PoolLengthFinder.FindInLine(line));

    // ── выбор по всему тексту ────────────────────────────────────────────────

    [Fact]
    public void StrongEvidenceWins_OverVenuePhrase()
    {
        // Надёжное «25 מ', 8 מסלולים» есть — слабая фраза про 50 м (например, о другом бассейне) не в счёт.
        var text = "מקום התחרות: בריכת מכבי - 25 מ', 8 מסלולים\nהחימום יתקיים בבריכה הסמוכה, מיקום: אורך 50 מטר";

        var hits = PoolLengthFinder.Find(text);

        Assert.All(hits, h => Assert.NotEqual(PoolEvidence.Venue, h.Hit.Evidence));
        Assert.Equal("25m", PoolLengthFinder.Resolve(text));
    }

    [Fact]
    public void VenuePhraseAlone_IsEnough() =>
        Assert.Equal("50m", PoolLengthFinder.Resolve(
            "  :תורחתה םוקימו דעומ  תכירב ,טייגניו ןוכמב תימואלה הכירבב םייקתת תורחתה50 ב 'מה ישיש םוי-  12.1.24 ."));

    [Theory]
    [InlineData("בריכה 25 מ', 8 מסלולים; בריכה 50 מ', 10 מסלולים")]        // обе длины — не угадываем
    [InlineData("בבריכת 25 מ' ובבריכת 50 מ'")]
    [InlineData("מקצים: 50 מטר חופשי")]                                    // ничего не нашли
    [InlineData("")]
    public void TwoLengthsOrNone_NotGuessed(string text) =>
        Assert.Null(PoolLengthFinder.Resolve(text));

    [Fact]
    public void OnePlaceInLine_OneHit()
    {
        // «בריכת 50 מטר, 10 מסלולים» подходит и под «дорожки», и под «בריכת N» — находка одна, надёжнейшая.
        var hit = Assert.Single(PoolLengthFinder.FindInLine("התחרות תתקיים בבריכת 50 מטר, 10 מסלולים"));
        Assert.Equal(PoolEvidence.Lanes, hit.Evidence);
    }
}
