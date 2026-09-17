using Swimm.Parsing.Parsers.WorldRecords;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Состав эстафеты в отчётах World Aquatics (`WorldRecordsParser.ParseRelayRoster`).
///
/// Зачем эти тесты вообще: до 17.09.2026 состав разбирался тем же кодом, что имя одного
/// спортсмена, и получалось «Alain, LEVEAUX Amaury, GILOT Fabien, BOUSQUET Frederick Bernard» —
/// фамилия первой ноги уезжала в конец строки. Вторая беда — источник тасует ноги между
/// прогонами, из-за чего эстафетные строки показывались изменившимися всегда.
/// Оба случая закрыты здесь регрессией, потому что оба видны только на реальных данных
/// и оба тихие: они не роняют импорт, а молча портят строку.
/// </summary>
public class WorldRecordsRelayRosterTests
{
    [Fact]
    public void Roster_PutsGivenNameBeforeSurname_ForEveryLeg()
    {
        var roster = WorldRecordsParser.ParseRelayRoster(
            "BERNARD Alain, LEVEAUX Amaury, GILOT Fabien, BOUSQUET Frederick");

        // Ни одной ноги в исходном «ФАМИЛИЯ Имя», и фамилия первой не уехала в конец строки.
        Assert.Equal("Alain Bernard, Amaury Leveaux, Fabien Gilot, Frederick Bousquet", roster);
    }

    /// <summary>
    /// Главное свойство: тот же состав в другом порядке даёт ту же строку. На этом держится
    /// и «изменившихся мало = источник ничего не поправил», и сравнение выгрузок в архиве.
    /// </summary>
    [Fact]
    public void Roster_IsStable_WhenSourceShufflesLegs()
    {
        var asFetchedYesterday = WorldRecordsParser.ParseRelayRoster(
            "BERNARD Alain, LEVEAUX Amaury, GILOT Fabien, BOUSQUET Frederick");
        var asFetchedToday = WorldRecordsParser.ParseRelayRoster(
            "LEVEAUX Amaury, BOUSQUET Frederick, BERNARD Alain, GILOT Fabien");

        Assert.Equal(asFetchedYesterday, asFetchedToday);
    }

    /// <summary>
    /// Составные фамилии («DE WAARD», «DI PIETRO») и апостроф — живые строки отчётов WR SCM.
    /// Разбор ноги обязан оставаться тем же, что у одиночного спортсмена.
    /// </summary>
    [Fact]
    public void Roster_KeepsMultiWordSurnamesTogether()
    {
        var roster = WorldRecordsParser.ParseRelayRoster(
            "BUSCH Kim, KROMOWIDJOJO Ranomi, HEEMSKERK Femke, DE WAARD Maaike");

        Assert.Contains("Maaike De Waard", roster);
        Assert.Equal("Femke Heemskerk, Kim Busch, Maaike De Waard, Ranomi Kromowidjojo", roster);
    }

    [Fact]
    public void Roster_HandlesApostrophe()
    {
        var roster = WorldRecordsParser.ParseRelayRoster(
            "O'CALLAGHAN Mollie, HODGES Chelsea, MCKEON Emma, WILSON Madison");

        Assert.Contains("Mollie O'callaghan", roster);
    }

    /// <summary>
    /// Пустая клетка «Athlete» у эстафеты — штатный случай источника (у 132 иностранных строк
    /// держателя нет вовсе). Должна дать пустую строку, а не мусор и не исключение.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" , , ")]
    public void Roster_IsEmpty_WhenSourceGivesNoNames(string athlete)
        => Assert.Equal("", WorldRecordsParser.ParseRelayRoster(athlete));

    /// <summary>Лишние пробелы и хвостовая запятая источника не должны плодить пустых ног.</summary>
    [Fact]
    public void Roster_IgnoresStrayCommasAndSpaces()
    {
        var roster = WorldRecordsParser.ParseRelayRoster(
            "  SMITH Kieran ,  HOBSON Luke,, GUILIANO Chris , ALEXY Jack , ");

        Assert.Equal("Chris Guiliano, Jack Alexy, Kieran Smith, Luke Hobson", roster);
    }
}
