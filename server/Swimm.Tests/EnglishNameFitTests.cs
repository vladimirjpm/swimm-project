using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Подходит ли английское имя пловца к ивритскому (10.10.2026). Все случаи — настоящие карточки
/// базы: сдвиг EN-имён соседей в паре HE+EN (comp 1486), иврит в латинском поле от потерянной
/// EN-фамилии, близнецы с переставленными именами — и то, что ошибкой НЕ является: второе имя
/// в одном из языков, «ו» как V, славянские окончания.
/// </summary>
public class EnglishNameFitTests
{
    [Theory]
    [InlineData("ווייס", "עמיחי", "WEISS", "AMICHAI")]
    [InlineData("פלדמן איתן", "צבי", "FELDMAN", "EITAN")]            // второго имени на loglig нет
    [InlineData("לוין", "דוד", "LEVIN", "DAVID")]                    // «ו» = V
    [InlineData("ויסמן", "ליאב", "WAIZMAN", "LIAV")]
    [InlineData("אלטשול", "גילי", "ALTSHUL", "GILI")]                // «TS» — два звука
    [InlineData("ליבוביץ", "גיל", "LEIBOVITCH", "GIL")]             // «-TCH» = ץ
    [InlineData("יעקובוביץ", "מרכוס", "JAKUBOVICH", "MARCUS")]
    [InlineData("פקטה מתן", "אילן", "MATANILAN", "KAPTA")]          // склейка — сравнивается с парой слов
    [InlineData("קיסלוב", "אווה", "KISLOV", "EVA")]                 // «EVA» без согласных не сравнивается
    [InlineData("אבן חן נויבואר", "איתן", "EVEN CHEN NEUBAUER", "EYTAN")]
    public void Fits(string heLast, string heFirst, string enLast, string enFirst) =>
        Assert.Equal(EnglishNameVerdict.Fits, EnglishNameFit.Assess(heLast, heFirst, enLast, enFirst).Verdict);

    [Theory]
    [InlineData("פוגל דודוביץ", "איתן", "ELKIN", "DANIEL", "ELKIN")]   // имя соседа по блоку неявок
    [InlineData("גבאלי", "עבדאלרחמן", "NATOUR", "AHMAD", "NATOUR")]
    [InlineData("דויד", "יבסייב", "Yavsayev", "Netanel", "Netanel")]   // близнецы, имена переставлены
    public void Foreign(string heLast, string heFirst, string enLast, string enFirst, string word)
    {
        var (verdict, w, _) = EnglishNameFit.Assess(heLast, heFirst, enLast, enFirst);
        Assert.Equal(EnglishNameVerdict.Foreign, verdict);
        Assert.Equal(word, w);
    }

    [Theory]
    [InlineData("סגרון", "נועם", "סגרון", "EYTAN")]
    [InlineData("ברוטמן", "מיכאל", "ברוטמן", "BRUTMAN")]
    public void HebrewInLatin(string heLast, string heFirst, string enLast, string enFirst) =>
        Assert.Equal(EnglishNameVerdict.HebrewInLatin, EnglishNameFit.Assess(heLast, heFirst, enLast, enFirst).Verdict);

    [Theory]
    [InlineData("", "", "COHEN", "DANA")]      // ивритского имени нет — сравнивать не с чем
    [InlineData("כהן", "דנה", "", "")]          // английского нет
    public void Nothing(string heLast, string heFirst, string enLast, string enFirst) =>
        Assert.Equal(EnglishNameVerdict.Nothing, EnglishNameFit.Assess(heLast, heFirst, enLast, enFirst).Verdict);

    [Theory]
    [InlineData("סגרון", "נועם", "SIGRON NOAM", "SIGRON", "NOAM")]                       // фамилия первой
    [InlineData("ברוטמן", "מיכאל", "MICHEL LEIB BRUTMAN", "BRUTMAN", "MICHEL LEIB")]     // второе имя
    [InlineData("אבן חן נויבואר", "איתן", "EYTAN EVEN CHEN NEUBAUER", "EVEN CHEN NEUBAUER", "EYTAN")]
    [InlineData("ווייס", "עמיחי", "AMICHAI WEISS", "WEISS", "AMICHAI")]
    public void Split_LoglIgCard(string heLast, string heFirst, string card, string last, string first) =>
        Assert.Equal((last, first), EnglishNameFit.SplitLatinName(heLast, heFirst, card));

    [Theory]
    [InlineData("פוגל דודוביץ", "איתן", "ADAM FOGEL")]   // имя на карточке другое — не наш разрез
    [InlineData("כהן", "דנה", "COHEN")]                   // одно слово — резать нечего
    [InlineData("כהן", "דנה", "כהן דנה")]
    public void Split_Refuses(string heLast, string heFirst, string card) =>
        Assert.Null(EnglishNameFit.SplitLatinName(heLast, heFirst, card));
}
