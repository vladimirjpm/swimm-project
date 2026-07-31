using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Возраст эстафетной строки = возрастная ПОЛОСА заплыва из заголовка протокола, а не возраст
/// первой ноги. Регрессия 2026-07-31 (кейс Влада, event 13): в заплыве «בנות 11-12» строки
/// получали то 11, то 12 — по порядку ног, — и клиентский isRecordTime сравнивал «11» с
/// рекордом возраста 11 (02:15.73) вместо возраста 12 (01:58.55). Бейдж «NEW RECORD» висел на
/// 4-м и 5-м местах, а победитель заплыва оставался без бейджа. Подробности — docs/relays.md.
/// </summary>
public class IsrOrgRelayEventAgeTests
{
    /// <summary>
    /// Детская полоса из заголовка побеждает возраст первой ноги — иначе строки ОДНОГО
    /// заплыва разъезжаются по разным осям рекордов и по разным ключам объединённого зачёта.
    /// </summary>
    [Theory]
    [InlineData("11-12", 11, "11-12")]   // первая нога младшая — было "11"
    [InlineData("11-12", 12, "11-12")]   // первая нога старшая — было "12"
    [InlineData("13-14", 13, "13-14")]
    [InlineData("9-10", 10, "9-10")]
    [InlineData("17-18", 17, "17-18")]
    [InlineData(" 11-12 ", 11, "11-12")] // пробелы из PDF
    public void YouthBand_FromHeader_WinsOverFirstLegAge(string headerAge, int firstSwimmerAge, string expected)
        => Assert.Equal(expected, IsrOrgParser.RelayEventAge(headerAge, firstSwimmerAge));

    /// <summary>
    /// Masters-полосы возрастом НЕ подменяются: на числе держится masters-логика
    /// (isResultMasters делает Number(...), и «25-29» прочиталось бы как NaN).
    /// </summary>
    [Theory]
    [InlineData("25-29", 27, "27")]
    [InlineData("50-54", 52, "52")]
    [InlineData("19-24", 21, "21")]
    public void MastersBand_KeepsNumericAge(string headerAge, int firstSwimmerAge, string expected)
        => Assert.Equal(expected, IsrOrgParser.RelayEventAge(headerAge, firstSwimmerAge));

    /// <summary>
    /// Заголовок без полосы (masters-эстафеты без возраста, одиночный возраст, мусор) —
    /// фоллбек на вычисленный возраст, поведение как до фикса.
    /// </summary>
    [Theory]
    [InlineData(null, 14, "14")]
    [InlineData("", 14, "14")]
    [InlineData("   ", 14, "14")]
    [InlineData("12", 12, "12")]        // одиночный возраст — не полоса
    [InlineData("U17", 16, "16")]       // английская категория Маккабиады
    [InlineData("18-99", 30, "30")]     // «полоса» шире детской — верхняя граница > 18
    public void NoYouthBand_FallsBackToComputedAge(string? headerAge, int firstSwimmerAge, string expected)
        => Assert.Equal(expected, IsrOrgParser.RelayEventAge(headerAge, firstSwimmerAge));
}
